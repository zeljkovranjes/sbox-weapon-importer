#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Grasp;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Several generators competing: their candidates are pooled and ranked together (the solver
/// then re-ranks the best ones with wrist bend and reach). A learned generator such as GrabNet
/// plugs in here the same way; it runs inside the background grip solve, never on the UI thread.
/// </summary>
public sealed class CompositeGripGenerator : IGripGenerator
{
    private readonly IReadOnlyList<IGripGenerator> _parts;

    public CompositeGripGenerator(params IGripGenerator[] parts) => _parts = parts.Where(p => p.IsAvailable).ToList();

    public string Name => string.Join(" + ", _parts.Select(p => p.Name));
    public bool IsAvailable => _parts.Count > 0;

    public IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default)
        => _parts.SelectMany(p => p.Generate(request, cancel)).OrderByDescending(c => c.Rank).ToList();
}
