#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>A ray/triangle hit.</summary>
public readonly record struct MeshHit(float Distance, int Triangle, Vector3 Point, Vector3 Normal);

/// <summary>Closest surface point to a query position.</summary>
public readonly record struct SurfacePoint(Vector3 Point, Vector3 Normal, float Distance, int Triangle)
{
    /// <summary>True when the query lies behind the surface (inside a closed shell).</summary>
    public bool Inside { get; init; }

    /// <summary>Signed distance: negative inside.</summary>
    public float Signed => Inside ? -Distance : Distance;
}

/// <summary>
/// Bounding volume hierarchy over a <see cref="TriMesh"/>: ray casts, closest-point and
/// signed-distance queries. Everything the grip solver learns about the weapon's shape goes
/// through here, so hands fit the actual geometry instead of fixed offsets.
/// </summary>
public sealed class MeshBvh
{
    private const int LeafSize = 6;

    private readonly struct Node
    {
        public readonly Bounds Box;
        public readonly int Left;   // child index, or -1 for a leaf
        public readonly int Right;
        public readonly int Start;  // leaf: first entry in _order
        public readonly int Count;

        public Node(Bounds box, int left, int right, int start, int count)
        {
            Box = box; Left = left; Right = right; Start = start; Count = count;
        }
    }

    private readonly List<Node> _nodes = new();
    private readonly int[] _order;
    private readonly Vector3[] _centroids;
    private readonly Vector3[] _normals;

    public TriMesh Mesh { get; }

