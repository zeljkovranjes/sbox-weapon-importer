#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Formats.Dmx;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Generation;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>A written model DMX plus the faceSet material names it uses.</summary>
public sealed class ModelDmxResult
{
    public required string Text { get; init; }

    /// <summary>Sanitised, unique faceSet material name and the source material, in file order.</summary>
    public required IReadOnlyList<(string DmxMaterial, MaterialInfo Material)> Materials { get; init; }
}

/// <summary>
/// Writes a weapon's render mesh as a Source 2 model DMX (<c>keyvalues2_noids</c>): a DmeModel
/// with the skeleton (DmeJoint rest locals) and one DmeDag/DmeMesh per material. Vertex data
/// keeps separate position / normal / texcoord index arrays so per-corner normals and UVs
/// survive; skinning is rigid (one joint per position, weight 1, jointCount 1) on
/// <see cref="TriMesh.VertexBone"/>.
/// </summary>
public static class ModelDmxWriter
{
    /// <summary>Writes the given triangles (indices into <see cref="WeaponAsset.Mesh"/>) as model DMX text.</summary>
    public static string Write(WeaponAsset asset, IReadOnlyList<int> triangles, DmxSpace space, string name)
        => WriteWithMaterials(asset, triangles, space, name).Text;

    /// <summary>As <see cref="Write"/>, also returning the faceSet material mapping.</summary>
    public static ModelDmxResult WriteWithMaterials(WeaponAsset asset, IReadOnlyList<int>? triangles, DmxSpace? space, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);
        space ??= DmxSpace.SourceZUpInches;
        name = string.IsNullOrWhiteSpace(name) ? asset.Name : name;
        var mesh = asset.Mesh;
        var skeleton = asset.Skeleton;
        if (skeleton.Count == 0)
            throw new ArgumentException("The weapon has no bones.", nameof(asset));

        // Triangles grouped by material, first use order of the material index for determinism.
        var selected = triangles ?? Enumerable.Range(0, mesh.TriangleCount).ToArray();
        var seen = new HashSet<int>();
        var byMaterial = new SortedDictionary<int, List<int>>();
        foreach (var t in selected)
        {
            if ((uint)t >= (uint)mesh.TriangleCount)
                throw new ArgumentOutOfRangeException(nameof(triangles), $"Triangle {t} is out of range (count {mesh.TriangleCount}).");
            if (!seen.Add(t))
                continue;
            var m = mesh.TriangleMaterial[t];
            if (!byMaterial.TryGetValue(m, out var list))
                byMaterial[m] = list = new List<int>();
            list.Add(t);
        }
        if (byMaterial.Count == 0)
            throw new ArgumentException("No triangles to write.", nameof(triangles));

        var usedMaterialNames = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<Part>();
        var normals = mesh.EnsureNormals();
        var uvs = mesh.EnsureUVs();
        foreach (var (materialIndex, tris) in byMaterial)
        {
            var material = mesh.Materials[materialIndex];
            var dmxName = DmxNames.Unique(DmxNames.Material(material.Name), usedMaterialNames);
            parts.Add(BuildPart(mesh, tris, dmxName, material, normals, uvs, space, skeleton.Count));
        }

        var boneNames = DmxNames.Bones(skeleton);
        var scope = "weapon-model:" + name;
        var modelId = DmxIds.Guid(scope, "model");
        var jointIds = boneNames.Select(b => DmxIds.Guid(scope, "joint:" + b)).ToArray();

        var w = new Kv2Writer();
        w.Raw(Kv2Writer.ModelHeader);
        w.BeginTop("DmElement");
        w.Attr("name", "string", "root");
        w.Attr("model", "element", modelId);
        w.Attr("skeleton", "element", modelId);
        w.EndTop();

        w.BeginTop("DmeModel");
        w.Attr("id", "elementid", modelId);
        w.Attr("name", "string", name);
        Transform(w, "transform", Vector3.Zero, Quaternion.Identity);
        w.Attr("visible", "bool", "1");
        var children = new List<string>();
        for (var i = 0; i < skeleton.Count; i++)
            if (skeleton[i].ParentIndex < 0)
                children.Add(jointIds[i]);
        children.AddRange(parts.Select(p => p.DagId(scope)));
        w.Refs("children", children);
        w.Refs("jointList", jointIds);
        space.WriteAxisSystem(w);
        w.EndTop();

        for (var i = 0; i < skeleton.Count; i++)
        {
            var bone = skeleton[i];
            var local = space.Local(bone.RestLocal, bone.ParentIndex < 0);
            w.BeginTop("DmeJoint");
            w.Attr("id", "elementid", jointIds[i]);
            w.Attr("name", "string", boneNames[i]);
            Transform(w, "transform", local.Pos, local.Rot);
            w.Attr("visible", "bool", "1");
            var boneChildren = new List<string>();
            for (var c = 0; c < skeleton.Count; c++)
                if (skeleton[c].ParentIndex == i)
                    boneChildren.Add(jointIds[c]);
            if (boneChildren.Count > 0)
                w.Refs("children", boneChildren);
            w.EndTop();
        }

