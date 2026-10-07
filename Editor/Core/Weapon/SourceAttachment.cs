#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

/// <summary>An attachment point already present on the source (a VMDL's attachments).</summary>
public sealed record SourceAttachment(string Name, string Bone, XForm Local);
