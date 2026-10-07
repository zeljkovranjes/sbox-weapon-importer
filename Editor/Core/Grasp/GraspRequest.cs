#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>What a grasp backend is asked to do. All positions are in weapon space.</summary>
public sealed record GraspRequest
{
    public required HandRig Hand { get; init; }

    /// <summary>Weapon geometry the hand must hold without passing through.</summary>
    public required MeshBvh Weapon { get; init; }

    public required GripSurface Surface { get; init; }
    public required GripStyle Style { get; init; }

    /// <summary>Direction the index finger side of the hand should face (toward the bore / muzzle).</summary>
    public required Vector3 IndexToward { get; init; }

    /// <summary>Trigger contact point for the firing hand; null for support hands and melee.</summary>
    public Vector3? Trigger { get; init; }

    /// <summary>Rest the index finger along the frame instead of on the trigger.</summary>
    public bool IndexOffTrigger { get; init; }

    /// <summary>The other hand, already posed, which this one must not intersect.</summary>
    public HandShape? OtherHand { get; init; }

    /// <summary>Wrist the animation already has; candidates closer to it are preferred.</summary>
    public XForm? PreferredWrist { get; init; }

    /// <summary>Extra gap between palm and surface (support hand over the firing hand).</summary>
    public float PalmOffset { get; init; }

    /// <summary>Wrist roll/slide search range; 0 disables the search (fast path for runtime-like refits).</summary>
    public int SearchSteps { get; init; } = 2;
}
