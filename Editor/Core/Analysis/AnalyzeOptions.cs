#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>Overrides for the analyzer, set when the user corrects orientation or scale.</summary>
public sealed record AnalyzeOptions
{
    public Quaternion? Rotation { get; init; }
    public float? Scale { get; init; }

    /// <summary>Clip whose first frame is treated as the assembled weapon.</summary>
    public string? ReferenceClip { get; init; }
}
