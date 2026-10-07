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
/// Grips from the library (captured from animations): each preset for the requested hand is
/// placed on the grip surface, refined against the geometry for this hand's size, and scored.
/// </summary>
public sealed class PresetGripGenerator : IGripGenerator
{
    private readonly IReadOnlyList<GripPreset> _presets;

    /// <summary>Per hand: only this preset is offered (the user picked it).</summary>
    public string? OnlyRight { get; init; }
    public string? OnlyLeft { get; init; }

    public PresetGripGenerator(IEnumerable<GripPreset> presets) => _presets = presets.ToList();

    public string Name => "Library";
    public bool IsAvailable => _presets.Count > 0;

    public IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default)
    {
        var list = new List<GraspCandidate>();
        foreach (var preset in _presets)
        {
            cancel.ThrowIfCancellationRequested();
            if (preset.Side != request.Hand.Side || !Compatible(preset.Style, request.Style))
                continue;
            var only = request.Hand.Side == Side.Right ? OnlyRight : OnlyLeft;
            if (!string.IsNullOrEmpty(only) && preset.Name != only)
                continue;
            var pose = GripLibrary.Apply(preset, request.Hand, request.Surface);
            ContactRefiner.Refine(request, pose);
            var quality = GraspEvaluator.Evaluate(request, pose);
            list.Add(new GraspCandidate(pose, quality, preset.Name) { Bonus = preset.Priority });
        }
        return list.OrderByDescending(c => c.Rank);
    }

    /// <summary>A handguard grip can cradle a pump and vice versa; a pistol grip grip is a wrap.</summary>
    public static bool Compatible(GripStyle preset, GripStyle surface) => preset == surface
        || (preset, surface) is (GripStyle.Cradle, GripStyle.Pump) or (GripStyle.Pump, GripStyle.Cradle)
        || (preset, surface) is (GripStyle.Wrap, GripStyle.Handle) or (GripStyle.Handle, GripStyle.Wrap);
}
