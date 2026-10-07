#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A source of hand poses for a grip. The procedural backend ships with the importer; heavier
/// editor-only generators (GraspXL, BG-HOP, MANO optimisers...) can implement this and hand
/// back candidates, which are then refined against the geometry and baked into ordinary bone
/// rotations. Nothing here runs in the game.
/// </summary>
public interface IGripGenerator
{
    string Name { get; }

    /// <summary>False when the backend's model files or runtime are missing.</summary>
    bool IsAvailable { get; }

    IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default);
}
