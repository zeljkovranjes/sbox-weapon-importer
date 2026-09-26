#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Core.Ik;

using Vector3 = System.Numerics.Vector3;

/// <summary>A character pose: parent-relative locals with lazily derived world transforms.</summary>
public sealed class CharacterPose
{
    public Skeleton Skeleton { get; }
    public XForm[] Locals { get; }
    private XForm[]? _world;

    public CharacterPose(Skeleton skeleton, XForm[] locals)
    {
        if (locals.Length != skeleton.Count)
            throw new ArgumentException("Pose bone count does not match the skeleton.", nameof(locals));
        Skeleton = skeleton;
        Locals = locals;
    }

    public static CharacterPose Rest(Skeleton skeleton) => new(skeleton, skeleton.Bones.Select(b => b.RestLocal).ToArray());

    public CharacterPose Clone() => new(Skeleton, Locals.ToArray());

    /// <summary>
    /// This pose with the upper body (the <paramref name="upperRoot"/> subtree: spine, head and
    /// arms) replaced by another pose's model-space transforms, matched by bone name. It is what
    /// Weapon Hold shows while a hold pose plays: the legs keep the animgraph, the rest follows
    /// the pose. Bones the other pose lacks keep theirs.
    /// </summary>
    public CharacterPose WithUpperBody(IReadOnlyDictionary<string, XForm> overlayModel, string upperRoot = "spine_0")
    {
        var root = Skeleton.IndexOf(upperRoot);
        if (root < 0)
            root = Skeleton.IndexOf("spine_1");
        if (root < 0 || overlayModel.Count == 0)
            return Clone();
        var inside = new bool[Skeleton.Count];
        inside[root] = true;
        for (var i = 0; i < Skeleton.Count; i++)
            if (Skeleton[i].ParentIndex >= 0 && inside[Skeleton[i].ParentIndex])
                inside[i] = true;
        var world = World.ToArray();
        // Anchored at the upper body's root: the overlay bends the upper body but stays on the
        // hips this pose has (as Weapon Hold plays it).
        var offset = overlayModel.TryGetValue(Skeleton[root].Name, out var anchor) ? world[root].Pos - anchor.Pos : System.Numerics.Vector3.Zero;
        for (var i = 0; i < Skeleton.Count; i++)
        {
            if (inside[i] && overlayModel.TryGetValue(Skeleton[i].Name, out var w))
                world[i] = new XForm(w.Pos + offset, w.Rot);
            else if (inside[i] && Skeleton[i].ParentIndex >= 0)
                world[i] = XForm.Compose(world[Skeleton[i].ParentIndex], Locals[i]);
        }
        var locals = Locals.ToArray();
        for (var i = 0; i < Skeleton.Count; i++)
            if (inside[i])
                locals[i] = Skeleton[i].ParentIndex >= 0 ? XForm.ToLocal(world[Skeleton[i].ParentIndex], world[i]) : world[i];
        return new CharacterPose(Skeleton, locals);
    }

    public XForm[] World => _world ??= ComputeWorld();

    public void Invalidate() => _world = null;

    private XForm[] ComputeWorld()
    {
        var w = new XForm[Locals.Length];
        for (var i = 0; i < w.Length; i++)
        {
            var p = Skeleton[i].ParentIndex;
            w[i] = p < 0 ? Locals[i] : XForm.Compose(w[p], Locals[i]);
        }
        return w;
    }

    /// <summary>Sets a bone's world rotation (children follow), keeping its position.</summary>
    public void SetWorldRotation(int bone, Quaternion world)
    {
        var parent = Skeleton[bone].ParentIndex;
        var parentRot = parent < 0 ? Quaternion.Identity : World[parent].Rot;
        Locals[bone].Rot = MathQ.Normalize(Quaternion.Conjugate(parentRot) * world);
        Invalidate();
    }

    /// <summary>Sets a bone's world transform (children follow).</summary>
    public void SetWorld(int bone, XForm world)
    {
        var parent = Skeleton[bone].ParentIndex;
        Locals[bone] = parent < 0 ? world : XForm.ToLocal(World[parent], world);
        Invalidate();
    }
}

/// <summary>Result of reaching for a target.</summary>
public readonly record struct ReachResult(bool Reached, float Shortfall, float ElbowBendDegrees);

