#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>A place on the weapon a hand should hold.</summary>
public sealed record GripCandidate
{
    public required GripSurface Surface { get; init; }
    public required GripStyle Style { get; init; }
    public float Confidence { get; init; }
    public string Reason { get; init; } = "";
    public bool Manual { get; init; }
}
