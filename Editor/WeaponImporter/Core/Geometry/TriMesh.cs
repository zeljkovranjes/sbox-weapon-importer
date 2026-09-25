#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.Core.Geometry;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A triangle soup in weapon model space (inches, Z up) with enough bookkeeping to tell
/// which bone and which mesh part each triangle belongs to, plus optional render attributes
/// (per-corner normals and UVs, per-triangle material).
/// </summary>
/// <remarks>
/// UV convention: <see cref="CornerUVs"/> use a TOP-LEFT origin (V grows downwards, the glTF /
/// DirectX convention). Readers convert on the way in (FBX stores bottom-left: V' = 1 - V);
/// writers convert on the way out.
/// </remarks>
public sealed class TriMesh
{
    /// <summary>Vertex positions in bind pose.</summary>
    public Vector3[] Positions { get; }

    /// <summary>Triangle vertex indices, three per triangle.</summary>
    public int[] Indices { get; }

    /// <summary>Dominant skinning bone per vertex (-1 when unskinned / rigid to the root).</summary>
    public int[] VertexBone { get; }

    /// <summary>Index into <see cref="PartNames"/> for each triangle.</summary>
    public int[] TrianglePart { get; }

    /// <summary>Mesh/material part names (FBX mesh node, glTF mesh, or engine mesh group).</summary>
    public IReadOnlyList<string> PartNames { get; }

    /// <summary>Unit normal per triangle corner (parallel to <see cref="Indices"/>), or null when the source had none.</summary>
    public Vector3[]? CornerNormals { get; }

    /// <summary>Texture coordinate per triangle corner (top-left origin), or null when the source had none.</summary>
    public Vector2[]? CornerUVs { get; }

    /// <summary>Index into <see cref="Materials"/> for each triangle.</summary>
    public int[] TriangleMaterial { get; }

    /// <summary>Render materials; never empty.</summary>
    public IReadOnlyList<MaterialInfo> Materials { get; }

    /// <summary>Axis-aligned bounds of all vertices.</summary>
    public Bounds Bounds { get; }

    public int TriangleCount => Indices.Length / 3;

    private Vector3[]? _smoothNormals;
    private float _smoothAngle = float.NaN;

    public TriMesh(Vector3[] positions, int[] indices, int[]? vertexBone = null, int[]? trianglePart = null, IReadOnlyList<string>? partNames = null,
        Vector3[]? cornerNormals = null, Vector2[]? cornerUVs = null, int[]? triangleMaterial = null, IReadOnlyList<MaterialInfo>? materials = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length % 3 != 0)
            throw new ArgumentException("Index count must be a multiple of three.", nameof(indices));
        foreach (var i in indices)
            if ((uint)i >= (uint)positions.Length)
                throw new ArgumentException($"Triangle index {i} is out of range (vertex count {positions.Length}).", nameof(indices));
        foreach (var p in positions)
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                throw new ArgumentException("Mesh contains non-finite vertex positions.", nameof(positions));
        if (cornerNormals is not null && cornerNormals.Length != indices.Length)
            throw new ArgumentException($"Corner normal count {cornerNormals.Length} does not match index count {indices.Length}.", nameof(cornerNormals));
        if (cornerUVs is not null && cornerUVs.Length != indices.Length)
            throw new ArgumentException($"Corner UV count {cornerUVs.Length} does not match index count {indices.Length}.", nameof(cornerUVs));
        if (triangleMaterial is not null && triangleMaterial.Length != indices.Length / 3)
            throw new ArgumentException($"Triangle material count {triangleMaterial.Length} does not match triangle count {indices.Length / 3}.", nameof(triangleMaterial));

