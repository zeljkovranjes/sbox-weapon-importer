#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

/// <summary>What is held: firearms, melee weapons, items (flashlight, bottle, syringe, phone...) or bare fists.</summary>
public enum WeaponType { Pistol, Revolver, Rifle, Smg, Shotgun, Sniper, Launcher, Melee, Item, Unarmed, Custom }
