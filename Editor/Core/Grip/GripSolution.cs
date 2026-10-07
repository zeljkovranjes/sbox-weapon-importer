#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Grasp;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Ik;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>Everything a baked grip needs, plus diagnostics.</summary>
public sealed class GripSolution
{
    /// <summary>Weapon (canonical space) relative to the hold bone.</summary>
    public required XForm WeaponInHold { get; init; }

    /// <summary>Firing hand in weapon space, with finger angles.</summary>
    public required HandPose Right { get; init; }
    public HandPose? Left { get; init; }

    public required GraspQuality RightQuality { get; init; }
    public GraspQuality? LeftQuality { get; init; }

    /// <summary>The grasp problems the hands were solved for (to re-score edited poses cheaply).</summary>
    public GraspRequest? RightRequest { get; init; }
    public GraspRequest? LeftRequest { get; init; }

    /// <summary>Rotation the firing wrist needs away from the animation, degrees.</summary>
    public float RightWristCorrection { get; init; }
    public float LeftWristCorrection { get; init; }

    /// <summary>Anatomical wrist bend after the solve (hand vs forearm, vs rest), degrees.</summary>
    public float RightWristBend { get; init; }
    public float LeftWristBend { get; init; }
    public ReachResult RightReach { get; init; }
    public ReachResult LeftReach { get; init; }

    /// <summary>Reference pose with the grip applied (preview / validation).</summary>
    public required CharacterPose Posed { get; init; }

    /// <summary>Which grip each hand came from ("Procedural", a library grip's name, "Edited").</summary>
    public string RightSource { get; init; } = "";
    public string LeftSource { get; init; } = "";

    public List<string> Notes { get; } = new();
}
