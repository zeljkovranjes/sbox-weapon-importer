#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Hands;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A hand pose: wrist transform plus per-joint flexion (and base spread) angles. Stored as
/// angles so blending, limits and "smallest change" comparisons stay meaningful; converted to
/// bone rotations with <see cref="LocalRotation"/>.
/// </summary>
public sealed class HandPose
{
    /// <summary>Hand bone transform in the space the pose was solved in (weapon space for grips).</summary>
    public XForm Wrist;

    /// <summary>Flexion per finger joint (radians), keyed by finger then joint index.</summary>
    public Dictionary<FingerKind, float[]> Flex { get; } = new();

    /// <summary>Base-joint spread per finger (radians, + toward the thumb).</summary>
    public Dictionary<FingerKind, float> Spread { get; } = new();

    /// <summary>Thumb opposition (radians): swings the thumb across the palm.</summary>
    public float ThumbOpposition;

    public HandPose(XForm wrist) => Wrist = wrist;

    public static HandPose Open(HandRig rig, XForm wrist)
    {
        var pose = new HandPose(wrist);
        foreach (var f in rig.Fingers)
        {
            pose.Flex[f.Kind] = new float[f.Joints.Length];
            pose.Spread[f.Kind] = 0f;
        }
        return pose;
    }

    public HandPose Clone()
    {
        var c = new HandPose(Wrist) { ThumbOpposition = ThumbOpposition };
        foreach (var (k, v) in Flex)
            c.Flex[k] = v.ToArray();
        foreach (var (k, v) in Spread)
            c.Spread[k] = v;
        return c;
    }

    public float[] FlexOf(FingerKind kind) => Flex.TryGetValue(kind, out var f) ? f : Array.Empty<float>();

    /// <summary>Local rotation of finger joint <paramref name="j"/> for this pose.</summary>
    public Quaternion LocalRotation(HandRig rig, FingerRig finger, int j)
    {
        var rest = rig.Skeleton[finger.Joints[j]].RestLocal.Rot;
        // What the character shows: a pinky its model copies from the ring takes the ring's angles.
        var kind = rig.PinkyFollowsRing && finger.Kind == FingerKind.Pinky && Flex.ContainsKey(FingerKind.Ring) ? FingerKind.Ring : finger.Kind;
        var flex = Flex.TryGetValue(kind, out var f) && j < f.Length ? f[j] : 0f;
        var q = Quaternion.CreateFromAxisAngle(finger.FlexAxes[j], flex);
        if (j == 0)
        {
            var spread = Spread.TryGetValue(kind, out var sp) ? sp : 0f;
            if (MathF.Abs(spread) > 1e-5f)
                q = Quaternion.CreateFromAxisAngle(finger.SpreadAxis, spread) * q;
            if (finger.Kind == FingerKind.Thumb && MathF.Abs(ThumbOpposition) > 1e-5f)
            {
                // Opposition rolls the thumb about its own length while swinging it across the palm.
                q = Quaternion.CreateFromAxisAngle(finger.Along[0], ThumbOpposition * 0.6f) * Quaternion.CreateFromAxisAngle(finger.SpreadAxis, -ThumbOpposition * (rig.Side == Side.Right ? 1f : -1f) * 0.5f) * q;
            }
        }
        return MathQ.Normalize(rest * q);
    }

    /// <summary>Linear blend between two poses (angles and wrist).</summary>
    public static HandPose Lerp(HandPose a, HandPose b, float t)
    {
        var r = new HandPose(new XForm(Vector3.Lerp(a.Wrist.Pos, b.Wrist.Pos, t), MathQ.Slerp(a.Wrist.Rot, b.Wrist.Rot, t)))
        {
            ThumbOpposition = a.ThumbOpposition + (b.ThumbOpposition - a.ThumbOpposition) * t,
        };
        foreach (var (k, fa) in a.Flex)
        {
            var fb = b.Flex.TryGetValue(k, out var x) ? x : fa;
            r.Flex[k] = fa.Select((v, i) => v + ((i < fb.Length ? fb[i] : v) - v) * t).ToArray();
        }
        foreach (var (k, sa) in a.Spread)
            r.Spread[k] = sa + ((b.Spread.TryGetValue(k, out var sb) ? sb : sa) - sa) * t;
        return r;
    }
}
