#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

public sealed record GraspCandidate(HandPose Pose, GraspQuality Quality, string Source)
{
    /// <summary>Preference on top of the quality score (a grip authored for this very weapon).</summary>
    public float Bonus { get; init; }

    public float Rank => Quality.Score + Bonus;
}
