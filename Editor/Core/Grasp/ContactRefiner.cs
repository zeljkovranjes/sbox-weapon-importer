#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

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
