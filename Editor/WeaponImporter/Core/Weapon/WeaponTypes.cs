#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

public enum WeaponType { Pistol, Revolver, Rifle, Smg, Shotgun, Sniper, Launcher, Melee, Custom }

public enum PartKind { Magazine, Slide, Bolt, ChargingHandle, Trigger, Hammer, Cylinder, Pump, Foregrip, Stock, Scope, Barrel }

/// <summary>A detected moving or notable part of the weapon.</summary>
public sealed record PartDetection
{
    public required PartKind Kind { get; init; }

    /// <summary>Weapon bone driving the part, or "" for a static mesh region.</summary>
    public string Bone { get; init; } = "";

    /// <summary>Mesh part names that belong to it.</summary>
    public IReadOnlyList<string> Meshes { get; init; } = Array.Empty<string>();

    /// <summary>Centre of the part in bind pose.</summary>
    public Vector3 Center { get; init; }

    public float Confidence { get; init; }
    public string Reason { get; init; } = "";

    /// <summary>Set once the user confirmed or corrected it.</summary>
    public bool Manual { get; init; }
}

/// <summary>A located point with a direction: muzzle, shell ejection.</summary>
public sealed record PointDetection
{
    /// <summary>Bone the point is attached to.</summary>
    public required string Bone { get; init; }

    /// <summary>Transform relative to <see cref="Bone"/>; local +X is the point's direction.</summary>
    public required XForm Local { get; init; }

    /// <summary>Same point in bind-pose model space.</summary>
    public required XForm Model { get; init; }

    public float Confidence { get; init; }
    public string Reason { get; init; } = "";
    public bool Manual { get; init; }
}

public static class WeaponTypes
{
    public static string Label(WeaponType type) => type switch
    {
        WeaponType.Smg => "SMG",
        WeaponType.Sniper => "Sniper Rifle",
        _ => type.ToString(),
    };

    public static bool IsFirearm(WeaponType type) => type is not WeaponType.Melee;

    /// <summary>Whether the support hand is expected on the weapon by default.</summary>
    public static bool TwoHanded(WeaponType type) => type is WeaponType.Rifle or WeaponType.Smg or WeaponType.Shotgun
        or WeaponType.Sniper or WeaponType.Launcher or WeaponType.Pistol or WeaponType.Revolver;

    /// <summary>Citizen/human animgraph <c>holdtype</c> value for the type.</summary>
    public static int HoldType(WeaponType type) => type switch
    {
        WeaponType.Pistol or WeaponType.Revolver => 1,
        WeaponType.Rifle or WeaponType.Smg or WeaponType.Sniper => 2,
        WeaponType.Shotgun => 3,
        WeaponType.Melee => 6,
        WeaponType.Launcher => 7,
        _ => 2,
    };

    /// <summary>Typical overall length in inches; used for scale sanity checks.</summary>
    public static (float Min, float Max) TypicalLength(WeaponType type) => type switch
    {
        WeaponType.Pistol => (5f, 13f),
        WeaponType.Revolver => (7f, 16f),
        WeaponType.Smg => (12f, 30f),
        WeaponType.Rifle => (26f, 44f),
        WeaponType.Shotgun => (26f, 50f),
        WeaponType.Sniper => (36f, 60f),
        WeaponType.Launcher => (28f, 70f),
        WeaponType.Melee => (6f, 60f),
        _ => (4f, 80f),
    };

    public static IReadOnlyList<PartKind> ExpectedParts(WeaponType type) => type switch
    {
        WeaponType.Pistol => new[] { PartKind.Magazine, PartKind.Slide, PartKind.Trigger },
        WeaponType.Revolver => new[] { PartKind.Cylinder, PartKind.Hammer, PartKind.Trigger },
        WeaponType.Rifle or WeaponType.Smg => new[] { PartKind.Magazine, PartKind.Bolt, PartKind.Trigger },
        WeaponType.Shotgun => new[] { PartKind.Pump, PartKind.Trigger },
        WeaponType.Sniper => new[] { PartKind.Magazine, PartKind.Bolt, PartKind.Trigger },
        WeaponType.Launcher => new[] { PartKind.Trigger },
        _ => Array.Empty<PartKind>(),
    };
}
