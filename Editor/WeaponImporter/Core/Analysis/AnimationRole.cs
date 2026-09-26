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
    /// <summary>Shell-by-shell reload, first part (to the loading port).</summary>
    ReloadStart,
    /// <summary>Shell-by-shell reload: one shell (repeated per shell).</summary>
    ReloadInsert,
    /// <summary>Shell-by-shell reload, last part (pump, back to idle).</summary>
    ReloadEnd,
    /// <summary>Secondary / heavy attack (right click: a stab, a heavy swing).</summary>
    Attack2,
    /// <summary>Raising the guard (melee, fists).</summary>
    BlockStart,
    /// <summary>Holding the guard (loops while blocking).</summary>
    Block,
    /// <summary>Lowering the guard.</summary>
    BlockEnd,
    /// <summary>Using an item once (drink, inject, eat, apply).</summary>
    Use,
    /// <summary>Starting a held use (bringing the item up).</summary>
    UseStart,
    /// <summary>Held use (loops while the item is in use).</summary>
    UseLoop,
    /// <summary>Finishing a held use.</summary>
    UseEnd,
    /// <summary>Throwing the item (grenade, knife, bottle).</summary>
    Throw,
}

public static class AnimationRoles
{
    /// <summary>Roles shown in the editor, in the order they appear.</summary>
    public static readonly AnimationRole[] All =
    {
        AnimationRole.Idle, AnimationRole.Fire, AnimationRole.FireEmpty, AnimationRole.Reload,
        AnimationRole.TacticalReload, AnimationRole.EmptyReload, AnimationRole.ReloadStart, AnimationRole.ReloadInsert, AnimationRole.ReloadEnd, AnimationRole.Draw, AnimationRole.Holster,
        AnimationRole.Inspect, AnimationRole.Sprint, AnimationRole.Walk, AnimationRole.Ads, AnimationRole.AdsIdle, AnimationRole.AdsOut, AnimationRole.AdsFire,
        AnimationRole.Melee, AnimationRole.Attack2, AnimationRole.BlockStart, AnimationRole.Block, AnimationRole.BlockEnd,
        AnimationRole.Use, AnimationRole.UseStart, AnimationRole.UseLoop, AnimationRole.UseEnd, AnimationRole.Throw,
        AnimationRole.Bolt, AnimationRole.Jam, AnimationRole.Unjam,
    };

    /// <summary>Roles a weapon of this kind can use (firearm roles are hidden for items, and the other way round).</summary>
    public static bool AppliesTo(AnimationRole role, Weapon.WeaponType type)
    {
        var firearm = Weapon.WeaponTypes.IsFirearm(type);
        var custom = type == Weapon.WeaponType.Custom;
        return role switch
        {
            AnimationRole.FireEmpty or AnimationRole.Reload or AnimationRole.TacticalReload or AnimationRole.EmptyReload
                or AnimationRole.ReloadStart or AnimationRole.ReloadInsert or AnimationRole.ReloadEnd or AnimationRole.AdsFire
                or AnimationRole.Bolt or AnimationRole.Jam or AnimationRole.Unjam or AnimationRole.Melee => firearm,
            AnimationRole.BlockStart or AnimationRole.Block or AnimationRole.BlockEnd or AnimationRole.Attack2 => !firearm || custom,
            AnimationRole.Use or AnimationRole.UseStart or AnimationRole.UseLoop or AnimationRole.UseEnd
                => type is Weapon.WeaponType.Item or Weapon.WeaponType.Unarmed || custom,
            _ => true,
        };
    }

    /// <summary>A role's name for a weapon type: attacks are "Attack" for melee weapons, fists and items.</summary>
    public static string Label(AnimationRole role, Weapon.WeaponType type)
        => role == AnimationRole.Fire && !Weapon.WeaponTypes.IsFirearm(type) ? "Attack" : Label(role);

    /// <summary>Roles that may hold several clips played in turn (left and right punches, a combo of slashes).</summary>
    public static bool HasVariants(AnimationRole role) => role is AnimationRole.Fire or AnimationRole.Melee or AnimationRole.Attack2;

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
        AnimationRole.ReloadStart => "Reload Start",
        AnimationRole.ReloadInsert => "Insert Shell",
        AnimationRole.ReloadEnd => "Reload End",
        AnimationRole.AdsFire => "ADS Fire",
        AnimationRole.Bolt => "Bolt / Charge",
        AnimationRole.Attack2 => "Heavy Attack",
        AnimationRole.BlockStart => "Block Start",
        AnimationRole.BlockEnd => "Block End",
        AnimationRole.UseStart => "Use Start",
        AnimationRole.UseLoop => "Use Loop",
        AnimationRole.UseEnd => "Use End",
        _ => role.ToString(),
    };

    /// <summary>
    /// Roles that cycle. ADS is not one: it raises the sights and the aimed pose is held at its
    /// last frame while the player aims (Weapon Hold's Aiming).
    /// </summary>
    /// <summary>A part of a shell-by-shell reload (start, one shell, end).</summary>
    public static bool IsShellReload(AnimationRole role)
        => role is AnimationRole.ReloadStart or AnimationRole.ReloadInsert or AnimationRole.ReloadEnd;

    public static bool Loops(AnimationRole role)
        => role is AnimationRole.Idle or AnimationRole.Sprint or AnimationRole.Walk or AnimationRole.AdsIdle or AnimationRole.Block or AnimationRole.UseLoop;

    /// <summary>Role the given one falls back to when it has no clip (empty reload plays reload).</summary>
    public static AnimationRole? Fallback(AnimationRole role) => role switch
    {
        AnimationRole.TacticalReload or AnimationRole.EmptyReload => AnimationRole.Reload,
        // A shotgun that loads shell by shell has no whole reload: the one-shell part stands in.
        AnimationRole.Reload => AnimationRole.ReloadInsert,
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
        AnimationRole.Reload or AnimationRole.TacticalReload or AnimationRole.EmptyReload or AnimationRole.ReloadStart or AnimationRole.ReloadInsert or AnimationRole.ReloadEnd => "b_reload",
        AnimationRole.Draw => "b_deploy",
        _ => null,
    };
}
