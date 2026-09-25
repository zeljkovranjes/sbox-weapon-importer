#nullable enable annotations

namespace WeaponImporter.Core.Analysis;

/// <summary>What an animation is for. The importer maps each role to one clip.</summary>
public enum AnimationRole
{
    Unknown,
    Idle,
    Fire,
    FireEmpty,
    Reload,
    TacticalReload,
    EmptyReload,
    Draw,
    Holster,
    Inspect,
    Sprint,
    Walk,
    Ads,
    AdsFire,
    Melee,
    Bolt,
    Jam,
    Unjam,
    /// <summary>Lowering the sights (played when aiming stops).</summary>
    AdsOut,
    /// <summary>Looping aimed pose (while aiming, after ADS raised the sights).</summary>
    AdsIdle,
}

public static class AnimationRoles
{
    /// <summary>Roles shown in the editor, in the order they appear.</summary>
    public static readonly AnimationRole[] All =
    {
        AnimationRole.Idle, AnimationRole.Fire, AnimationRole.FireEmpty, AnimationRole.Reload,
        AnimationRole.TacticalReload, AnimationRole.EmptyReload, AnimationRole.Draw, AnimationRole.Holster,
        AnimationRole.Inspect, AnimationRole.Sprint, AnimationRole.Walk, AnimationRole.Ads, AnimationRole.AdsIdle, AnimationRole.AdsOut, AnimationRole.AdsFire,
        AnimationRole.Melee, AnimationRole.Bolt, AnimationRole.Jam, AnimationRole.Unjam,
    };

    /// <summary>Roles every firearm should have; missing ones are reported by validation.</summary>
    public static readonly AnimationRole[] Required = { AnimationRole.Fire, AnimationRole.Reload };

    public static string Label(AnimationRole role) => role switch
    {
        AnimationRole.FireEmpty => "Fire Empty",
        AnimationRole.TacticalReload => "Tactical Reload",
        AnimationRole.EmptyReload => "Empty Reload",
        AnimationRole.Ads => "ADS",
        AnimationRole.AdsOut => "ADS Out",
        AnimationRole.AdsIdle => "ADS Idle",
        AnimationRole.AdsFire => "ADS Fire",
        AnimationRole.Bolt => "Bolt / Charge",
        _ => role.ToString(),
    };

    /// <summary>
    /// Roles that cycle. ADS is not one: it raises the sights and the aimed pose is held at its
    /// last frame while the player aims (Weapon Hold's Aiming).
    /// </summary>
    public static bool Loops(AnimationRole role)
        => role is AnimationRole.Idle or AnimationRole.Sprint or AnimationRole.Walk or AnimationRole.AdsIdle;

    /// <summary>Role the given one falls back to when it has no clip (empty reload plays reload).</summary>
    public static AnimationRole? Fallback(AnimationRole role) => role switch
    {
        AnimationRole.TacticalReload or AnimationRole.EmptyReload => AnimationRole.Reload,
        AnimationRole.FireEmpty or AnimationRole.AdsFire => AnimationRole.Fire,
        AnimationRole.Ads or AnimationRole.Walk or AnimationRole.Inspect => AnimationRole.Idle,
        AnimationRole.Sprint => AnimationRole.Walk,
        AnimationRole.Unjam => AnimationRole.Bolt,
        _ => null,
    };

    /// <summary>
    /// The citizen/human animgraph parameter that triggers the role in game, when there is one.
    /// </summary>
    public static string? GraphTrigger(AnimationRole role) => role switch
    {
        AnimationRole.Fire or AnimationRole.AdsFire or AnimationRole.FireEmpty => "b_attack",
        AnimationRole.Reload or AnimationRole.TacticalReload or AnimationRole.EmptyReload => "b_reload",
        AnimationRole.Draw => "b_deploy",
        _ => null,
    };
}
