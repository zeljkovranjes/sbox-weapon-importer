#nullable enable annotations

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Grasp;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>Learned grasps as a grip source of their own (the Grips list offers them per hand too).</summary>
public sealed class GrabNetGripGenerator : IGripGenerator
{
    private readonly PresetGripGenerator _presets;
    private readonly int _count;

    public GrabNetGripGenerator(IEnumerable<GripPreset> presets)
    {
        var list = presets.ToList();
        _count = list.Count;
        _presets = new PresetGripGenerator(list);
    }

    public string Name => GrabNetGrips.Source;
    public bool IsAvailable => _count > 0;

    public IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default) => _presets.Generate(request, cancel);
}
