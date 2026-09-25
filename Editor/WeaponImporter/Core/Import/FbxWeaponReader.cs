#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Formats.Fbx;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Import;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Reads an FBX weapon: skeleton and clips through <see cref="FbxImporter"/>, plus every mesh
/// in bind pose with its dominant bone, all converted to s&amp;box model space.
/// </summary>
public static class FbxWeaponReader
{
    public const int MaxTriangles = 600_000;

    public static WeaponAsset Read(byte[] data, string name, string sourcePath = "", float sampleFps = 30f)
    {
        var tree = FbxTokenizer.Parse(data);
        var scene = FbxScene.Build(tree);
        var unit = (float)scene.UnitScaleFactor; // file units -> cm

        SourceScene? imported = null;
        var notes = new List<string>();
        try
        {
            imported = FbxImporter.Import(data, new FbxImportOptions { SampleFps = sampleFps });
            notes.AddRange(imported.Notes);
        }
        catch (FormatException e) when (e.Message.Contains("no skeleton", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("No skeleton in the file; the weapon is imported as a static prop.");
        }

        var conversion = imported is not null
            ? SpaceConversion.For(imported)
            : new SpaceConversion(scene.UpAxis, scene.UpAxisSign, scene.FrontAxis, scene.FrontAxisSign, scene.CoordAxis, scene.CoordAxisSign);

        var skeletonCm = imported?.Skeleton ?? WeaponAsset.SingleBone(name.Length > 0 ? "root" : "root");
        var skeleton = conversion.Skeleton(skeletonCm);
        var clips = imported is null ? new List<Clip>() : conversion.Clips(imported.Clips, skeletonCm, skeleton);

        var links = tree.Child("Connections")?.ChildrenNamed("C")
            .Where(n => n.Prop<string>(0) == "OO")
            .Select(n => (Child: n.Prop<long>(1), Parent: n.Prop<long>(2)))
            .ToArray() ?? Array.Empty<(long Child, long Parent)>();
        IEnumerable<FbxObject> Children(long id) => links.Where(c => c.Parent == id && scene.ObjectsById.ContainsKey(c.Child)).Select(c => scene.ObjectsById[c.Child]);
        IEnumerable<FbxObject> Parents(long id) => links.Where(c => c.Child == id && scene.ObjectsById.ContainsKey(c.Parent)).Select(c => scene.ObjectsById[c.Parent]);

        Matrix4x4 World(FbxObject? node)
        {
            var m = Matrix4x4.Identity;
            var seen = new HashSet<long>();
            while (node is not null)
            {
                if (!seen.Add(node.Id))
                    throw new FormatException("FBX node hierarchy contains a cycle.");
                m *= FbxTransform.FromModel(scene, node).LocalMatrixDefault();
                node = node.ModelParent;
            }
            return m;
        }

        var materialReader = new FbxMaterialReader(tree, scene, sourcePath);

        var positions = new List<Vector3>();
        var vertexBone = new List<int>();
        var indices = new List<int>();
        var triPart = new List<int>();
        var partNames = new List<string>();
        var cornerNormals = new List<Vector3>();
        var cornerUVs = new List<Vector2>();
        var triMaterial = new List<int>();
        var anyNormals = false;
        var anyUVs = false;
        var missingNormals = false;

        foreach (var geometry in scene.ObjectsById.Values.Where(o => o.NodeType == "Geometry" && o.SubClass == "Mesh"))
        {
            var verts = geometry.Node.Child("Vertices")?.AsDoubleArray(0);
            var polys = geometry.Node.Child("PolygonVertexIndex")?.AsIntArray(0);
            if (verts is null || polys is null || verts.Length < 9 || verts.Length % 3 != 0)
                continue;
            var owner = Parents(geometry.Id).FirstOrDefault(o => o.NodeType == "Model");
            if (owner is null)
                continue;

            var count = verts.Length / 3;
            var geometric = Matrix4x4.CreateScale(scene.GetVector3(owner, "GeometricScaling", Vector3.One))
                * Matrix4x4.CreateFromQuaternion(FbxTransform.EulerDegreesToQuaternion(scene.GetVector3(owner, "GeometricRotation", Vector3.Zero), 0))
                * Matrix4x4.CreateTranslation(scene.GetVector3(owner, "GeometricTranslation", Vector3.Zero));

            var model = new Vector3[count];
            var bone = new int[count];
            var bestWeight = new double[count];
            Array.Fill(bone, -1);
            // Normals follow the same matrices as positions: file-space linear part (inverse
            // transpose, for non-uniform scale) then the axis conversion then the rest rotation.
            var normalMatrix = new int[count];
            var normalRotation = new Quaternion[count];
            var normalMatrices = new List<Matrix4x4>();

            // Skinned: place each vertex relative to its strongest bone's rest pose. Exporters
            // do not always normalise weights (a vertex can be 1.0 on a "Control" bone and 1.0
            // on its part bone); ties go to the more specific bone, the one with fewer vertices.
            var skinned = false;
            var tieSize = new int[count];
            Array.Fill(tieSize, int.MaxValue);
            foreach (var skin in Children(geometry.Id).Where(o => o.NodeType == "Deformer" && o.SubClass == "Skin"))
            {
                foreach (var cluster in Children(skin.Id).Where(o => o.SubClass == "Cluster"))
                {
                    var link = Children(cluster.Id).FirstOrDefault(o => o.NodeType == "Model");
                    var idx = cluster.Node.Child("Indexes")?.AsIntArray(0);
                    var wts = cluster.Node.Child("Weights")?.AsDoubleArray(0);
                    if (link is null || idx is null || wts is null || idx.Length != wts.Length)
                        continue;
                    var boneIndex = skeleton.IndexOf(link.Name);
                    if (boneIndex < 0)
                        continue;
                    var meshToBone = ReadMatrix(cluster.Node.Child("Transform"));
                    var linkMatrix = ReadMatrix(cluster.Node.Child("TransformLink"));
                    if (meshToBone is null || linkMatrix is null)
                        continue;
                    Matrix4x4.Decompose(linkMatrix.Value, out var bindScale, out _, out _);
                    var local = geometric * meshToBone.Value * Matrix4x4.CreateScale(bindScale);
                    var rest = skeleton.RestWorld[boneIndex];
                    var clusterNormal = -1;
                    for (var i = 0; i < idx.Length; i++)
                    {
                        var v = idx[i];
                        if ((uint)v >= (uint)count || !(wts[i] > 1e-6))
                            continue;
                        var heavier = wts[i] > bestWeight[v] + 1e-4;
                        var tie = Math.Abs(wts[i] - bestWeight[v]) <= 1e-4 && idx.Length < tieSize[v];
                        if (!heavier && !tie)
                            continue;
                        bestWeight[v] = wts[i];
                        tieSize[v] = idx.Length;
                        bone[v] = boneIndex;
                        var p = Vector3.Transform(new Vector3((float)verts[v * 3], (float)verts[v * 3 + 1], (float)verts[v * 3 + 2]), local) * unit;
                        model[v] = rest.TransformPoint(conversion.Direction(p) * conversion.Scale);
                        if (clusterNormal < 0)
                        {
                            clusterNormal = normalMatrices.Count;
                            normalMatrices.Add(NormalMatrix(local));
                        }
                        normalMatrix[v] = clusterNormal;
                        normalRotation[v] = rest.Rot;
                    }
                    skinned = true;
                }
            }

            // Rigid: relative to the nearest skeleton bone above the mesh node.
            var rigidBone = -1;
            FbxObject? anchor = owner;
            while (anchor is not null && skeleton.IndexOf(anchor.Name) < 0)
                anchor = anchor.ModelParent;
            if (anchor is not null)
                rigidBone = skeleton.IndexOf(anchor.Name);
            var toAnchor = anchor is null
                ? geometric * World(owner)
                : Matrix4x4.Invert(World(anchor), out var invAnchor) ? geometric * World(owner) * invAnchor : geometric;
            if (anchor is not null)
            {
                // Keep the anchor's own scale (rigid rest transforms drop it).
                Matrix4x4.Decompose(World(anchor), out var anchorScale, out _, out _);
                toAnchor *= Matrix4x4.CreateScale(anchorScale);
            }
            var rigidNormal = normalMatrices.Count;
            normalMatrices.Add(NormalMatrix(toAnchor));
            var rigidRotation = rigidBone >= 0 ? skeleton.RestWorld[rigidBone].Rot : Quaternion.Identity;
            for (var v = 0; v < count; v++)
            {
                if (skinned && bone[v] >= 0)
                    continue;
                var p = Vector3.Transform(new Vector3((float)verts[v * 3], (float)verts[v * 3 + 1], (float)verts[v * 3 + 2]), toAnchor) * unit;
                var q = conversion.Direction(p) * conversion.Scale;
                model[v] = rigidBone >= 0 ? skeleton.RestWorld[rigidBone].TransformPoint(q) : q;
                bone[v] = rigidBone >= 0 ? rigidBone : 0;
                normalMatrix[v] = rigidNormal;
                normalRotation[v] = rigidRotation;
            }

            var baseVertex = positions.Count;
            positions.AddRange(model);
            vertexBone.AddRange(bone);
            var part = partNames.Count;
            partNames.Add(owner.Name);

            // Render attributes.
            var normalLayer = FbxLayer.Read(geometry.Node, "LayerElementNormal", "Normals", new[] { "NormalsIndex", "NormalIndex" }, 3);
            var uvLayer = FbxLayer.Read(geometry.Node, "LayerElementUV", "UV", new[] { "UVIndex" }, 2);
            var materialLayer = FbxLayer.ReadMaterials(geometry.Node);
            var modelMaterials = materialReader.MaterialsOf(owner);
            if (normalLayer is not null)
                anyNormals = true;
            else
                missingNormals = true;
            if (uvLayer is not null)
                anyUVs = true;

            Vector3 CornerNormal(int pvi, int v, int polygon)
            {
                if (normalLayer?.Get(pvi, v, polygon) is not { } raw)
                    return new Vector3(float.NaN);
                var n = new Vector3((float)raw[0], (float)raw[1], (float)raw[2]);
                n = Vector3.Transform(conversion.Direction(Vector3.TransformNormal(n, normalMatrices[normalMatrix[v]])), normalRotation[v]);
                var len = n.Length();
                return len > 1e-12f && float.IsFinite(len) ? n / len : new Vector3(float.NaN);
            }

            Vector2 CornerUV(int pvi, int v, int polygon)
            {
                if (uvLayer?.Get(pvi, v, polygon) is not { } raw)
                    return Vector2.Zero;
                // FBX UVs have a bottom-left origin; TriMesh stores top-left.
                return new Vector2((float)raw[0], 1f - (float)raw[1]);
            }

            int PolygonMaterial(int polygon)
            {
                if (modelMaterials.Length == 0)
                    return materialReader.DefaultMaterial();
                var local = materialLayer?.Get(polygon) ?? 0;
                return (uint)local < (uint)modelMaterials.Length ? modelMaterials[local] : modelMaterials[0];
            }

            var face = new List<int>();
            var facePvi = new List<int>();
            var polygonIndex = 0;
            for (var pvi = 0; pvi < polys.Length; pvi++)
            {
                var raw = polys[pvi];
                var v = raw < 0 ? ~raw : raw;
                if ((uint)v >= (uint)count)
                    throw new FormatException($"Mesh '{owner.Name}' has an out-of-range polygon index.");
                face.Add(v);
                facePvi.Add(pvi);
                if (raw >= 0)
                    continue;
                var material = face.Count >= 3 ? PolygonMaterial(polygonIndex) : 0;
                for (var k = 1; k + 1 < face.Count; k++)
                {
                    foreach (var corner in new[] { 0, k, k + 1 })
                    {
                        indices.Add(baseVertex + face[corner]);
                        cornerNormals.Add(CornerNormal(facePvi[corner], face[corner], polygonIndex));
                        cornerUVs.Add(CornerUV(facePvi[corner], face[corner], polygonIndex));
                    }
                    triPart.Add(part);
                    triMaterial.Add(material);
                }
                face.Clear();
                facePvi.Clear();
                polygonIndex++;
                if (indices.Count / 3 > MaxTriangles)
                    throw new FormatException($"The model has more than {MaxTriangles:N0} triangles; decimate it before importing.");
            }
        }

        if (indices.Count == 0)
            throw new FormatException("The FBX contains no mesh geometry.");

        var materials = materialReader.Materials.Count > 0 ? materialReader.Materials : new List<MaterialInfo> { MaterialInfo.Default };
        Vector3[]? normalsArray = null;
        if (anyNormals)
        {
            normalsArray = cornerNormals.ToArray();
            if (missingNormals || normalsArray.Any(n => float.IsNaN(n.X)))
            {
                // Some meshes (or corners) had no normals: fill them with computed smooth normals.
                var smooth = new TriMesh(positions.ToArray(), indices.ToArray()).EnsureNormals();
                for (var c = 0; c < normalsArray.Length; c++)
                    if (float.IsNaN(normalsArray[c].X))
                        normalsArray[c] = smooth[c];
            }
        }

        var asset = new WeaponAsset
        {
            Name = name,
            Kind = SourceKind.Fbx,
            SourcePath = sourcePath,
            Skeleton = skeleton,
            Mesh = new TriMesh(positions.ToArray(), indices.ToArray(), vertexBone.ToArray(), triPart.ToArray(), partNames,
                normalsArray, anyUVs ? cornerUVs.ToArray() : null, triMaterial.ToArray(), materials),
            Clips = clips,
        };
        asset.Notes.AddRange(notes);
        asset.Notes.AddRange(materialReader.Notes.Distinct().Take(20));
        return asset;
    }

    /// <summary>Row-vector normal matrix: inverse transpose of the linear part (falls back to the matrix itself when singular).</summary>
    private static Matrix4x4 NormalMatrix(Matrix4x4 m)
    {
        m.M41 = m.M42 = m.M43 = 0f;
        return Matrix4x4.Invert(m, out var inv) ? Matrix4x4.Transpose(inv) : m;
    }

    private static Matrix4x4? ReadMatrix(FbxNode? node)
    {
        var a = node?.AsDoubleArray(0);
        if (a is null || a.Length != 16 || a.Any(x => !double.IsFinite(x)))
            return null;
        return new Matrix4x4(
            (float)a[0], (float)a[1], (float)a[2], (float)a[3],
            (float)a[4], (float)a[5], (float)a[6], (float)a[7],
            (float)a[8], (float)a[9], (float)a[10], (float)a[11],
            (float)a[12], (float)a[13], (float)a[14], (float)a[15]);
    }
}
