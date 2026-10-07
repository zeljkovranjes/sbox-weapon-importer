#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Hands;

using Vector3 = System.Numerics.Vector3;

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
