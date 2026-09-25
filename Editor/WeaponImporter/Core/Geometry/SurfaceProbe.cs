#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// What the solver knows about a point on the weapon a hand should hold. Every value is
/// measured from the mesh, never assumed.
/// </summary>
public sealed record GripSurface
{
    /// <summary>Contact point on the surface.</summary>
    public required Vector3 Contact { get; init; }

    /// <summary>Outward surface normal at the contact.</summary>
    public required Vector3 Normal { get; init; }

    /// <summary>Long axis of the local shape (along a pistol grip, along a handguard).</summary>
    public required Vector3 Axis { get; init; }

    /// <summary>Estimated centre of the cross-section under the contact.</summary>
    public required Vector3 Center { get; init; }

    /// <summary>Thickness through the part along the normal.</summary>
    public required float Depth { get; init; }

    /// <summary>Width across the part, perpendicular to both axis and normal.</summary>
    public required float Width { get; init; }

    /// <summary>Radius a wrapping hand closes around (half the mean of depth and width).</summary>
    public float Radius => MathF.Max(0.15f, (Depth + Width) * 0.25f);

    /// <summary>Free space outward from the contact before other geometry (for the palm).</summary>
    public required float Clearance { get; init; }

    /// <summary>How far the shape runs along the axis either side of the contact.</summary>
    public required float ExtentBack { get; init; }
    public required float ExtentForward { get; init; }

    /// <summary>Triangles within a hand-sized sphere around the contact.</summary>
    public required int NearbyTriangles { get; init; }

    /// <summary>Part (mesh) names within reach of the hand.</summary>
    public required IReadOnlyList<string> NearbyParts { get; init; }

    /// <summary>Surface triangle the contact lies on.</summary>
    public required int Triangle { get; init; }

    /// <summary>
    /// Local frame: X = axis, Y = normal x axis, Z = outward normal (projected orthogonal).
    /// </summary>
    public Quaternion Frame => MathQ.FromAxes(Axis, Vector3.Cross(Normal, Axis));
}

/// <summary>Measures <see cref="GripSurface"/> data at points on a weapon mesh.</summary>
public static class SurfaceProbe
{
    /// <summary>Probe radius for the local-shape analysis; about half a hand width in inches.</summary>
    public const float PatchRadius = 2.2f;

    /// <summary>
    /// Probes the surface where a ray (e.g. a viewport click) hits the weapon.
    /// </summary>
    public static GripSurface? FromRay(MeshBvh bvh, Vector3 origin, Vector3 direction, Vector3? axisHint = null)
    {
        var hit = bvh.Raycast(origin, direction);
        return hit is null ? null : Measure(bvh, hit.Value.Point, hit.Value.Normal, hit.Value.Triangle, axisHint);
    }

    /// <summary>Probes the surface point nearest to <paramref name="near"/>.</summary>
    public static GripSurface? FromPoint(MeshBvh bvh, Vector3 near, Vector3? axisHint = null)
    {
        var closest = bvh.Closest(near);
        if (closest is null)
            return null;
        var c = closest.Value;
        var normal = bvh.Mesh.FaceNormal(c.Triangle);
        // Winding is unreliable in game meshes: the outward side is the side the query came from.
        if (c.Distance > 1e-3f && Vector3.Dot(near - c.Point, normal) < 0f)
            normal = -normal;
        return Measure(bvh, c.Point, normal, c.Triangle, axisHint);
    }

    public static GripSurface Measure(MeshBvh bvh, Vector3 contact, Vector3 normal, int triangle, Vector3? axisHint = null)
    {
        normal = SafeNormalize(normal, Vector3.UnitZ);

        // Principal axis of the patch: the direction the surface keeps running in.
        var axis = PrincipalAxis(bvh, contact, normal, axisHint);

        // Cross-section: thickness along -normal, width along the binormal through the centre.
        var depth = Thickness(bvh, contact, -normal, 12f);
        var center = contact - normal * (depth * 0.5f);
        var binormal = SafeNormalize(Vector3.Cross(normal, axis), MathQ.Perpendicular(normal));
        var width = Span(bvh, center, binormal, 12f);
        if (width <= 0f)
            width = depth;

        var clearance = Clearance(bvh, contact, normal, 8f);
        var back = RunLength(bvh, center, -axis, normal, depth, 14f);
        var forward = RunLength(bvh, center, axis, normal, depth, 14f);

        var nearby = bvh.Overlap(contact, 3.5f);
        var parts = nearby.Select(t => bvh.Mesh.PartNames[bvh.Mesh.TrianglePart[t]]).Distinct().ToArray();

        return new GripSurface
        {
            Contact = contact,
            Normal = normal,
            Axis = axis,
            Center = center,
            Depth = depth,
            Width = width,
            Clearance = clearance,
            ExtentBack = back,
            ExtentForward = forward,
            NearbyTriangles = nearby.Count,
            NearbyParts = parts,
            Triangle = triangle,
        };
    }

