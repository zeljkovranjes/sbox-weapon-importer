#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Analysis;

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
