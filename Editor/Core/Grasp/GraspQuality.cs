#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>How good a hand pose is against the geometry.</summary>
public sealed record GraspQuality
{
    public int TouchingFingers { get; init; }
    public int FloatingFingers { get; init; }
    public float MaxPenetration { get; init; }
    public float PalmPenetration { get; init; }
    public float PalmGap { get; init; }
    public float HandOverlap { get; init; }
    public float WristDeviationDegrees { get; init; }

    /// <summary>Higher is better.</summary>
    public float Score => TouchingFingers * 1.0f - FloatingFingers * 0.8f - MaxPenetration * 6f - PalmPenetration * 8f
        - MathF.Max(0f, PalmGap - 0.15f) * 3f - HandOverlap * 6f - WristDeviationDegrees * 0.01f;

    public override string ToString()
        => $"touch {TouchingFingers}, floating {FloatingFingers}, penetration {MaxPenetration:0.00}, palm {PalmPenetration:0.00}/{PalmGap:0.00}, overlap {HandOverlap:0.00}, wrist {WristDeviationDegrees:0}°";
}
