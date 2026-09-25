#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Grasp;

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

public sealed record GraspCandidate(HandPose Pose, GraspQuality Quality, string Source)
{
    /// <summary>Preference on top of the quality score (a grip authored for this very weapon).</summary>
    public float Bonus { get; init; }

    public float Rank => Quality.Score + Bonus;
}

/// <summary>
/// A source of hand poses for a grip. The procedural backend ships with the importer; heavier
/// editor-only generators (GraspXL, BG-HOP, MANO optimisers...) can implement this and hand
/// back candidates, which are then refined against the geometry and baked into ordinary bone
/// rotations. Nothing here runs in the game.
/// </summary>
public interface IGripGenerator
{
    string Name { get; }

    /// <summary>False when the backend's model files or runtime are missing.</summary>
    bool IsAvailable { get; }

    IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default);
}

/// <summary>
/// Backends that produce hand keypoints (MANO-style joint positions) rather than bone angles
/// can convert them with <see cref="KeypointConverter"/>; this is the hand-off shape.
/// </summary>
public sealed record HandKeypoints(XForm Wrist, IReadOnlyDictionary<FingerKind, Vector3[]> Joints);

/// <summary>Registry of available backends; the first available one that yields candidates wins.</summary>
public static class GripGenerators
{
    private static readonly List<IGripGenerator> _backends = new() { new ProceduralGripGenerator() };

    public static IReadOnlyList<IGripGenerator> All => _backends;

    /// <summary>Adds an editor-only backend ahead of the procedural fallback.</summary>
    public static void Register(IGripGenerator backend)
    {
        if (_backends.Any(b => b.Name == backend.Name))
            return;
        _backends.Insert(0, backend);
    }
}
