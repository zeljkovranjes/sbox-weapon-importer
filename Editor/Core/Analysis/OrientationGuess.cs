#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

public sealed record OrientationGuess(Quaternion ToCanonical, float Confidence, string Reason)
{
    public bool NeedsFix => MathF.Abs(ToCanonical.W) < 0.9999f;
}