        Positions = positions;
        Indices = indices;
        VertexBone = vertexBone ?? Enumerable.Repeat(-1, positions.Length).ToArray();
        TrianglePart = trianglePart ?? new int[indices.Length / 3];
        PartNames = partNames ?? new[] { "mesh" };
        CornerNormals = cornerNormals;
        CornerUVs = cornerUVs;
        Materials = materials is { Count: > 0 } ? materials : new[] { MaterialInfo.Default };
        TriangleMaterial = triangleMaterial ?? new int[indices.Length / 3];
        foreach (var m in TriangleMaterial)
            if ((uint)m >= (uint)Materials.Count)
                throw new ArgumentException($"Triangle material {m} is out of range (material count {Materials.Count}).", nameof(triangleMaterial));
        Bounds = Bounds.FromPoints(positions);
    }

    public (Vector3 A, Vector3 B, Vector3 C) Triangle(int t)
        => (Positions[Indices[t * 3]], Positions[Indices[t * 3 + 1]], Positions[Indices[t * 3 + 2]]);

    /// <summary>Unit face normal (zero for degenerate triangles).</summary>
    public Vector3 FaceNormal(int t)
    {
        var (a, b, c) = Triangle(t);
        var n = Vector3.Cross(b - a, c - a);
        var len = n.Length();
        return len > 1e-12f ? n / len : Vector3.Zero;
    }

    /// <summary>The bone most vertices of triangle <paramref name="t"/> are skinned to.</summary>
    public int TriangleBone(int t)
    {
        int a = VertexBone[Indices[t * 3]], b = VertexBone[Indices[t * 3 + 1]], c = VertexBone[Indices[t * 3 + 2]];
        return b == c ? b : a;
    }

    /// <summary>Material of triangle <paramref name="t"/>.</summary>
    public MaterialInfo MaterialOf(int t) => Materials[TriangleMaterial[t]];

    /// <summary>
    /// Corner normals for export: the source normals when present, otherwise smooth normals
    /// computed on demand (corners sharing a position are averaged when their faces are within
    /// <paramref name="smoothingAngle"/> degrees, area weighted). Cached per angle.
    /// </summary>
    public Vector3[] EnsureNormals(float smoothingAngle = 60f)
    {
        if (CornerNormals is not null)
            return CornerNormals;
        var cached = _smoothNormals;
        if (cached is not null && _smoothAngle == smoothingAngle)
            return cached;
        cached = ComputeSmoothNormals(smoothingAngle);
        _smoothNormals = cached;
        _smoothAngle = smoothingAngle;
        return cached;
    }

    /// <summary>Corner UVs for export (zeros when the source had none).</summary>
    public Vector2[] EnsureUVs() => CornerUVs ?? new Vector2[Indices.Length];

    private Vector3[] ComputeSmoothNormals(float smoothingAngle)
    {
        var tris = TriangleCount;
        var faceArea = new Vector3[tris]; // unnormalised cross product = area weighting
        var faceUnit = new Vector3[tris];
        for (var t = 0; t < tris; t++)
        {
            var (a, b, c) = Triangle(t);
            var n = Vector3.Cross(b - a, c - a);
            faceArea[t] = n;
            var len = n.Length();
            faceUnit[t] = len > 1e-20f ? n / len : Vector3.Zero;
        }

        // Weld corners by exact position so UV seams (split vertices) still smooth.
        var groups = new Dictionary<Vector3, List<int>>();
        for (var c = 0; c < Indices.Length; c++)
        {
            var p = Positions[Indices[c]];
            if (!groups.TryGetValue(p, out var list))
                groups[p] = list = new List<int>(6);
            list.Add(c);
        }

        var cosLimit = MathF.Cos(Math.Clamp(smoothingAngle, 0f, 180f) * MathF.PI / 180f);
        var result = new Vector3[Indices.Length];
        foreach (var list in groups.Values)
        {
            // Pathological fans (hundreds of corners on one point) fall back to face normals.
            var huge = list.Count > 256;
            foreach (var c in list)
            {
                var own = faceUnit[c / 3];
                var sum = Vector3.Zero;
                if (!huge)
                {
                    foreach (var o in list)
                    {
                        var other = o / 3;
                        if (other == c / 3 || Vector3.Dot(own, faceUnit[other]) >= cosLimit)
                            sum += faceArea[other];
                    }
                }
                else
                {
                    sum = faceArea[c / 3];
                }
                var len = sum.Length();
                result[c] = len > 1e-20f ? sum / len : (own.LengthSquared() > 0f ? own : Vector3.UnitZ);
            }
        }
        return result;
    }

    /// <summary>Combines several meshes into one, offsetting indices, part ids and material ids.</summary>
    public static TriMesh Merge(IReadOnlyList<TriMesh> meshes)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var bones = new List<int>();
        var parts = new List<int>();
        var names = new List<string>();
        var normals = meshes.Any(m => m.CornerNormals is not null) ? new List<Vector3>() : null;
        var uvs = meshes.Any(m => m.CornerUVs is not null) ? new List<Vector2>() : null;
        var triMaterial = new List<int>();
        var materials = new List<MaterialInfo>();
        foreach (var mesh in meshes)
        {
            var baseVertex = positions.Count;
            var basePart = names.Count;
            var baseMaterial = materials.Count;
            positions.AddRange(mesh.Positions);
            bones.AddRange(mesh.VertexBone);
            indices.AddRange(mesh.Indices.Select(i => i + baseVertex));
            parts.AddRange(mesh.TrianglePart.Select(p => p + basePart));
            names.AddRange(mesh.PartNames);
            normals?.AddRange(mesh.EnsureNormals());
            uvs?.AddRange(mesh.EnsureUVs());
            triMaterial.AddRange(mesh.TriangleMaterial.Select(m => m + baseMaterial));
            materials.AddRange(mesh.Materials);
        }
        return new TriMesh(positions.ToArray(), indices.ToArray(), bones.ToArray(), parts.ToArray(), names,
            normals?.ToArray(), uvs?.ToArray(), triMaterial.ToArray(), materials);
    }

    /// <summary>
    /// A copy with every vertex moved by <paramref name="transform"/>. Corner normals go through
    /// <paramref name="normalTransform"/> (vertex index, normal) and are renormalised; when it is
    /// null they are carried over unchanged, so pass one for anything but a translation.
    /// UVs and materials carry over.
    /// </summary>
    public TriMesh Transformed(Func<int, Vector3, Vector3> transform, Func<int, Vector3, Vector3>? normalTransform = null)
    {
        var moved = new Vector3[Positions.Length];
        for (var i = 0; i < moved.Length; i++)
            moved[i] = transform(i, Positions[i]);
        var normals = CornerNormals;
        if (normals is not null && normalTransform is not null)
        {
            var src = normals;
            normals = new Vector3[src.Length];
            for (var c = 0; c < src.Length; c++)
                normals[c] = SafeNormalize(normalTransform(Indices[c], src[c]), src[c]);
        }
        return new TriMesh(moved, Indices, VertexBone, TrianglePart, PartNames, normals, CornerUVs, TriangleMaterial, Materials);
    }

    /// <summary>
    /// A copy with new vertex positions and bones; each corner normal is rotated by its vertex's
    /// rotation (<paramref name="vertexRotation"/>, e.g. the vertex bone's rest delta) and
    /// renormalised. Topology, UVs, parts and materials carry over.
    /// </summary>
    public TriMesh WithVertices(Vector3[] positions, int[] vertexBone, Func<int, Quaternion>? vertexRotation)
    {
        var normals = CornerNormals;
        if (normals is not null && vertexRotation is not null)
        {
            var src = normals;
            normals = new Vector3[src.Length];
            var rotation = new Quaternion[positions.Length];
            for (var v = 0; v < rotation.Length; v++)
                rotation[v] = vertexRotation(v);
            for (var c = 0; c < src.Length; c++)
                normals[c] = SafeNormalize(Vector3.Transform(src[c], rotation[Indices[c]]), src[c]);
        }
        return new TriMesh(positions, Indices, vertexBone, TrianglePart, PartNames, normals, CornerUVs, TriangleMaterial, Materials);
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var len = v.Length();
        return len > 1e-20f && float.IsFinite(len) ? v / len : fallback;
    }
}

