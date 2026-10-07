#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>Registry of available backends; the first available one that yields candidates wins.</summary>
public static class GripGenerators
{
    private static readonly List<IGripGenerator> _backends = new() { new ProceduralGripGenerator() };

    public static IReadOnlyList<IGripGenerator> All => _backends;

    /// <summary>Adds an editor-only backend ahead of the procedural fallback.</summary>
    public static void Register(IGripGenerator backend)
    {
        if (_backends.Any(b => b.Name == backend.Name))
            return;
        _backends.Insert(0, backend);
    }
}
