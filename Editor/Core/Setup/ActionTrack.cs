#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Grip;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Ik;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>A character action sampled over its normalized time (see CharacterPoser.SampleTrack).</summary>
public sealed record ActionTrack(AnimationRole Role, float Seconds, IReadOnlyList<(float Time, CharacterPose Pose)> Samples);