    public MeshBvh(TriMesh mesh, IEnumerable<int>? triangleFilter = null)
    {
        Mesh = mesh;
        var tris = triangleFilter?.ToArray() ?? Enumerable.Range(0, mesh.TriangleCount).ToArray();
        _order = tris;
        _centroids = new Vector3[mesh.TriangleCount];
        _normals = new Vector3[mesh.TriangleCount];
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.Triangle(t);
            _centroids[t] = (a + b + c) / 3f;
            _normals[t] = mesh.FaceNormal(t);
        }
        if (_order.Length > 0)
            Build(0, _order.Length);
    }

    public bool IsEmpty => _order.Length == 0;

    private Bounds TriBounds(int t)
    {
        var (a, b, c) = Mesh.Triangle(t);
        return new Bounds(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
    }

    private int Build(int start, int count)
    {
        var box = Bounds.Empty;
        var centroidBox = Bounds.Empty;
        for (var i = start; i < start + count; i++)
        {
            box = box.Encapsulate(TriBounds(_order[i]));
            centroidBox = centroidBox.Encapsulate(_centroids[_order[i]]);
        }

        var index = _nodes.Count;
        if (count <= LeafSize)
        {
            _nodes.Add(new Node(box, -1, -1, start, count));
            return index;
        }

        var size = centroidBox.Size;
        var axis = size.X > size.Y ? (size.X > size.Z ? 0 : 2) : (size.Y > size.Z ? 1 : 2);
        float Key(int t) => axis == 0 ? _centroids[t].X : axis == 1 ? _centroids[t].Y : _centroids[t].Z;
        Array.Sort(_order, start, count, Comparer<int>.Create((x, y) => Key(x).CompareTo(Key(y))));

        _nodes.Add(default); // placeholder, children fill in below
        var half = count / 2;
        var left = Build(start, half);
        var right = Build(start + half, count - half);
        _nodes[index] = new Node(box, left, right, start, count);
        return index;
    }

    /// <summary>Nearest hit along a ray, or null.</summary>
    public MeshHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.MaxValue, bool twoSided = true)
    {
        if (IsEmpty || direction.LengthSquared() < 1e-12f)
            return null;
        direction = Vector3.Normalize(direction);
        var inv = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        MeshHit? best = null;
        var bestT = maxDistance;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            var enter = node.Box.Intersect(origin, inv, bestT);
            if (enter < 0f)
                continue;
            if (node.Left < 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = _order[i];
                    var (a, b, c) = Mesh.Triangle(t);
                    if (!RayTriangle(origin, direction, a, b, c, out var dist) || dist > bestT)
                        continue;
                    var n = _normals[t];
                    if (!twoSided && Vector3.Dot(n, direction) > 0f)
                        continue;
                    bestT = dist;
                    best = new MeshHit(dist, t, origin + direction * dist, Vector3.Dot(n, direction) > 0f ? -n : n);
                }
                continue;
            }
            stack.Push(node.Left);
            stack.Push(node.Right);
        }
        return best;
    }

    /// <summary>All hits along a ray, nearest first (used for thickness / cross-section probes).</summary>
    public List<MeshHit> RaycastAll(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var hits = new List<MeshHit>();
        if (IsEmpty || direction.LengthSquared() < 1e-12f)
            return hits;
        direction = Vector3.Normalize(direction);
        var inv = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (node.Box.Intersect(origin, inv, maxDistance) < 0f)
                continue;
            if (node.Left < 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = _order[i];
                    var (a, b, c) = Mesh.Triangle(t);
                    if (RayTriangle(origin, direction, a, b, c, out var dist) && dist <= maxDistance)
                        hits.Add(new MeshHit(dist, t, origin + direction * dist, _normals[t]));
                }
                continue;
            }
            stack.Push(node.Left);
            stack.Push(node.Right);
        }
        hits.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        return hits;
    }

    /// <summary>Closest point on the surface within <paramref name="maxDistance"/>, or null.</summary>
    public SurfacePoint? Closest(Vector3 p, float maxDistance = float.MaxValue)
    {
        if (IsEmpty)
            return null;
        var bestSq = maxDistance >= float.MaxValue ? float.MaxValue : maxDistance * maxDistance;
        var bestTri = -1;
        var bestPoint = Vector3.Zero;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (node.Box.DistanceSquared(p) > bestSq)
                continue;
            if (node.Left < 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = _order[i];
                    var (a, b, c) = Mesh.Triangle(t);
                    var q = ClosestOnTriangle(p, a, b, c);
                    var d = (q - p).LengthSquared();
                    if (d < bestSq)
                    {
                        bestSq = d;
                        bestTri = t;
                        bestPoint = q;
                    }
                }
                continue;
            }
            // Visit the nearer child last so it pops first.
            var l = _nodes[node.Left].Box.DistanceSquared(p);
            var r = _nodes[node.Right].Box.DistanceSquared(p);
            if (l < r) { stack.Push(node.Right); stack.Push(node.Left); }
            else { stack.Push(node.Left); stack.Push(node.Right); }
        }
        if (bestTri < 0)
            return null;

        var dist = MathF.Sqrt(bestSq);
        var normal = _normals[bestTri];
        var offset = p - bestPoint;
        // Inside test: the query sits behind the face it projects onto. For points that project
        // onto an edge/vertex the face normal is still a good pseudo-normal for weapon meshes.
        var inside = dist > 1e-5f && Vector3.Dot(offset, normal) < 0f;
        var outward = dist > 1e-5f ? (inside ? -offset / dist : offset / dist) : normal;
        return new SurfacePoint(bestPoint, Vector3.Dot(outward, normal) < 0 ? normal : outward, dist, bestTri) { Inside = inside };
    }

    /// <summary>
    /// Parity inside test: casts three skewed rays and takes the majority vote, which tolerates
    /// the small holes and open seams typical of game weapon meshes.
    /// </summary>
    public bool IsInside(Vector3 p, float maxDistance = 1000f)
    {
        if (IsEmpty)
            return false;
        var votes = 0;
        foreach (var dir in ParityDirections)
            if (RaycastAll(p, dir, maxDistance).Count % 2 == 1)
                votes++;
        return votes >= 2;
    }

    private static readonly Vector3[] ParityDirections =
    {
        Vector3.Normalize(new Vector3(0.577f, 0.211f, 0.789f)),
        Vector3.Normalize(new Vector3(-0.613f, 0.701f, -0.364f)),
        Vector3.Normalize(new Vector3(0.143f, -0.883f, 0.447f)),
    };

    /// <summary>Triangles whose bounds touch a sphere.</summary>
    public List<int> Overlap(Vector3 center, float radius)
    {
        var result = new List<int>();
        if (IsEmpty)
            return result;
        var rSq = radius * radius;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (node.Box.DistanceSquared(center) > rSq)
                continue;
            if (node.Left < 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = _order[i];
                    var (a, b, c) = Mesh.Triangle(t);
                    if ((ClosestOnTriangle(center, a, b, c) - center).LengthSquared() <= rSq)
                        result.Add(t);
                }
                continue;
            }
            stack.Push(node.Left);
            stack.Push(node.Right);
        }
        return result;
    }

    // Möller–Trumbore, two-sided.
    public static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(d, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-10f)
            return false;
        var inv = 1f / det;
        var s = o - a;
        var u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f)
            return false;
        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(d, q) * inv;
        if (v < 0f || u + v > 1f)
            return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 1e-5f;
    }

    // Ericson, Real-Time Collision Detection 5.1.5.
    public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));

        var denom = 1f / (va + vb + vc);
        var v = vb * denom;
        var w = vc * denom;
        return a + ab * v + ac * w;
    }
}
