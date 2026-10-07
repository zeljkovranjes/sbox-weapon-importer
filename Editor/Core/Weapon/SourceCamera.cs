#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A camera in the file: the local axes it looks along and holds up, and its vertical field
/// of view in degrees (0 when the file doesn't say).
/// </summary>
public readonly record struct SourceCamera(Vector3 Forward, Vector3 Up, float FieldOfView = 0f);
