#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

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
