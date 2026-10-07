#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Backends that produce hand keypoints (MANO-style joint positions) rather than bone angles
/// can convert them with <see cref="KeypointConverter"/>; this is the hand-off shape.
/// </summary>
public sealed record HandKeypoints(XForm Wrist, IReadOnlyDictionary<FingerKind, Vector3[]> Joints);
