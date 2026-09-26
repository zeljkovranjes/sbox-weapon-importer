namespace WeaponImporter;

/// <summary>
/// A reusable grip: how a character holds one weapon, made by the Weapon Importer. Transforms are
/// "px,py,pz,qx,qy,qz,qw" in the weapon model's space, finger poses "bone=qx,qy,qz,qw;..." (the
/// same formats as <see cref="WeaponHold"/>). Assign it to a Weapon Hold to use it; nothing here
/// is computed at runtime beyond the small IK correction.
/// </summary>
[AssetType( Name = "Weapon Grip Profile", Extension = "wgrip", Category = "Weapons" )]
public sealed class WeaponGripProfile : GameResource
{
    [Header( "Hands" )]
    /// <summary>The hand that holds the weapon's main grip ("R" or "L").</summary>
    public string PrimaryHand { get; set; } = "R";

    /// <summary>Primary hand bone in weapon space.</summary>
    public string PrimaryGripTransform { get; set; } = "";

    /// <summary>Primary hand finger rotations.</summary>
    public string PrimaryFingerPose { get; set; } = "";

    /// <summary>Support hand bone in weapon space ("" = one-handed).</summary>
    public string SecondaryGripTransform { get; set; } = "";

    /// <summary>Support hand finger rotations.</summary>
    public string SecondaryFingerPose { get; set; } = "";

    /// <summary>Where the index finger rests, weapon space (zero = no trigger).</summary>
    public Vector3 TriggerTarget { get; set; }

    /// <summary>Where the elbows point, weapon space; the arm IK leans toward them (keeps elbows steady).</summary>
    public Vector3 PrimaryElbowHint { get; set; }
    public Vector3 SecondaryElbowHint { get; set; }

    public bool TwoHanded { get; set; } = true;

    [Header( "Weapon" )]
    /// <summary>Weapon model relative to the hold bone.</summary>
    public string WeaponOffset { get; set; } = "";

    /// <summary>Character bone the weapon is held by.</summary>
    public string HoldBone { get; set; } = "hold_R";

    /// <summary>Dual weapons: the left-hand copy's root bone ("" = a single weapon).</summary>
    public string SecondWeaponBone { get; set; } = "";

    /// <summary>Dual weapons: the character bone the left-hand copy is held by.</summary>
    public string SecondHoldBone { get; set; } = "";

    /// <summary>Dual weapons: the left-hand copy's bone relative to <see cref="SecondHoldBone"/>.</summary>
    public string SecondWeaponOffset { get; set; } = "";

    [Header( "Contact" )]
    /// <summary>Per-action hand contact tracks (JSON, see <see cref="WeaponHold.Contacts"/>).</summary>
    public string Contacts { get; set; } = "";

    /// <summary>Per-action timing, weapon sequence and trigger parameter (JSON).</summary>
    public string Actions { get; set; } = "";

    [Range( 0, 1 )] public float Smoothing { get; set; } = 0.35f;
    public float ReachSlack { get; set; } = 1.5f;
    [Range( 0, 1 )] public float TransitionSeconds { get; set; } = 0.15f;

    [Header( "Third person" )]
    /// <summary>Citizen holdtype the weapon was fitted to (-1 = leave to the game).</summary>
    public int HoldType { get; set; } = -1;

    /// <summary>Character model the grip was fitted to.</summary>
    public string Character { get; set; } = "";

    /// <summary>Replacement character animations per action (JSON).</summary>
    public string CharacterActions { get; set; } = "";

    [Header( "First person" )]
    /// <summary>Weapon sequence for each first-person action (JSON: role → sequence).</summary>
    public string FirstPersonSequences { get; set; } = "";

    /// <summary>Suggested first-person field of view.</summary>
    public float FirstPersonFieldOfView { get; set; } = 70f;
}
