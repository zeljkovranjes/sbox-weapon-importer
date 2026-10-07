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
/// The grip source per hand from the user's choice: "" = automatic (library grips and the
/// procedural fit compete), "Procedural" = only the procedural fit, a preset name = that grip.
/// </summary>
public sealed class ChoiceGripGenerator : IGripGenerator
{
    public const string Procedural = "Procedural";

    private readonly IReadOnlyList<GripPreset> _presets;
    private readonly string _right, _left;
    private readonly ProceduralGripGenerator _procedural = new();

    public ChoiceGripGenerator(IEnumerable<GripPreset> presets, string? right, string? left)
    {
        _presets = presets.ToList();
        _right = right ?? "";
        _left = left ?? "";
    }

    public string Name => "Grips";
    public bool IsAvailable => true;

    public IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default)
    {
        var choice = request.Hand.Side == Side.Right ? _right : _left;
        if (choice == Procedural)
            return _procedural.Generate(request, cancel);
        if (choice.Length > 0)
        {
            var chosen = new PresetGripGenerator(_presets.Where(p => p.Name == choice)).Generate(request, cancel).ToList();
            if (chosen.Count > 0)
                return chosen;
        }
        return new CompositeGripGenerator(new PresetGripGenerator(_presets), _procedural).Generate(request, cancel);
    }

    /// <summary>Only the library grips this hand may use (none when the procedural fit is chosen).</summary>
    public IEnumerable<GraspCandidate> GenerateLibrary(GraspRequest request, CancellationToken cancel = default)
    {
        var choice = request.Hand.Side == Side.Right ? _right : _left;
        if (choice == Procedural)
            return Array.Empty<GraspCandidate>();
        var presets = choice.Length > 0 ? _presets.Where(p => p.Name == choice) : _presets;
        return new PresetGripGenerator(presets).Generate(request, cancel);
    }
}
