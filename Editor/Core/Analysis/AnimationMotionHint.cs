#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Analysis;

/// <summary>Motion measurements used when a name alone is not enough.</summary>
public readonly record struct AnimationMotionHint(float Duration, float MagazineTravel, float BoltTravel, float RootTravel, float MaxBoneMotion);
