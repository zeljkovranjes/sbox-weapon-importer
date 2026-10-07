#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

public enum GripStyle
{
    /// <summary>Fingers wrap a vertical handle: pistol grip, vertical foregrip.</summary>
    Wrap,
    /// <summary>Palm under a horizontal handguard, fingers up the side.</summary>
    Cradle,
    /// <summary>Hand around a shotgun pump / forend.</summary>
    Pump,
    /// <summary>Support hand cupping the primary hand on a pistol.</summary>
    Overlay,
    /// <summary>Melee handle held in a fist.</summary>
    Handle,
}
