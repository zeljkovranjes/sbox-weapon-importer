#nullable enable annotations

using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Setup;

/// <summary>
/// How long each action lasts at runtime (the WeaponHold action table): the mapped weapon clip's
/// length, else a sensible default. Everything that plans over an action's normalized time
/// (contact planning, checks, previews) uses this so it matches the game.
/// </summary>
public static class ActionTiming
{
    public static float Seconds(WeaponSetup setup, WeaponAsset? asset, AnimationRole role)
    {
        // The character drives actions it triggers itself; the weapon clip is stretched to fit.
        // Shell-reload parts keep their own clip lengths (the reload is as long as its shells).
        // A replacement character animation (stock or picked) replaces the character's own action:
        // the weapon clip decides, else the animation itself (see OverlayDecides).
        if (AnimationRoles.GraphTrigger(role) is not null && !AnimationRoles.IsShellReload(role) && !OverlayDecides(setup, role) && setup.ActionSeconds.TryGetValue(role, out var measured) && measured > 0.05f)
            return measured;
        if (setup.WeaponAnimations.TryGetValue(role, out var binding) && asset?.FindClip(binding.Clip) is { Duration: > 0.01f } clip)
            return MathF.Max(clip.Duration, 0.05f);
        return DefaultSeconds(role);
    }

    /// <summary>
    /// The character plays a replacement animation for this role instead of its animgraph action,
    /// so the animgraph's timing doesn't apply.
    /// </summary>
    public static bool OverlayDecides(WeaponSetup setup, AnimationRole role)
        => setup.ThirdPerson.TryGetValue(role, out var tp) && tp.Source == CharacterAnimationSource.Sequence && !string.IsNullOrEmpty(tp.Sequence);

    /// <summary>
    /// Seconds the baked action table holds: 0 lets the replacement animation decide at runtime
    /// when the weapon has no clip of its own for the role.
    /// </summary>
    public static float BakedSeconds(WeaponSetup setup, WeaponAsset? asset, AnimationRole role)
    {
        var hasClip = setup.WeaponAnimations.TryGetValue(role, out var binding) && asset?.FindClip(binding.Clip) is { Duration: > 0.01f };
        return OverlayDecides(setup, role) && !hasClip ? 0f : Seconds(setup, asset, role);
    }

    public static float DefaultSeconds(AnimationRole role) => role switch
    {
        AnimationRole.Fire or AnimationRole.AdsFire or AnimationRole.FireEmpty => 0.25f,
        AnimationRole.Reload or AnimationRole.TacticalReload or AnimationRole.EmptyReload => 2.2f,
        AnimationRole.Draw => 0.8f,
        AnimationRole.Holster => 0.6f,
        AnimationRole.Melee => 0.8f,
        AnimationRole.Bolt => 0.9f,
        AnimationRole.ReloadStart or AnimationRole.ReloadEnd => 0.6f,
        AnimationRole.ReloadInsert => 0.5f,
        _ => 1f,
    };

    /// <summary>Roles whose support-hand contacts are planned from the character's animation.</summary>
    public static readonly AnimationRole[] ReloadRoles = { AnimationRole.Reload, AnimationRole.TacticalReload, AnimationRole.EmptyReload };
}
