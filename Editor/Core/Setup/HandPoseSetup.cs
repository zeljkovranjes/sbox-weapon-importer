#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>Serializable <see cref="HandPose"/>.</summary>
public sealed class HandPoseSetup
{
    public float[] WristPosition { get; set; } = new float[3];
    public float[] WristRotation { get; set; } = { 0, 0, 0, 1 };
    public Dictionary<FingerKind, float[]> Flex { get; set; } = new();
    public Dictionary<FingerKind, float> Spread { get; set; } = new();
    public float ThumbOpposition { get; set; }

    public static HandPoseSetup From(HandPose pose) => new()
    {
        WristPosition = V.A(pose.Wrist.Pos),
        WristRotation = V.A(pose.Wrist.Rot),
        Flex = pose.Flex.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
        Spread = pose.Spread.ToDictionary(kv => kv.Key, kv => kv.Value),
        ThumbOpposition = pose.ThumbOpposition,
    };

    public HandPose ToPose()
    {
        var pose = new HandPose(new XForm(V.Of(WristPosition), V.Q(WristRotation))) { ThumbOpposition = ThumbOpposition };
        foreach (var (k, v) in Flex) pose.Flex[k] = v.ToArray();
        foreach (var (k, v) in Spread) pose.Spread[k] = v;
        return pose;
    }
}
