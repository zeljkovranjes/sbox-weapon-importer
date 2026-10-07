#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

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
