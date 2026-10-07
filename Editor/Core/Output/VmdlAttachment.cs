#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Output;

using Vector3 = System.Numerics.Vector3;

public sealed record VmdlAttachment(string Name, string Bone, Vector3 Position, Quaternion Rotation);
