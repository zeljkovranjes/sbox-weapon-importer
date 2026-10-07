#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

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
