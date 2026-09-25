#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>Scores a hand pose against the weapon (and the other hand).</summary>
public static class GraspEvaluator
{
    public static GraspQuality Evaluate(GraspRequest r, HandPose pose)
    {
        var shape = HandShape.Of(r.Hand, pose);
        var touching = 0;
        var floating = 0;
        var maxPen = 0f;
        foreach (var finger in r.Hand.Fingers)
        {
            var segs = shape.Segments.Where(s => s.Finger == finger.Kind).ToList();
            var fingerTouch = false;
            foreach (var seg in segs)
                for (var k = 1; k <= 4; k++)
                {
                    if (r.Weapon.Closest(seg.Point(k / 4f), seg.Radius * 3f + 0.5f) is not { } c)
                        continue;
                    var gap = c.Inside ? -c.Distance : c.Distance;
                    if (gap < seg.Radius + 0.08f)
                        fingerTouch = true;
                    maxPen = MathF.Max(maxPen, seg.Radius - gap);
                }
            if (fingerTouch)
                touching++;
            else if (!(finger.Kind == FingerKind.Index && r.Trigger is not null))
                floating++;
        }

        var palmPen = 0f;
        var palmGap = float.MaxValue;
        foreach (var p in shape.PalmPoints)
        {
            if (r.Weapon.Closest(p, 4f) is not { } c)
                continue;
            var gap = c.Inside ? -c.Distance : c.Distance;
            palmPen = MathF.Max(palmPen, -gap);
            palmGap = MathF.Min(palmGap, MathF.Max(0f, gap));
        }
        if (palmGap == float.MaxValue)
            palmGap = 4f;

        var overlap = 0f;
        if (r.OtherHand is { } other)
            foreach (var seg in shape.Segments)
                overlap = MathF.Max(overlap, ProceduralGripGenerator.OverlapsOther(seg, other));

        var deviation = r.PreferredWrist is { } pref ? MathQ.AngleBetween(pref.Rot, pose.Wrist.Rot) * 180f / MathF.PI : 0f;

        return new GraspQuality
        {
            TouchingFingers = touching,
            FloatingFingers = floating,
            MaxPenetration = MathF.Max(0f, maxPen - 0.06f),
            PalmPenetration = palmPen,
            PalmGap = palmGap,
            HandOverlap = overlap,
            WristDeviationDegrees = deviation,
        };
    }
}

/// <summary>
/// Final clean-up of a grasp: opens joints that sink into the weapon, closes fingers that
/// float, eases the palm onto the surface and keeps joints inside anatomical limits.
/// </summary>
public static class ContactRefiner
{
    public static void Refine(GraspRequest r, HandPose pose, int iterations = 3)
    {
        for (var iter = 0; iter < iterations; iter++)
        {
            var changed = false;
            foreach (var finger in r.Hand.Fingers)
            {
                var flex = pose.Flex[finger.Kind];
                // Clamp to limits (impossible joints).
                for (var j = 0; j < flex.Length; j++)
                {
                    var minFlex = finger.Kind == FingerKind.Thumb && j == 0 ? -0.8f : -0.15f;
                    var clamped = Math.Clamp(flex[j], minFlex, finger.MaxFlex[j]);
                    if (clamped != flex[j]) { flex[j] = clamped; changed = true; }
                }
                // Open joints from the tip back while the finger penetrates.
                for (var guard = 0; guard < 30; guard++)
                {
                    var (_, penetrating, joint) = ProceduralGripGenerator.FingerContact(r, pose, finger, 0);
                    if (!penetrating || joint < 0)
                        break;
                    flex[joint] = MathF.Max(-0.15f, flex[joint] - 0.03f);
                    changed = true;
                }
            }

            // Palm too far from the surface: slide the hand toward it (only when fingers aren't blocking).
            var shape = HandShape.Of(r.Hand, pose);
            var gap = float.MaxValue;
            foreach (var p in shape.PalmPoints)
                if (r.Weapon.Closest(p, 3f) is { } c && !c.Inside)
                    gap = MathF.Min(gap, c.Distance);
            if (gap is > 0.25f and < 3f && r.Style is not Analysis.GripStyle.Overlay)
            {
                var move = -r.Surface.Normal * (gap - 0.1f) * 0.6f;
                pose.Wrist.Pos += move;
                if (Penetrates(r, pose))
                    pose.Wrist.Pos -= move;
                else
                    changed = true;
            }
            if (!changed)
                break;
        }
    }

    private static bool Penetrates(GraspRequest r, HandPose pose)
    {
        foreach (var p in HandShape.Of(r.Hand, pose).PalmPoints)
            if (r.Weapon.Closest(p, 1f) is { } c && c.Inside && c.Distance > 0.02f)
                return true;
        return false;
    }
}

/// <summary>
/// Converts keypoint hands (joint positions from a neural grasp model or a MANO optimiser) into
/// the rig's joint angles, so any external generator can feed the same refine-and-bake path.
/// </summary>
public static class KeypointConverter
{
    public static HandPose ToPose(HandRig rig, HandKeypoints keypoints)
    {
        var pose = HandPose.Open(rig, keypoints.Wrist);
        foreach (var finger in rig.Fingers)
        {
            if (!keypoints.Joints.TryGetValue(finger.Kind, out var targets) || targets.Length < 2)
                continue;
            var flex = pose.Flex[finger.Kind];
            // Fit each joint angle so the next joint lands on its keypoint (1-D search per joint).
            for (var j = 0; j < finger.Joints.Length && j + 1 < targets.Length; j++)
            {
                var bestAngle = 0f;
                var bestErr = float.MaxValue;
                for (var a = -0.2f; a <= finger.MaxFlex[j]; a += 0.02f)
                {
                    flex[j] = a;
                    var segs = ProceduralGripGenerator.FingerSegments(rig, pose, finger);
                    var err = Vector3.Distance(segs[j].B, targets[j + 1]);
                    if (err < bestErr)
                    {
                        bestErr = err;
                        bestAngle = a;
                    }
                }
                flex[j] = bestAngle;
            }
        }
        return pose;
    }
}
