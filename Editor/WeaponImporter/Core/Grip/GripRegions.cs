#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>What a region of the weapon is for, from the hands' point of view.</summary>
public enum GripRegionKind
{
    /// <summary>Where the firing hand holds (pistol grip, stock wrist, melee handle).</summary>
    PrimaryGrip,
    /// <summary>Where the support hand holds (handguard, pump, second handle, pistol overlay).</summary>
    SecondaryGrip,
    /// <summary>What the firing index finger rests on.</summary>
    Trigger,
    /// <summary>Where the support hand works during actions: the magazine (well) for reloads.</summary>
    Support,
    /// <summary>Hands must stay off it: muzzle, barrel end, blade.</summary>
    Forbidden,
}

/// <summary>
/// A semantic area on the weapon (canonical space: inches, muzzle +X, up +Z, left +Y). The grip
/// solver places hands in the grip regions, the contact planner uses Support during reloads,
/// and validation flags hands inside Forbidden.
/// </summary>
public sealed record GripRegion
{
    public required GripRegionKind Kind { get; init; }
    public required Vector3 Center { get; init; }

    /// <summary>Half size of the region's box along the weapon axes.</summary>
    public required Vector3 HalfExtents { get; init; }

    /// <summary>Surface the hand touches (for grips and the magazine grab), when measured.</summary>
    public GripSurface? Surface { get; init; }

    public float Confidence { get; init; }
    public string Reason { get; init; } = "";
    public bool Manual { get; init; }

    public bool Contains(Vector3 p, float margin = 0f)
    {
        var d = Vector3.Abs(p - Center);
        return d.X <= HalfExtents.X + margin && d.Y <= HalfExtents.Y + margin && d.Z <= HalfExtents.Z + margin;
    }

    public static string Label(GripRegionKind kind) => kind switch
    {
        GripRegionKind.PrimaryGrip => "Primary grip",
        GripRegionKind.SecondaryGrip => "Secondary grip",
        GripRegionKind.Trigger => "Trigger",
        GripRegionKind.Support => "Magazine",
        GripRegionKind.Forbidden => "Keep clear",
        _ => kind.ToString(),
    };
}

