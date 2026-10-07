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

/// <summary>Result of the grip solve in the form the runtime component consumes.</summary>
public sealed class BakedGrip
{
    public string Character { get; set; } = "";
    public string HoldBone { get; set; } = "hold_R";
    /// <summary>Weapon model (as compiled) relative to the hold bone.</summary>
    public float[] WeaponInHold { get; set; } = new float[7];
    /// <summary>Hand bones in canonical weapon space.</summary>
    public float[] RightHand { get; set; } = new float[7];
    public float[]? LeftHand { get; set; }
    /// <summary>Finger local rotations: bone name -> quaternion.</summary>
    public Dictionary<string, float[]> RightFingers { get; set; } = new();
    public Dictionary<string, float[]> LeftFingers { get; set; } = new();
    /// <summary>Elbow positions of the fitted reference pose, weapon space (IK hints).</summary>
    public float[]? RightElbow { get; set; }
    public float[]? LeftElbow { get; set; }
    public string Quality { get; set; } = "";

    /// <summary>
    /// Dual weapons: the left-hand copy's root bone in the weapon model, and its transform
    /// relative to <see cref="SecondHoldBone"/> (it follows the left hand).
    /// </summary>
    public string SecondWeaponBone { get; set; } = "";
    public string SecondHoldBone { get; set; } = "";
    public float[]? SecondInHold { get; set; }

    /// <summary>Largest change treated as sampling noise for hand, weapon and finger values.</summary>
    public const float PoseNoise = 1e-3f;

    /// <summary>Largest change treated as sampling noise for the elbow hints (inches).</summary>
    public const float ElbowNoise = 0.5f;

    /// <summary>
    /// The character's animgraph has random idle layers, so poses sampled from it differ slightly
    /// between sessions. Values that moved less than that noise keep their previous value, so
    /// re-importing and re-baking an unchanged weapon writes identical files; real edits (far
    /// larger) come through unchanged.
    /// </summary>
    public BakedGrip SettledOn(BakedGrip? previous)
    {
        if (previous is null || previous.Character != Character || previous.HoldBone != HoldBone)
            return this;
        WeaponInHold = Keep(WeaponInHold, previous.WeaponInHold, PoseNoise)!;
        RightHand = Keep(RightHand, previous.RightHand, PoseNoise)!;
        LeftHand = Keep(LeftHand, previous.LeftHand, PoseNoise);
        RightElbow = Keep(RightElbow, previous.RightElbow, ElbowNoise);
        LeftElbow = Keep(LeftElbow, previous.LeftElbow, ElbowNoise);
        SecondInHold = Keep(SecondInHold, previous.SecondInHold, PoseNoise);
        foreach (var name in RightFingers.Keys.ToList())
            if (previous.RightFingers.TryGetValue(name, out var old))
                RightFingers[name] = Keep(RightFingers[name], old, PoseNoise)!;
        foreach (var name in LeftFingers.Keys.ToList())
            if (previous.LeftFingers.TryGetValue(name, out var old))
                LeftFingers[name] = Keep(LeftFingers[name], old, PoseNoise)!;
        if (RightHand == previous.RightHand && LeftHand == previous.LeftHand)
            Quality = previous.Quality;
        return this;
    }

    private static float[]? Keep(float[]? next, float[]? previous, float tolerance)
    {
        if (next is null || previous is null || next.Length != previous.Length)
            return next;
        for (var i = 0; i < next.Length; i++)
            if (MathF.Abs(next[i] - previous[i]) > tolerance)
                return next;
        return previous;
    }
}
