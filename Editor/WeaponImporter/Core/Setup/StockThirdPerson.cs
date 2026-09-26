#nullable enable annotations

using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Setup;

/// <summary>How the character holds and uses a weapon with the stock third-person animations.</summary>
public enum StockStyle
{
    /// <summary>The character's own animgraph (firearms; or no stock animations).</summary>
    None,
    Melee1H,
    Melee2H,
    Polearm,
    MeleeDual,
    Fists,
    DualPistols,
    Throwable,
    Drink,
    Inject,
    Eat,
    Pills,
    Bandage,
    Device,
    /// <summary>An item held in one hand (a flashlight, a key).</summary>
    Carry,
    /// <summary>A large item held in both hands in front.</summary>
    CarryTwoHanded,
    /// <summary>A long item rested on the shoulder.</summary>
    Shoulder,
}

/// <summary>
/// The optional stock third-person animations (item use, throws, dual pistols, melee stances and
/// attacks, fists, hold poses): which style suits a weapon, and which stock sequence the
/// character plays for each action in that style. The idle entry is the hold itself, played over
/// the upper body the whole time the weapon is held.
/// </summary>
public static class StockThirdPerson
{
    /// <summary>The model holding every stock sequence (installed by the Download button).</summary>
    public const string Model = "weapon_importer/stock/wi_stock_third_person.vmdl";

    public static readonly StockStyle[] Styles = Enum.GetValues<StockStyle>();

    public static string Label(StockStyle style) => style switch
    {
        StockStyle.None => "Character animgraph",
        StockStyle.Melee1H => "One-handed melee",
        StockStyle.Melee2H => "Two-handed melee",
        StockStyle.Polearm => "Polearm",
        StockStyle.MeleeDual => "Dual melee",
        StockStyle.Fists => "Fists",
        StockStyle.DualPistols => "Dual pistols",
        StockStyle.Throwable => "Throwable",
        StockStyle.Drink => "Drink",
        StockStyle.Inject => "Injection",
        StockStyle.Eat => "Eat",
        StockStyle.Pills => "Pills",
        StockStyle.Bandage => "Bandage",
        StockStyle.Device => "Device",
        StockStyle.Carry => "Carry (one hand)",
        StockStyle.CarryTwoHanded => "Carry (two hands)",
        StockStyle.Shoulder => "On the shoulder",
        _ => style.ToString(),
    };

    private static readonly string[] PolearmNames = { "spear", "halberd", "pike", "staff", "glaive", "lance", "naginata", "polearm", "trident", "scythe", "quarterstaff", "bo" };
    private static readonly string[] TwoHandNames = { "greatsword", "claymore", "zweihander", "sledge", "sledgehammer", "warhammer", "maul", "bat", "katana", "greataxe", "fireaxe", "twohanded", "2h" };
    private static readonly string[] ThrowNames = { "grenade", "molotov", "flashbang", "frag", "throwable", "smoke", "stun", "cocktail", "dynamite" };
    private static readonly string[] InjectNames = { "syringe", "injector", "adrenaline", "stim", "stimpack", "needle", "epipen", "morphine", "shot" };
    private static readonly string[] DrinkNames = { "bottle", "flask", "potion", "drink", "soda", "beer", "water", "canteen", "bleach", "juice", "vodka", "whiskey", "wine", "cola", "can", "mug", "cup" };
    private static readonly string[] EatNames = { "food", "apple", "bread", "burger", "sandwich", "snack", "meat", "fruit", "ration", "candy", "chocolate", "banana", "eat" };
    private static readonly string[] PillNames = { "pill", "pills", "tablets", "medication", "painkiller", "painkillers", "capsule", "capsules" };
    private static readonly string[] BandageNames = { "bandage", "gauze", "medkit", "firstaid", "splint", "tourniquet" };
    private static readonly string[] DeviceNames = { "phone", "cellphone", "smartphone", "radio", "walkie", "detonator", "remote", "gps", "scanner", "pda", "tablet", "detector", "device" };

