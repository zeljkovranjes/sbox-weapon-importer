#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Ik;

using Vector3 = System.Numerics.Vector3;

/// <summary>Result of reaching for a target.</summary>
public readonly record struct ReachResult(bool Reached, float Shortfall, float ElbowBendDegrees);