/// <summary>Axis-aligned box.</summary>
public readonly record struct Bounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
    public bool IsEmpty => Min.X > Max.X;

    public static Bounds Empty => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public static Bounds FromPoints(IEnumerable<Vector3> points)
    {
        var b = Empty;
        foreach (var p in points)
            b = b.Encapsulate(p);
        return b;
    }

    public Bounds Encapsulate(Vector3 p) => new(Vector3.Min(Min, p), Vector3.Max(Max, p));
    public Bounds Encapsulate(Bounds o) => o.IsEmpty ? this : new(Vector3.Min(Min, o.Min), Vector3.Max(Max, o.Max));
    public Bounds Grow(float amount) => new(Min - new Vector3(amount), Max + new Vector3(amount));

    public float DistanceSquared(Vector3 p)
    {
        var d = Vector3.Max(Vector3.Zero, Vector3.Max(Min - p, p - Max));
        return d.LengthSquared();
    }

    /// <summary>Slab test; returns the entry distance or -1 when the ray misses within <paramref name="maxT"/>.</summary>
    public float Intersect(Vector3 origin, Vector3 invDir, float maxT)
    {
        var t1 = (Min - origin) * invDir;
        var t2 = (Max - origin) * invDir;
        var tMin = Vector3.Min(t1, t2);
        var tMax = Vector3.Max(t1, t2);
        var enter = MathF.Max(MathF.Max(tMin.X, tMin.Y), MathF.Max(tMin.Z, 0f));
        var exit = MathF.Min(MathF.Min(tMax.X, tMax.Y), MathF.Min(tMax.Z, maxT));
        return enter <= exit ? enter : -1f;
    }
}
