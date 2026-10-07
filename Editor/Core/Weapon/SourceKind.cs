#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

public enum SourceKind { Fbx, Gltf, Vmdl, Synthetic }
