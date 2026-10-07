#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>What a region of the weapon is for, from the hands' point of view.</summary>
public enum GripRegionKind
{
    /// <summary>Where the firing hand holds (pistol grip, stock wrist, melee handle).</summary>
    PrimaryGrip,
    /// <summary>Where the support hand holds (handguard, pump, second handle, pistol overlay).</summary>
    SecondaryGrip,
    /// <summary>What the firing index finger rests on.</summary>
    Trigger,
    /// <summary>Where the support hand works during actions: the magazine (well) for reloads.</summary>
    Support,
    /// <summary>Hands must stay off it: muzzle, barrel end, blade.</summary>
    Forbidden,
}
