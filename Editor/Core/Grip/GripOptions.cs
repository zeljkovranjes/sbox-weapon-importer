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

/// <summary>User choices that shape the solve.</summary>
public sealed record GripOptions
{
    public bool UseSupportHand { get; init; } = true;
    public bool IndexOnTrigger { get; init; } = true;

    /// <summary>0 keeps the weapon pointing exactly where the character aims; 1 keeps the animated wrist.</summary>
    public float WristPreference { get; init; } = 0.35f;

    /// <summary>User nudge of the weapon in the hand (applied after the solve), weapon space.</summary>
    public XForm WeaponOffset { get; init; } = XForm.Identity;

    /// <summary>Hand poses edited by the user (weapon space); they replace the generated ones.</summary>
    public HandPose? RightOverride { get; init; }
    public HandPose? LeftOverride { get; init; }

    public IGripGenerator? Backend { get; init; }

    /// <summary>
    /// Preview pass: no placement search and no fine grasp search, just the direct grasp on the
    /// given surfaces (milliseconds). The editor shows it at once and refines in the background.
    /// </summary>
    public bool Fast { get; init; }
}