    /// <summary>
    /// Largest-variance direction of the surface points near the contact, orthogonalised
    /// against the normal. Falls back to the hint (or a stable perpendicular) on flat patches.
    /// </summary>
    public static Vector3 PrincipalAxis(MeshBvh bvh, Vector3 contact, Vector3 normal, Vector3? hint)
    {
        var mesh = bvh.Mesh;
        var tris = bvh.Overlap(contact, PatchRadius);
        var points = new List<(Vector3 P, float W)>();
        foreach (var t in tris)
        {
            var (a, b, c) = mesh.Triangle(t);
            var area = Vector3.Cross(b - a, c - a).Length() * 0.5f;
            if (area <= 0f)
                continue;
            var centroid = (a + b + c) / 3f;
            // Weight by area and proximity so a nearby trigger guard doesn't steer the axis.
            var falloff = 1f - MathF.Min(1f, Vector3.Distance(centroid, contact) / PatchRadius);
            points.Add((centroid, area * falloff * falloff));
        }

        var fallback = hint is { } h && (h - normal * Vector3.Dot(h, normal)).LengthSquared() > 1e-6f
            ? Vector3.Normalize(h - normal * Vector3.Dot(h, normal))
            : MathQ.Perpendicular(normal);
        if (points.Count < 3)
            return fallback;

        var total = points.Sum(p => p.W);
        if (total <= 0f)
            return fallback;
        var mean = points.Aggregate(Vector3.Zero, (acc, p) => acc + p.P * p.W) / total;
        var cov = new float[3, 3];
        foreach (var (p, w) in points)
        {
            var d = p - mean;
            var v = new[] { d.X, d.Y, d.Z };
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    cov[i, j] += v[i] * v[j] * w;
        }

        // Power iteration on the covariance projected into the tangent plane.
        var dir = fallback;
        for (var iter = 0; iter < 32; iter++)
        {
            var next = new Vector3(
                cov[0, 0] * dir.X + cov[0, 1] * dir.Y + cov[0, 2] * dir.Z,
                cov[1, 0] * dir.X + cov[1, 1] * dir.Y + cov[1, 2] * dir.Z,
                cov[2, 0] * dir.X + cov[2, 1] * dir.Y + cov[2, 2] * dir.Z);
            next -= normal * Vector3.Dot(next, normal);
            if (next.LengthSquared() < 1e-12f)
                return fallback;
            dir = Vector3.Normalize(next);
        }

        // Keep a stable sign so repeated probes agree with the hint.
        if (hint is { } hh && Vector3.Dot(dir, hh) < 0f)
            dir = -dir;
        return dir;
    }

    /// <summary>Distance through the solid from an entry point along a direction.</summary>
    public static float Thickness(MeshBvh bvh, Vector3 entry, Vector3 dir, float max)
    {
        var start = entry + dir * 0.01f;
        var hit = bvh.Raycast(start, dir, max);
        return hit is null ? 0.8f : hit.Value.Distance + 0.01f;
    }

    /// <summary>Full width of the solid across a centre point along ±dir.</summary>
    public static float Span(MeshBvh bvh, Vector3 center, Vector3 dir, float max)
    {
        var a = bvh.Raycast(center, dir, max);
        var b = bvh.Raycast(center, -dir, max);
        if (a is null || b is null)
            return 0f;
        return a.Value.Distance + b.Value.Distance;
    }

    /// <summary>Free distance outward from the surface before other geometry.</summary>
    public static float Clearance(MeshBvh bvh, Vector3 contact, Vector3 normal, float max)
    {
        var hit = bvh.Raycast(contact + normal * 0.02f, normal, max);
        return hit is null ? max : hit.Value.Distance;
    }

    /// <summary>
    /// How far a cross-section of similar depth continues along the axis. Steps along the axis
    /// and stops once the part gets much thinner/thicker or ends.
    /// </summary>
    public static float RunLength(MeshBvh bvh, Vector3 center, Vector3 axis, Vector3 normal, float depth, float max)
    {
        const float step = 0.25f;
        var run = 0f;
        for (var s = step; s <= max; s += step)
        {
            var c = center + axis * s;
            // Re-measure the thickness at this station from outside, along the same normal.
            var outside = c + normal * (depth + 2f);
            var hit = bvh.Raycast(outside, -normal, depth + 4f);
            if (hit is null)
                break;
            var entry = hit.Value.Point;
            var local = Thickness(bvh, entry, -normal, depth * 3f + 2f);
            if (local < depth * 0.45f || local > depth * 2.2f + 0.5f)
                break;
            run = s;
        }
        return run;
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : fallback;
}
