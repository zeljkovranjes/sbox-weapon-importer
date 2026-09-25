#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Hands;

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
        var flex = Flex.TryGetValue(finger.Kind, out var f) && j < f.Length ? f[j] : 0f;
        var q = Quaternion.CreateFromAxisAngle(finger.FlexAxes[j], flex);
        if (j == 0)
        {
            var spread = Spread.TryGetValue(finger.Kind, out var sp) ? sp : 0f;
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

/// <summary>World-space samples of a posed hand for contact and penetration tests.</summary>
public sealed class HandShape
{
    public readonly record struct Segment(FingerKind Finger, int Joint, Vector3 A, Vector3 B, float Radius)
    {
        public Vector3 Point(float t) => Vector3.Lerp(A, B, t);
    }

    public List<Segment> Segments { get; } = new();

    /// <summary>
    /// Contact radius of a finger segment: tapers toward the tip. The thumb's first segment is
    /// the fleshy metacarpal web, which yields against a grip, so it is modelled thinner.
    /// </summary>
    public static float SegmentRadius(FingerRig finger, int joint)
        => finger.Radius * (1f - 0.12f * joint) * (finger.Kind == FingerKind.Thumb && joint == 0 ? 0.55f : 1f);

    /// <summary>Points on the palm skin, with the outward palm normal.</summary>
    public List<Vector3> PalmPoints { get; } = new();
    public Vector3 PalmNormal { get; private set; }

    /// <summary>World transform of every finger joint bone (plus the hand).</summary>
    public Dictionary<int, XForm> JointWorld { get; } = new();

    public static HandShape Of(HandRig rig, HandPose pose)
    {
        var shape = new HandShape();
        var skeleton = rig.Skeleton;
        var handWorld = pose.Wrist;
        shape.JointWorld[rig.Hand] = handWorld;

        foreach (var finger in rig.Fingers)
        {
            // Walk from the hand down to the first joint through any metacarpal at rest.
            var chainToBase = new List<int>();
            for (var b = skeleton[finger.Joints[0]].ParentIndex; b >= 0 && b != rig.Hand; b = skeleton[b].ParentIndex)
                chainToBase.Add(b);
            var world = handWorld;
            for (var i = chainToBase.Count - 1; i >= 0; i--)
            {
                world = XForm.Compose(world, skeleton[chainToBase[i]].RestLocal);
                shape.JointWorld[chainToBase[i]] = world;
            }
            for (var j = 0; j < finger.Joints.Length; j++)
            {
                var local = skeleton[finger.Joints[j]].RestLocal;
                local.Rot = pose.LocalRotation(rig, finger, j);
                world = XForm.Compose(world, local);
                shape.JointWorld[finger.Joints[j]] = world;
                var a = world.Pos;
                var b = world.TransformPoint(finger.Along[j] * finger.Lengths[j]);
                // Taper toward the tip.
                var radius = HandShape.SegmentRadius(finger, j);
                shape.Segments.Add(new Segment(finger.Kind, j, a, b, radius));
            }
        }

        // Palm: a small grid on the palm skin between wrist and knuckles.
        var n = handWorld.TransformVector(rig.PalmNormal);
        shape.PalmNormal = n;
        var k = rig.KnuckleLine;
        var skin = rig.PalmNormal * (rig.PalmThickness * 0.5f);
        for (var u = 0; u <= 3; u++)
            for (var v = 0; v <= 3; v++)
            {
                var along = Vector3.Lerp(rig.PalmCenter - (rig.KnuckleCenter - rig.PalmCenter) * 0.6f, rig.KnuckleCenter, u / 3f);
                var across = k * (rig.PalmWidth * (v / 3f - 0.5f) * 0.85f);
                shape.PalmPoints.Add(handWorld.TransformPoint(along + across + skin));
            }
        return shape;
    }

    /// <summary>Fingertip position of a finger.</summary>
    public Vector3 Tip(FingerKind finger) => Segments.Where(s => s.Finger == finger).OrderBy(s => s.Joint).Last().B;
}