/// <summary>
/// Analytic two-bone arm IK. The elbow stays in the plane it already had in the animation, so
/// the solve makes the smallest change that gets the wrist onto its target.
/// </summary>
public static class ArmIk
{
    /// <summary>
    /// Moves upper arm and forearm so the hand reaches <paramref name="target"/>, blending by
    /// <paramref name="weight"/>. The hand takes the target rotation. Returns reach info.
    /// </summary>
    public static ReachResult Solve(CharacterPose pose, HandRig arm, XForm target, float weight = 1f)
    {
        if (weight <= 1e-4f)
            return new ReachResult(true, 0f, 0f);
        var world = pose.World;
        var upper = world[arm.UpperArm];
        var lower = world[arm.LowerArm];
        var hand = world[arm.Hand];

        var desired = weight >= 0.9999f ? target : new XForm(Vector3.Lerp(hand.Pos, target.Pos, weight), MathQ.Slerp(hand.Rot, target.Rot, weight));

        var s = upper.Pos;
        var e = lower.Pos;
        var w = hand.Pos;
        var a = Vector3.Distance(s, e);
        var b = Vector3.Distance(e, w);
        var toTarget = desired.Pos - s;
        var dist = toTarget.Length();
        var reach = a + b;
        var shortfall = MathF.Max(0f, dist - reach * 0.999f);
        var c = Math.Clamp(dist, MathF.Abs(a - b) + 1e-3f, reach * 0.999f);
        var d = dist > 1e-5f ? toTarget / dist : Vector3.Normalize(w - s);

        // Bend direction: the animation's own bend (elbow offset from the shoulder->animated-wrist
        // line), so it never flips when the target swings past the elbow. Nearly straight arms
        // ease toward a relaxed down-and-back elbow.
        var animReach = w - s;
        var animPole = animReach.LengthSquared() > 1e-8f ? (e - s) - Vector3.Normalize(animReach) * Vector3.Dot(e - s, Vector3.Normalize(animReach)) : Vector3.Zero;
        var relaxed = Vector3.Normalize(new Vector3(-0.2f, 0f, -0.8f));
        var straightness = Math.Clamp(animPole.Length() / ((a + b) * 0.08f), 0f, 1f);
        var bend = (animPole.LengthSquared() > 1e-10f ? Vector3.Normalize(animPole) : relaxed) * straightness + relaxed * (1f - straightness);
        bend -= d * Vector3.Dot(bend, d);
        if (bend.LengthSquared() < 1e-8f)
        {
            bend = Vector3.Cross(d, Vector3.Cross(-Vector3.UnitZ, d));
            if (bend.LengthSquared() < 1e-8f)
                bend = MathQ.Perpendicular(d);
        }
        bend = Vector3.Normalize(bend);

        var cosA = Math.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
        var sinA = MathF.Sqrt(MathF.Max(0f, 1f - cosA * cosA));
        var newElbow = s + (d * cosA + bend * sinA) * a;

        // Rotate the upper arm so the elbow lands on its new spot.
        var upperDelta = MathQ.FromTo(e - s, newElbow - s);
        pose.SetWorldRotation(arm.UpperArm, MathQ.Normalize(upperDelta * upper.Rot));

        // Then the forearm so the wrist lands on the (clamped) target.
        world = pose.World;
        var elbowNow = world[arm.LowerArm].Pos;
        var wristNow = world[arm.Hand].Pos;
        var clampedTarget = s + d * c;
        var lowerDelta = MathQ.FromTo(wristNow - elbowNow, clampedTarget - elbowNow);
        pose.SetWorldRotation(arm.LowerArm, MathQ.Normalize(lowerDelta * world[arm.LowerArm].Rot));

        // Hand orientation.
        pose.SetWorldRotation(arm.Hand, desired.Rot);

        var bendDeg = 180f - MathF.Acos(Math.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * 180f / MathF.PI;
        return new ReachResult(shortfall < 0.05f, shortfall, bendDeg);
    }

    /// <summary>Applies finger joint rotations of a hand pose to a character pose.</summary>
    public static void ApplyFingers(CharacterPose pose, HandRig rig, HandPose hand, float weight = 1f)
    {
        foreach (var finger in rig.Fingers)
            for (var j = 0; j < finger.Joints.Length; j++)
            {
                var bone = finger.Joints[j];
                var target = hand.LocalRotation(rig, finger, j);
                pose.Locals[bone].Rot = weight >= 0.9999f ? target : MathQ.Slerp(pose.Locals[bone].Rot, target, weight);
            }
        pose.Invalidate();
    }
}
