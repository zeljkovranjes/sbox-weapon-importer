namespace WeaponImporter.Engine;

/// <summary>
/// Result of an analytic two-bone solve. Both rotations are deltas expressed in the space the
/// input joints were given in (character model space in practice): <see cref="UpperDelta"/>
/// rotates the whole arm about the shoulder, <see cref="LowerDelta"/> is applied afterwards and
/// rotates the forearm (and everything below it) about the corrected elbow.
/// </summary>
public struct ArmSolution
{
    /// <summary>Rotation about the shoulder applied to the upper arm and all its descendants.</summary>
    public Rotation UpperDelta;

    /// <summary>Rotation about the corrected elbow applied after <see cref="UpperDelta"/>.</summary>
    public Rotation LowerDelta;

    /// <summary>Corrected elbow position.</summary>
    public Vector3 Elbow;

    /// <summary>Corrected wrist position (equals the target unless it was out of reach).</summary>
    public Vector3 Wrist;

    /// <summary>Distance from the shoulder to the requested target.</summary>
    public float TargetDistance;

    /// <summary>Full reach of the chain (upper length + lower length).</summary>
    public float Reach;

    /// <summary>How far the wrist stays short of the target (0 when reached).</summary>
    public float Shortfall;
}
