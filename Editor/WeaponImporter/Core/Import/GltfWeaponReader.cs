#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using WeaponImporter.Core.Formats.Gltf;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Import;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>Reads a glTF/GLB weapon: skeleton, clips and bind-pose triangles in model space.</summary>
public static class GltfWeaponReader
{
    public static WeaponAsset Read(byte[] data, string name, string sourcePath = "", Func<string, byte[]>? externalBuffers = null, float sampleFps = 30f)
    {
        var imported = GltfImporter.Import(data, new GltfImportOptions { SampleFps = sampleFps, ExternalBufferResolver = externalBuffers }, out var document, out var boneByNode);
        var conversion = SpaceConversion.For(imported);
        var skeleton = conversion.Skeleton(imported.Skeleton);
        var clips = conversion.Clips(imported.Clips, imported.Skeleton, skeleton);

        var root = document.Root;
        var nodes = document.Nodes;
        var accessors = root.TryGetProperty("accessors", out var acc) ? acc : default;
        var views = root.TryGetProperty("bufferViews", out var bv) ? bv : default;
        if (!root.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array)
            throw new FormatException("The glTF contains no meshes.");
        root.TryGetProperty("skins", out var skins);
        root.TryGetProperty("nodes", out var nodeArray);

        // World matrices (meters) of every node.
        var world = new Matrix4x4[nodes.Count];
        var done = new bool[nodes.Count];
        Matrix4x4 World(int i)
        {
            if (done[i])
                return world[i];
            var n = nodes[i];
            var local = Matrix4x4.CreateScale(n.Scale) * Matrix4x4.CreateFromQuaternion(n.Rotation) * Matrix4x4.CreateTranslation(n.Translation);
            done[i] = true; // guards malformed cycles
            world[i] = n.Parent >= 0 ? local * World(n.Parent) : local;
            return world[i];
        }

        int BoneOf(int node)
        {
            for (var n = node; n >= 0; n = nodes[n].Parent)
                if (boneByNode.TryGetValue(n, out var boneName))
                    return skeleton.IndexOf(boneName);
            return 0;
        }

        var positions = new List<Vector3>();
        var vertexBone = new List<int>();
        var indices = new List<int>();
        var triPart = new List<int>();
        var partNames = new List<string>();
        var vertexNormals = new List<Vector3>();
        var vertexUVs = new List<Vector2>();
        var triMaterial = new List<int>();
        var anyNormals = false;
        var anyUVs = false;
        var materialReader = new GltfMaterialReader(document, sourcePath, externalBuffers);

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            var nodeJson = nodeArray[nodeIndex];
            if (!nodeJson.TryGetProperty("mesh", out var meshIndexProp))
                continue;
            var meshIndex = meshIndexProp.GetInt32();
            if (meshIndex < 0 || meshIndex >= meshes.GetArrayLength())
                throw new FormatException($"glTF node {nodeIndex} references missing mesh {meshIndex}.");
            var meshJson = meshes[meshIndex];
            int[]? skinJoints = null;
            Matrix4x4[]? inverseBind = null;
            if (nodeJson.TryGetProperty("skin", out var skinProp) && skins.ValueKind == JsonValueKind.Array)
            {
                var skin = skins[skinProp.GetInt32()];
                skinJoints = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
                if (skin.TryGetProperty("inverseBindMatrices", out var ibm))
                {
                    var floats = ReadFloats(accessors, views, document.Buffers, ibm.GetInt32(), 16);
                    inverseBind = new Matrix4x4[skinJoints.Length];
                    for (var j = 0; j < skinJoints.Length && (j + 1) * 16 <= floats.Length; j++)
                    {
                        var f = floats.AsSpan(j * 16, 16);
                        // glTF matrices are column-major column-vector = row-major row-vector.
                        inverseBind[j] = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
                    }
                }
            }

            var nodeBone = BoneOf(nodeIndex);
            var partName = nodes[nodeIndex].Name ?? (meshJson.TryGetProperty("name", out var mn) ? mn.GetString() : null) ?? $"mesh_{meshIndex}";
            var part = partNames.Count;
            partNames.Add(partName);

            foreach (var prim in meshJson.GetProperty("primitives").EnumerateArray())
            {
                var mode = prim.TryGetProperty("mode", out var modeProp) ? modeProp.GetInt32() : 4;
                if (mode != 4)
                    continue; // points/lines/strips are not solid geometry
                var attributes = prim.GetProperty("attributes");
                if (!attributes.TryGetProperty("POSITION", out var posProp))
                    continue;
                var pos = ReadFloats(accessors, views, document.Buffers, posProp.GetInt32(), 3);
                var count = pos.Length / 3;
                float[]? joints = null, weights = null;
                if (skinJoints is not null && attributes.TryGetProperty("JOINTS_0", out var jp) && attributes.TryGetProperty("WEIGHTS_0", out var wp))
                {
                    joints = ReadFloats(accessors, views, document.Buffers, jp.GetInt32(), 4, raw: true);
                    weights = ReadFloats(accessors, views, document.Buffers, wp.GetInt32(), 4);
                }

                float[]? nrm = null, tex = null;
                if (attributes.TryGetProperty("NORMAL", out var nrmProp))
                {
                    nrm = ReadFloats(accessors, views, document.Buffers, nrmProp.GetInt32(), 3);
                    if (nrm.Length / 3 < count)
                        nrm = null;
                }
                if (attributes.TryGetProperty("TEXCOORD_0", out var texProp))
                {
                    tex = ReadFloats(accessors, views, document.Buffers, texProp.GetInt32(), 2);
                    if (tex.Length / 2 < count)
                        tex = null;
                }
                anyNormals |= nrm is not null;
                anyUVs |= tex is not null;
                var material = materialReader.MaterialIndex(prim.TryGetProperty("material", out var matProp) ? matProp.GetInt32() : -1);

                var baseVertex = positions.Count;
                var nodeWorld = World(nodeIndex);
                var anchorOfNode = Anchor(nodeIndex);
                var invAnchor = Matrix4x4.Identity;
                if (anchorOfNode >= 0 && Matrix4x4.Invert(World(anchorOfNode), out var inv))
                {
                    // Rigid rest transforms carry no scale; keep the anchor's.
                    Matrix4x4.Decompose(World(anchorOfNode), out var anchorScale, out _, out _);
                    invAnchor = inv * Matrix4x4.CreateScale(anchorScale);
                }
                var nodeNormal = NormalMatrix(nodeWorld);
                var anchoredNormal = NormalMatrix(nodeWorld * invAnchor);
                for (var v = 0; v < count; v++)
                {
                    var p = new Vector3(pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
                    var n = nrm is not null ? new Vector3(nrm[v * 3], nrm[v * 3 + 1], nrm[v * 3 + 2]) : Vector3.Zero;
                    int bone;
                    Vector3 model;
                    Vector3 normal;
                    if (joints is not null && weights is not null)
                    {
                        var best = 0;
                        for (var k = 1; k < 4; k++)
                            if (weights[v * 4 + k] > weights[v * 4 + best])
                                best = k;
                        var jointSlot = (int)joints[v * 4 + best];
                        var jointNode = jointSlot >= 0 && jointSlot < skinJoints!.Length ? skinJoints[jointSlot] : nodeIndex;
                        bone = BoneOf(jointNode);
                        // Mesh space -> joint bind space -> joint rest world. The full node matrix
                        // is used (rigid rest transforms drop inherited scale, which would pull
                        // parts on differently scaled joints apart).
                        var ibm = inverseBind is not null && jointSlot < inverseBind.Length ? inverseBind[jointSlot] : Matrix4x4.Identity;
                        var bindWorld = Vector3.Transform(Vector3.Transform(p, ibm), World(jointNode)) * 100f;
                        model = conversion.Point(bindWorld);
                        normal = conversion.Direction(Vector3.TransformNormal(n, NormalMatrix(ibm * World(jointNode))));
                    }
                    else
                    {
                        bone = nodeBone;
                        var anchorNode = anchorOfNode;
                        var rel = Vector3.Transform(Vector3.Transform(p, nodeWorld), invAnchor) * 100f;
                        var anchored = anchorNode >= 0 && boneByNode.ContainsKey(anchorNode);
                        model = anchored
                            ? skeleton.RestWorld[bone].TransformPoint(conversion.Direction(rel) * conversion.Scale)
                            : conversion.Point(Vector3.Transform(p, nodeWorld) * 100f);
                        normal = anchored
                            ? skeleton.RestWorld[bone].TransformVector(conversion.Direction(Vector3.TransformNormal(n, anchoredNormal)))
                            : conversion.Direction(Vector3.TransformNormal(n, nodeNormal));
                    }
                    positions.Add(model);
                    vertexBone.Add(bone);
                    var len = normal.Length();
                    vertexNormals.Add(nrm is not null && len > 1e-12f && float.IsFinite(len) ? normal / len : new Vector3(float.NaN));
                    // glTF UVs already have the top-left origin TriMesh uses.
                    vertexUVs.Add(tex is not null ? new Vector2(tex[v * 2], tex[v * 2 + 1]) : Vector2.Zero);
                }

                if (prim.TryGetProperty("indices", out var idxProp))
                {
                    var idx = ReadFloats(accessors, views, document.Buffers, idxProp.GetInt32(), 1, raw: true);
                    for (var i = 0; i + 2 < idx.Length; i += 3)
                    {
                        var a = (int)idx[i]; var b = (int)idx[i + 1]; var c = (int)idx[i + 2];
                        if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count)
                            throw new FormatException($"glTF mesh '{partName}' has out-of-range indices.");
                        indices.Add(baseVertex + a); indices.Add(baseVertex + b); indices.Add(baseVertex + c);
                        triPart.Add(part);
                        triMaterial.Add(material);
                    }
                }
                else
                {
                    for (var i = 0; i + 2 < count; i += 3)
                    {
                        indices.Add(baseVertex + i); indices.Add(baseVertex + i + 1); indices.Add(baseVertex + i + 2);
                        triPart.Add(part);
                        triMaterial.Add(material);
                    }
                }
                if (indices.Count / 3 > FbxWeaponReader.MaxTriangles)
                    throw new FormatException($"The model has more than {FbxWeaponReader.MaxTriangles:N0} triangles; decimate it before importing.");
            }
        }

