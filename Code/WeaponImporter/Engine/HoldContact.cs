using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WeaponImporter.Engine;

/// <summary>One key of a hand contact track (see <c>WeaponHold.Contacts</c>).</summary>
public sealed class HoldContact
{
    /// <summary>True for the left (support) hand, false for the right hand.</summary>
    public bool Left { get; set; } = true;

    /// <summary>Normalized action time (0..1) at which the blend starts.</summary>
    public float T { get; set; }

    /// <summary>True blends to locked (weight 1), false to released (weight 0).</summary>
    public bool Lock { get; set; }

    /// <summary>Blend duration in seconds.</summary>
    public float Blend { get; set; }

    /// <summary>Weapon bone the hand target follows while locked ("" = the weapon itself).</summary>
    public string Follow { get; set; } = "";

    /// <summary>True when <see cref="Offset"/> was given.</summary>
    public bool HasOffset { get; set; }

    /// <summary>Extra weapon-space offset: position added, rotation pre-multiplied (weapon axes).</summary>
    public Transform Offset { get; set; } = Transform.Zero;
}
