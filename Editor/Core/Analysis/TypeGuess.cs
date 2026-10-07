#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

public sealed record TypeGuess(WeaponType Type, float Confidence, string Reason, IReadOnlyDictionary<WeaponType, float> Scores);
