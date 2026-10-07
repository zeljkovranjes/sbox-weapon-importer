#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Hands;

using Vector3 = System.Numerics.Vector3;

/// <summary>One finger: its joint bones from the knuckle outward, with flex axes.</summary>
public sealed class FingerRig
{
    public required FingerKind Kind { get; init; }

    /// <summary>Joint bones, proximal first (metacarpals are not included).</summary>
    public required int[] Joints { get; init; }

    /// <summary>Segment lengths, one per joint (the last one is estimated to the fingertip).</summary>
    public required float[] Lengths { get; init; }

    /// <summary>Flexion axis of each joint in that joint's own rest-local frame.</summary>
    public required Vector3[] FlexAxes { get; init; }

    /// <summary>Spread (abduction) axis of the base joint, local frame.</summary>
    public required Vector3 SpreadAxis { get; init; }

    /// <summary>Flexion limits per joint (radians).</summary>
    public required float[] MaxFlex { get; init; }

    /// <summary>Finger radius used for contact tests.</summary>
    public required float Radius { get; init; }

    /// <summary>Local direction a joint points along (toward its child), rest frame.</summary>
    public required Vector3[] Along { get; init; }
}
