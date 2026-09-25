#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Grasp;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Grip;

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

/// <summary>Captures the grips of a file's own arms (first-person rigs, animation packs).</summary>
public static class GripExtractor
{
    /// <summary>A hand counts as holding when its palm is this close to the weapon surface (inches).</summary>
    public const float HoldingDistance = 2.5f;

    /// <summary>Ranking bonus of grips taken from the weapon's own animation (see <see cref="GripPreset.Priority"/>).</summary>
    public const float OwnPriority = 2.5f;

    /// <summary>The weapon's own grips, preferred when grips compete.</summary>
    public static List<GripPreset> ExtractOwn(WeaponAnalysis a, string source)
    {
        var list = Extract(a, source);
        foreach (var p in list)
            p.Priority = OwnPriority;
        return list;
    }

    /// <summary>
    /// The hands in the analysed file's reference pose (canonical weapon space), captured on the
    /// grip surface each one holds. Hands that aren't on the weapon are skipped.
    /// </summary>
    public static List<GripPreset> Extract(WeaponAnalysis a, string source)
    {
        var list = new List<GripPreset>();
        var skeleton = a.Asset.Skeleton;
        if (skeleton.Count == 0 || a.WeaponBvh.IsEmpty)
            return list;
        var world = ReferenceWorld(a);
        foreach (var side in new[] { Side.Right, Side.Left })
        {
            var rig = HandRig.Build(skeleton, side);
            if (rig is null || rig.Fingers.Count < 3)
                continue;
            var palm = world[rig.Hand].TransformPoint(rig.PalmCenter);
            var candidate = side == Side.Right ? a.Primary : a.Support;
            GripSurface? surface = null;
            var style = side == Side.Right ? (a.Type.Type == WeaponType.Melee ? GripStyle.Handle : GripStyle.Wrap) : GripStyle.Cradle;
            if (candidate is not null && Vector3.Distance(candidate.Surface.Contact, palm) < rig.HandLength)
            {
                surface = candidate.Surface;
                style = candidate.Style;
            }
            else
            {
                var hint = side == Side.Right ? Vector3.UnitZ : Vector3.UnitX;
                surface = SurfaceProbe.FromPoint(a.WeaponBvh, palm, hint);
            }
            if (surface is null || Vector3.Distance(surface.Contact, palm) > HoldingDistance + rig.PalmThickness)
                continue;
            var name = $"{source} · {(side == Side.Right ? "right" : "left")}";
            var preset = GripLibrary.Capture(rig, world, surface, style, name, source);
            // A palm inside the surface means the "weapon" is the arms themselves (an arms-only
            // file) or the hand isn't really holding it.
            if (preset.Palm[2] < -0.12f)
                continue;
            preset.WeaponType = a.Type.Type.ToString();
            list.Add(preset);
        }
        return list;
    }

    /// <summary>World transforms of the analysis reference frame (bind pose without a clip).</summary>
    public static XForm[] ReferenceWorld(WeaponAnalysis a)
    {
        var skeleton = a.Asset.Skeleton;
        var clip = a.Asset.FindClip(a.ReferenceClip);
        if (clip is { FrameCount: > 0 })
        {
            var frame = Math.Clamp(a.ReferenceFrame, 0, clip.FrameCount - 1);
            return new Pose(clip.Frames[frame]).ToWorld(skeleton);
        }
        return skeleton.RestWorld.ToArray();
    }
}