        foreach (var part in parts)
        {
            var dagId = part.DagId(scope);
            var meshId = DmxIds.Guid(scope, "mesh:" + part.Material);
            var vertexId = DmxIds.Guid(scope, "vertices:" + part.Material);

            w.BeginTop("DmeDag");
            w.Attr("id", "elementid", dagId);
            w.Attr("name", "string", part.Material);
            Transform(w, "transform", Vector3.Zero, Quaternion.Identity);
            w.Attr("shape", "element", meshId);
            w.Attr("visible", "bool", "1");
            w.EndTop();

            w.BeginTop("DmeMesh");
            w.Attr("id", "elementid", meshId);
            w.Attr("name", "string", part.Material);
            w.Attr("visible", "bool", "1");
            w.Attr("currentState", "element", vertexId);
            w.Refs("baseStates", new[] { vertexId });
            w.BeginArray("faceSets");
            w.BeginArrayElement("DmeFaceSet");
            w.Attr("name", "string", part.Material);
            w.BeginArray("faces", "int_array");
            for (var i = 0; i < part.Faces.Count; i++)
            {
                w.Value(Kv2Writer.I(part.Faces[i]), false);
                if (i % 3 == 2)
                    w.Value("-1", i == part.Faces.Count - 1);
            }
            w.EndArray();
            w.BeginInline("material", "DmeMaterial");
            w.Attr("name", "string", part.Material);
            w.Attr("mtlName", "string", part.Material);
            w.EndInline();
            w.EndArrayElement(true);
            w.EndArray();
            w.EndTop();

            w.BeginTop("DmeVertexData");
            w.Attr("id", "elementid", vertexId);
            w.Attr("name", "string", "bind");
            w.ValueArray("vertexFormat", "string_array", new[] { "position$0", "normal$0", "texcoord$0", "blendweights$0", "blendindices$0" }, s => s);
            w.Attr("jointCount", "int", "1");
            w.Attr("flipVCoordinates", "bool", space.UvBottomLeft ? "1" : "0");
            w.ValueArray("position$0", "vector3_array", part.Positions, Kv2Writer.Vec);
            w.ValueArray("position$0Indices", "int_array", part.PositionIndices, Kv2Writer.I);
            w.ValueArray("normal$0", "vector3_array", part.Normals, Kv2Writer.Vec);
            w.ValueArray("normal$0Indices", "int_array", part.NormalIndices, Kv2Writer.I);
            w.ValueArray("texcoord$0", "vector2_array", part.UVs, Kv2Writer.Vec);
            w.ValueArray("texcoord$0Indices", "int_array", part.UvIndices, Kv2Writer.I);
            // Skinning data is per position (indexed through position$0Indices).
            w.ValueArray("blendweights$0", "float_array", part.Joints, _ => "1");
            w.ValueArray("blendindices$0", "int_array", part.Joints, Kv2Writer.I);
            w.EndTop();
        }

        return new ModelDmxResult
        {
            Text = w.ToString(),
            Materials = parts.Select(p => (p.Material, p.Source)).ToList(),
        };
    }

    private sealed class Part
    {
        public required string Material;
        public required MaterialInfo Source;
        public readonly List<Vector3> Positions = new();
        public readonly List<int> Joints = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> UVs = new();
        public readonly List<int> PositionIndices = new();
        public readonly List<int> NormalIndices = new();
        public readonly List<int> UvIndices = new();
        public readonly List<int> Faces = new();

        public string DagId(string scope) => DmxIds.Guid(scope, "dag:" + Material);
    }

    private static Part BuildPart(TriMesh mesh, List<int> tris, string dmxName, MaterialInfo material, Vector3[] normals, Vector2[] uvs, DmxSpace space, int boneCount)
    {
        var part = new Part { Material = dmxName, Source = material };
        var positionOf = new Dictionary<int, int>();
        var normalOf = new Dictionary<Vector3, int>();
        var uvOf = new Dictionary<Vector2, int>();
        var vertexOf = new Dictionary<(int P, int N, int U), int>();
        foreach (var t in tris)
        {
            for (var k = 0; k < 3; k++)
            {
                var corner = t * 3 + k;
                var v = mesh.Indices[corner];
                if (!positionOf.TryGetValue(v, out var p))
                {
                    p = part.Positions.Count;
                    positionOf[v] = p;
                    part.Positions.Add(space.Point(mesh.Positions[v]));
                    var bone = mesh.VertexBone[v];
                    part.Joints.Add(bone >= 0 && bone < boneCount ? bone : 0);
                }
                var nv = space.Direction(normals[corner]);
                var len = nv.Length();
                nv = len > 1e-12f && float.IsFinite(len) ? nv / len : space.Direction(Vector3.UnitZ);
                if (!normalOf.TryGetValue(nv, out var n))
                {
                    n = part.Normals.Count;
                    normalOf[nv] = n;
                    part.Normals.Add(nv);
                }
                var uvValue = space.Uv(uvs[corner]);
                if (!float.IsFinite(uvValue.X) || !float.IsFinite(uvValue.Y))
                    uvValue = Vector2.Zero;
                if (!uvOf.TryGetValue(uvValue, out var u))
                {
                    u = part.UVs.Count;
                    uvOf[uvValue] = u;
                    part.UVs.Add(uvValue);
                }
                if (!vertexOf.TryGetValue((p, n, u), out var vertex))
                {
                    vertex = part.PositionIndices.Count;
                    vertexOf[(p, n, u)] = vertex;
                    part.PositionIndices.Add(p);
                    part.NormalIndices.Add(n);
                    part.UvIndices.Add(u);
                }
                part.Faces.Add(vertex);
            }
        }
        return part;
    }

    private static void Transform(Kv2Writer w, string name, Vector3 position, Quaternion orientation)
    {
        w.BeginInline(name, "DmeTransform");
        w.Attr("name", "string", name);
        w.Attr("position", "vector3", Kv2Writer.Vec(position));
        w.Attr("orientation", "quaternion", Kv2Writer.Quat(orientation));
        w.Attr("scale", "float", "1");
        w.EndInline();
    }
}
