#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

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
