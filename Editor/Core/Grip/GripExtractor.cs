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
