#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>Per-bone motion over all clips, relative to the parent bone.</summary>
public sealed record BoneMotion(int Bone, float MaxTranslation, float MaxRotationDeg, Vector3 MainDirection, string PeakClip, int PeakFrame);
