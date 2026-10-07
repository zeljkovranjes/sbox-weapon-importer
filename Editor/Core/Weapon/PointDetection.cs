#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

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