/// <summary>Finds the semantic regions of a weapon from its analysis and geometry.</summary>
public static class GripRegions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WeaponAnalysis, List<GripRegion>> Cache = new();

    /// <summary>The regions of an analysis, computed once (validation asks after every edit).</summary>
    public static IReadOnlyList<GripRegion> Of(WeaponAnalysis a) => Cache.GetValue(a, Detect);

    public static List<GripRegion> Detect(WeaponAnalysis a)
    {
        var list = new List<GripRegion>();
        if (a.Primary is { } p)
            list.Add(FromGrip(GripRegionKind.PrimaryGrip, p));
        if (a.Support is { } s)
            list.Add(FromGrip(GripRegionKind.SecondaryGrip, s));
        if (a.Part(PartKind.Trigger) is { } t)
            list.Add(new GripRegion { Kind = GripRegionKind.Trigger, Center = t.Center, HalfExtents = new Vector3(0.35f, 0.25f, 0.5f), Confidence = t.Confidence, Reason = "trigger" });
        if (Magazine(a) is { } mag)
            list.Add(mag);
        list.AddRange(Forbidden(a));
        return list;
    }

    private static GripRegion FromGrip(GripRegionKind kind, GripCandidate g)
    {
        var s = g.Surface;
        var half = new Vector3(MathF.Max(0.5f, (s.ExtentBack + s.ExtentForward) * 0.5f), MathF.Max(0.4f, s.Width * 0.5f), MathF.Max(0.4f, s.Depth * 0.5f));
        return new GripRegion { Kind = kind, Center = s.Center, HalfExtents = half, Surface = s, Confidence = g.Confidence, Reason = g.Reason, Manual = g.Manual };
    }

    /// <summary>
    /// The magazine the support hand grabs during reloads: the magazine part when one was
    /// detected, else the lowest protrusion ahead of the grip on long guns, or the bottom of the
    /// grip on pistols (where the new magazine goes in). The grab surface is on the left side
    /// (+Y), a third of the way up from the bottom, where a hand takes a magazine.
    /// </summary>
    public static GripRegion? Magazine(WeaponAnalysis a)
    {
        var type = a.Type.Type;
        if (!WeaponTypes.IsFirearm(type) || type is WeaponType.Revolver or WeaponType.Launcher || a.WeaponBvh.IsEmpty)
            return null;

        Bounds? box = null;
        var reason = "";
        if (a.Part(PartKind.Magazine) is { } part)
        {
            var tris = GripFinder.PartTriangles(a, part).ToArray();
            if (tris.Length > 0)
            {
                box = Bounds.FromPoints(tris.SelectMany(t => { var (x, y, z) = a.Asset.Mesh.Triangle(t); return new[] { x, y, z }; }));
                reason = part.Bone.Length > 0 ? $"magazine ({part.Bone})" : "magazine";
            }
        }
        if (box is null && type is WeaponType.Pistol)
        {
            if (a.Primary is { } grip)
            {
                // The magazine goes in at the bottom of the grip.
                var bottom = a.WeaponBvh.Raycast(grip.Surface.Center, -Vector3.UnitZ, 12f);
                var z = bottom?.Point.Z ?? grip.Surface.Center.Z - 1.5f;
                var c = new Vector3(grip.Surface.Center.X, grip.Surface.Center.Y, z + 0.6f);
                box = new Bounds(c - new Vector3(0.6f, 0.5f, 0.6f), c + new Vector3(0.6f, 0.5f, 0.6f));
                reason = "bottom of the grip (magazine well)";
            }
        }
        if (box is null && type == WeaponType.Shotgun)
        {
            // Tube magazines load through the port under the receiver, just ahead of the trigger guard.
            var p = a.Profile;
            var gripX = a.Primary?.Surface.Center.X ?? (a.WeaponBounds.Min.X + a.Length * 0.35f);
            var x = (a.Part(PartKind.Trigger)?.Center.X ?? gripX + 1.5f) + 2.5f;
            var bin = p.Bin(x);
            if (p.Has(bin))
            {
                var c = new Vector3(x, (p.Left[bin] + p.Right[bin]) * 0.5f, p.Bottom[bin] + 0.5f);
                box = new Bounds(c - new Vector3(1.2f, 0.6f, 0.5f), c + new Vector3(1.2f, 0.6f, 0.5f));
                reason = "loading port under the receiver";
            }
        }
        box ??= ProtrusionAheadOfGrip(a, out reason);
        if (box is not { } b)
            return null;

        var center = b.Center;
        var half = b.Size * 0.5f;
        // Grab a third of the way up from the bottom, from the left side.
        var inside = new Vector3(center.X, center.Y, b.Min.Z + MathF.Max(0.4f, b.Size.Z * 0.35f));
        GripSurface? surface = null;
        // Magazine wells under a grip and loading ports are reached from below.
        if (type is WeaponType.Pistol || reason.StartsWith("loading port"))
            surface = GripFinder.ProbeFromInside(a.WeaponBvh, inside + Vector3.UnitZ * 0.3f, -Vector3.UnitZ, Vector3.UnitX);
        surface ??= GripFinder.ProbeFromInside(a.WeaponBvh, inside, Vector3.Normalize(new Vector3(0.1f, 1f, -0.15f)), Vector3.UnitZ);
        return new GripRegion { Kind = GripRegionKind.Support, Center = center, HalfExtents = half, Surface = surface, Confidence = reason.StartsWith("magazine") ? 0.8f : 0.5f, Reason = reason };
    }

    /// <summary>A box-shaped drop below the receiver ahead of the firing grip (a magazine without its own part).</summary>
    private static Bounds? ProtrusionAheadOfGrip(WeaponAnalysis a, out string reason)
    {
        reason = "";
        var p = a.Profile;
        var gripX = a.Primary?.Surface.Center.X ?? (a.WeaponBounds.Min.X + a.Length * 0.35f);
        var triggerX = a.Part(PartKind.Trigger)?.Center.X ?? gripX + 1.5f;
        var from = p.Bin(MathF.Max(gripX + 1f, triggerX + 0.4f));
        var to = p.Bin(gripX + MathF.Min(12f, a.Length * 0.45f));
        if (to <= from)
            return null;
        var receiverBottom = p.Percentile(i => p.Bottom[i], 0.7f, p.Bin(a.WeaponBounds.Min.X + a.Length * 0.2f), p.Bin(a.WeaponBounds.Min.X + a.Length * 0.8f));
        var runs = p.Runs(i => i >= from && i <= to && p.Bottom[i] < receiverBottom - 1.2f, 2);
        if (runs.Count == 0)
            return null;
        var run = runs.OrderBy(r => MathF.Abs(p.X((r.Start + r.End) / 2) - triggerX)).First();
        var x0 = p.X(run.Start) - p.BinWidth * 0.5f;
        var x1 = p.X(run.End) + p.BinWidth * 0.5f;
        var bottom = Enumerable.Range(run.Start, run.End - run.Start + 1).Where(p.Has).Min(i => p.Bottom[i]);
        var left = Enumerable.Range(run.Start, run.End - run.Start + 1).Where(p.Has).Max(i => p.Left[i]);
        var right = Enumerable.Range(run.Start, run.End - run.Start + 1).Where(p.Has).Min(i => p.Right[i]);
        reason = $"drop below the receiver ahead of the grip ({receiverBottom - bottom:0.#} in)";
        return new Bounds(new Vector3(x0, right, bottom), new Vector3(x1, left, receiverBottom));
    }

    private static IEnumerable<GripRegion> Forbidden(WeaponAnalysis a)
    {
        var type = a.Type.Type;
        if (WeaponTypes.IsFirearm(type))
        {
            var muzzle = a.Muzzle?.Model.Pos ?? a.BoreEnd;
            var length = MathF.Min(3f, a.Length * 0.12f);
            var r = MathF.Max(0.6f, a.BoreDiameter);
            yield return new GripRegion
            {
                Kind = GripRegionKind.Forbidden,
                Center = muzzle - Vector3.UnitX * length * 0.5f,
                HalfExtents = new Vector3(length * 0.5f, r, r),
                Confidence = 0.8f,
                Reason = "muzzle",
            };
        }
        else if (type == WeaponType.Melee && a.Primary is { } handle)
        {
            // Everything past the handle's far end along its axis is blade/head.
            var axis = handle.Surface.Axis;
            var b = a.WeaponBounds;
            var far = Vector3.Dot(b.Max - handle.Surface.Center, axis) > Vector3.Dot(b.Min - handle.Surface.Center, axis) ? b.Max : b.Min;
            var start = handle.Surface.Center + axis * (handle.Surface.ExtentForward + 1f);
            var center = (start + far) * 0.5f;
            yield return new GripRegion
            {
                Kind = GripRegionKind.Forbidden,
                Center = center,
                HalfExtents = Vector3.Abs(far - start) * 0.5f + new Vector3(0.5f),
                Confidence = 0.5f,
                Reason = "blade / head",
            };
        }
    }
}
