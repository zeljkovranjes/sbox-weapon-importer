#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Grasp;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Ik;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>The character side of a grip solve.</summary>
public sealed record CharacterRig
{
    public required HandRig Right { get; init; }
    public HandRig? Left { get; init; }

    /// <summary>Bone the weapon follows in game (hold_R, else hand_R).</summary>
    public required int HoldBone { get; init; }

    /// <summary>Direction the character aims in model space (s&amp;box characters face +X).</summary>
    public Vector3 Forward { get; init; } = Vector3.UnitX;
    public Vector3 Up { get; init; } = Vector3.UnitZ;

    public static CharacterRig? From(Rig.Skeleton skeleton, bool pinkyFollowsRing = false)
    {
        var right = HandRig.Build(skeleton, Side.Right);
        if (right is null)
            return null;
        var left = HandRig.Build(skeleton, Side.Left);
        right.PinkyFollowsRing = pinkyFollowsRing;
        if (left is not null)
            left.PinkyFollowsRing = pinkyFollowsRing;
        var hold = skeleton.IndexOf("hold_R");
        return new CharacterRig { Right = right, Left = left, HoldBone = hold >= 0 ? hold : right.Hand };
    }
}