    /// <summary>
    /// The style for a weapon: melee by length and name (polearms, two-handed weapons, dual
    /// blades), items by what they are, fists, dual pistols, throwables. Single firearms keep
    /// the character's animgraph, which already holds, fires and reloads them.
    /// </summary>
    /// <param name="lengthInches">The weapon's length (muzzle axis), inches.</param>
    /// <param name="twoHanded">The support hand holds the weapon too.</param>
    /// <param name="dual">One weapon in each hand.</param>
    public static StockStyle Suggest(WeaponType type, string? name, float lengthInches, bool twoHanded, bool dual)
    {
        var tokens = NameTokens.Split(name);
        var joined = NameTokens.Joined(tokens);
        bool Named(string[] words) => NameTokens.Has(tokens, words) || words.Any(w => w.Length >= 6 && joined.Contains(w, StringComparison.Ordinal));
        switch (type)
        {
            case WeaponType.Unarmed:
                return StockStyle.Fists;
            case WeaponType.Melee:
                if (dual)
                    return StockStyle.MeleeDual;
                if (Named(PolearmNames) || lengthInches > 56f)
                    return StockStyle.Polearm;
                if (Named(TwoHandNames) || twoHanded || lengthInches > 34f)
                    return StockStyle.Melee2H;
                return StockStyle.Melee1H;
            case WeaponType.Item:
                if (Named(ThrowNames))
                    return StockStyle.Throwable;
                if (Named(InjectNames))
                    return StockStyle.Inject;
                if (Named(PillNames))
                    return StockStyle.Pills;
                if (Named(BandageNames))
                    return StockStyle.Bandage;
                if (Named(EatNames))
                    return StockStyle.Eat;
                if (Named(DrinkNames))
                    return StockStyle.Drink;
                if (Named(DeviceNames))
                    return StockStyle.Device;
                if (lengthInches > 40f)
                    return StockStyle.Shoulder;
                return twoHanded ? StockStyle.CarryTwoHanded : StockStyle.Carry;
            case WeaponType.Pistol or WeaponType.Revolver or WeaponType.Smg or WeaponType.Shotgun:
                // Two short guns (dual pistols, dual sawn-offs) aim one per hand.
                return dual && lengthInches < 30f ? StockStyle.DualPistols : StockStyle.None;
            default:
                return StockStyle.None;
        }
    }

