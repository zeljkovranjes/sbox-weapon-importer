using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Setup;
using WeaponImporter.EditorTools.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.EditorTools.UI;
using WeaponImporter.EditorTools.Engine;
using WeaponImporter.EditorTools.UI.Widgets;

/// <summary>What the next click on the weapon in the viewport places.</summary>
public enum PickTarget { None, RightGrip, LeftGrip, Muzzle, Eject }
