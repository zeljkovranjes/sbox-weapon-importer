#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

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
