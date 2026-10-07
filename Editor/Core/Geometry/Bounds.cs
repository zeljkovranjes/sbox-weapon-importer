#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.EditorTools.Core.Geometry;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

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