    /// <summary>
    /// What a weapon is called, for picking its style: the file name and the folders it sits in
    /// (packs often name the file after the rig, "BRAZO.fbx", and the folder after the item).
    /// </summary>
    public static string NameOf(WeaponAsset asset)
    {
        var parts = new List<string> { asset.Name };
        var dir = string.IsNullOrEmpty(asset.SourcePath) ? null : System.IO.Path.GetDirectoryName(asset.SourcePath);
        for (var i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
        {
            parts.Add(System.IO.Path.GetFileName(dir));
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return string.Join(" ", parts);
    }

    /// <summary>A stock sequence per action, and more played in turn for attacks.</summary>
    public readonly record struct Entry(AnimationRole Role, string Sequence, string[] Variants);

    /// <summary>The stock sequences a style plays, per action (the idle entry is the hold).</summary>
    public static IReadOnlyList<Entry> Sequences(StockStyle style)
    {
        Entry E(AnimationRole role, string sequence, params string[] variants) => new(role, sequence, variants);
        return style switch
        {
            StockStyle.Melee1H => new[]
            {
                E(AnimationRole.Idle, "wi_melee_1h_idle"),
                E(AnimationRole.Fire, "wi_melee_1h_attack1", "wi_melee_1h_attack2", "wi_melee_1h_attack3"),
                E(AnimationRole.Attack2, "wi_melee_1h_attack3"),
                E(AnimationRole.Block, "wi_melee_1h_block"),
            },
            StockStyle.Melee2H => new[]
            {
                E(AnimationRole.Idle, "wi_melee_2h_idle"),
                E(AnimationRole.Fire, "wi_melee_2h_attack1", "wi_melee_2h_attack2", "wi_melee_2h_attack3"),
                E(AnimationRole.Attack2, "wi_melee_2h_attack2"),
                E(AnimationRole.Block, "wi_melee_2h_block"),
            },
            StockStyle.Polearm => new[]
            {
                E(AnimationRole.Idle, "wi_melee_polearm_idle"),
                E(AnimationRole.Fire, "wi_melee_polearm_attack1", "wi_melee_polearm_attack2"),
                E(AnimationRole.Attack2, "wi_melee_polearm_attack2"),
                E(AnimationRole.Block, "wi_melee_polearm_block"),
            },
            StockStyle.MeleeDual => new[]
            {
                E(AnimationRole.Idle, "wi_melee_1h_idle"),
                E(AnimationRole.Fire, "wi_melee_dual_attack1", "wi_melee_dual_attack2"),
                E(AnimationRole.Block, "wi_melee_dual_block"),
            },
            StockStyle.Fists => new[]
            {
                E(AnimationRole.Idle, "wi_fists_idle"),
                E(AnimationRole.Fire, "wi_fists_punch_l", "wi_fists_punch_r"),
                E(AnimationRole.Attack2, "wi_fists_punch2"),
            },
            StockStyle.DualPistols => new[]
            {
                E(AnimationRole.Idle, "wi_dual_idle"),
                E(AnimationRole.Fire, "wi_dual_fire"),
                E(AnimationRole.Reload, "wi_dual_reload"),
            },
            StockStyle.Throwable => new[]
            {
                E(AnimationRole.Idle, "wi_throw_idle"),
                E(AnimationRole.Draw, "wi_throw_equip"),
                E(AnimationRole.UseStart, "wi_throw_pinpull"),
                E(AnimationRole.UseLoop, "wi_throw_aim"),
                E(AnimationRole.Throw, "wi_throw"),
            },
            StockStyle.Drink => new[]
            {
                E(AnimationRole.Idle, "wi_hold_object"),
                E(AnimationRole.Use, "wi_item_drink"),
                E(AnimationRole.UseStart, "wi_item_drink_start"),
                E(AnimationRole.UseLoop, "wi_item_drink_loop"),
                E(AnimationRole.UseEnd, "wi_item_drink_end"),
            },
            StockStyle.Inject => new[]
            {
                E(AnimationRole.Idle, "wi_hold_object"),
                E(AnimationRole.Use, "wi_item_inject"),
            },
            StockStyle.Eat => new[]
            {
                E(AnimationRole.Idle, "wi_hold_object"),
                E(AnimationRole.Use, "wi_item_eat"),
            },
            StockStyle.Pills => new[]
            {
                E(AnimationRole.Idle, "wi_hold_object"),
                E(AnimationRole.Use, "wi_item_pills"),
            },
            StockStyle.Bandage => new[]
            {
                E(AnimationRole.Idle, "wi_hold_object"),
                E(AnimationRole.UseStart, "wi_item_bandage_start"),
                E(AnimationRole.UseLoop, "wi_item_bandage_loop"),
                E(AnimationRole.UseEnd, "wi_item_bandage_end"),
            },
            StockStyle.Device => new[]
            {
                E(AnimationRole.Idle, "wi_hold_object"),
                E(AnimationRole.UseStart, "wi_item_device_start"),
                E(AnimationRole.UseLoop, "wi_item_device_loop"),
                E(AnimationRole.UseEnd, "wi_item_device_end"),
            },
            StockStyle.Carry => new[] { E(AnimationRole.Idle, "wi_hold_object") },
            StockStyle.CarryTwoHanded => new[] { E(AnimationRole.Idle, "wi_hold_2h") },
            StockStyle.Shoulder => new[] { E(AnimationRole.Idle, "wi_hold_shoulder") },
            _ => Array.Empty<Entry>(),
        };
    }

    /// <summary>
    /// Writes a style's stock sequences into the setup's third-person actions, only for actions
    /// the user hasn't set and only with sequences the installed stock model has. Actions the
    /// style doesn't cover go back to the character's animgraph (unless the user set them).
    /// </summary>
    public static void Apply(WeaponSetup setup, StockStyle style, IReadOnlyCollection<string> installed)
    {
        foreach (var (role, tp) in setup.ThirdPerson.ToList())
            if (!tp.Manual && tp.Source == CharacterAnimationSource.Sequence && tp.Model == Model)
                setup.ThirdPerson[role] = new CharacterAnimation { Source = CharacterAnimationSource.Graph };
        foreach (var entry in Sequences(style))
        {
            if (!installed.Contains(entry.Sequence))
                continue;
            if (setup.ThirdPerson.TryGetValue(entry.Role, out var current) && current.Manual)
                continue;
            setup.ThirdPerson[entry.Role] = new CharacterAnimation
            {
                Source = CharacterAnimationSource.Sequence,
                Model = Model,
                Sequence = entry.Sequence,
                Variants = entry.Variants.Where(installed.Contains).ToList(),
            };
        }
    }
}