        int Anchor(int node)
        {
            for (var n = node; n >= 0; n = nodes[n].Parent)
                if (boneByNode.ContainsKey(n))
                    return n;
            return -1;
        }

        if (indices.Count == 0)
            throw new FormatException("The glTF contains no triangle geometry.");

        var indexArray = indices.ToArray();
        Vector3[]? cornerNormals = null;
        if (anyNormals)
        {
            cornerNormals = indexArray.Select(i => vertexNormals[i]).ToArray();
            if (cornerNormals.Any(n => float.IsNaN(n.X)))
            {
                var smooth = new TriMesh(positions.ToArray(), indexArray).EnsureNormals();
                for (var c = 0; c < cornerNormals.Length; c++)
                    if (float.IsNaN(cornerNormals[c].X))
                        cornerNormals[c] = smooth[c];
            }
        }
        var cornerUVs = anyUVs ? indexArray.Select(i => vertexUVs[i]).ToArray() : null;

        var asset = new WeaponAsset
        {
            Name = name,
            Kind = SourceKind.Gltf,
            SourcePath = sourcePath,
            Skeleton = skeleton,
            Mesh = new TriMesh(positions.ToArray(), indexArray, vertexBone.ToArray(), triPart.ToArray(), partNames,
                cornerNormals, cornerUVs, triMaterial.ToArray(), materialReader.Materials),
            Clips = clips,
        };
        asset.Notes.AddRange(imported.Notes);
        asset.Notes.AddRange(materialReader.Notes.Distinct().Take(20));
        return asset;
    }

    /// <summary>Row-vector normal matrix: inverse transpose of the linear part.</summary>
    private static Matrix4x4 NormalMatrix(Matrix4x4 m)
    {
        m.M41 = m.M42 = m.M43 = 0f;
        return Matrix4x4.Invert(m, out var inv) ? Matrix4x4.Transpose(inv) : m;
    }

    /// <summary>Accessor reader for any component type; <paramref name="raw"/> skips normalisation (indices, joints).</summary>
    private static float[] ReadFloats(JsonElement accessors, JsonElement views, List<byte[]> buffers, int index, int comps, bool raw = false)
    {
        if (accessors.ValueKind != JsonValueKind.Array || index < 0 || index >= accessors.GetArrayLength())
            throw new FormatException($"glTF accessor {index} does not exist.");
        var accessor = accessors[index];
        if (accessor.TryGetProperty("sparse", out _))
            throw new FormatException("glTF sparse accessors are not supported.");
        var type = accessor.GetProperty("type").GetString();
        var actual = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new FormatException($"glTF accessor type '{type}' is not supported.") };
        if (actual != comps)
            throw new FormatException($"glTF accessor {index} is {type}, expected {comps} components.");
        var count = accessor.GetProperty("count").GetInt32();
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var normalized = accessor.TryGetProperty("normalized", out var n) && n.GetBoolean();
        var size = componentType switch { 5126 or 5125 => 4, 5122 or 5123 => 2, 5120 or 5121 => 1, _ => throw new FormatException($"glTF component type {componentType} is not supported.") };
        if (count < 0 || count > 50_000_000)
            throw new FormatException($"glTF accessor {index} has an invalid count.");
        var result = new float[count * comps];
        if (!accessor.TryGetProperty("bufferView", out var viewProp))
            return result;
        var view = views[viewProp.GetInt32()];
        var buffer = buffers[view.GetProperty("buffer").GetInt32()];
        var start = (long)(view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
        var stride = view.TryGetProperty("byteStride", out var st) ? st.GetInt32() : size * comps;
        if (count > 0 && start + (long)(count - 1) * stride + size * comps > buffer.Length)
            throw new FormatException($"glTF accessor {index} reads past the end of its buffer (truncated file?).");
        for (var e = 0; e < count; e++)
        {
            var offset = (int)(start + (long)e * stride);
            for (var c = 0; c < comps; c++)
            {
                var at = offset + c * size;
                float value = componentType switch
                {
                    5126 => BitConverter.ToSingle(buffer, at),
                    5125 => BitConverter.ToUInt32(buffer, at),
                    5123 => BitConverter.ToUInt16(buffer, at),
                    5122 => BitConverter.ToInt16(buffer, at),
                    5121 => buffer[at],
                    _ => (sbyte)buffer[at],
                };
                if (!raw && normalized)
                    value = componentType switch { 5121 => value / 255f, 5123 => value / 65535f, 5120 => MathF.Max(value / 127f, -1f), 5122 => MathF.Max(value / 32767f, -1f), _ => value };
                result[e * comps + c] = value;
            }
        }
        return result;
    }
}
