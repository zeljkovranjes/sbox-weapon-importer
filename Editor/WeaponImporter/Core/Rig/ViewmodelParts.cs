#nullable enable annotations

using System.Numerics;
using System.Text.RegularExpressions;
using WeaponImporter.Core.Analysis;

namespace WeaponImporter.Core.Rig;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// What a first-person view of an imported file shows: the weapon, forearms and hands. Scene
/// dressing (a floor or backdrop plane) and, in files without a rig camera, the rest of a full
/// character are left out. Shared by the editor's first-person preview and the viewmodel export.
/// </summary>
public static class ViewmodelParts
{
    private static readonly HashSet<string> BodyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "spine", "neck", "head", "pelvis", "hip", "hips", "thigh", "leg", "shin", "calf", "knee", "foot",
        "toe", "toes", "heel", "eye", "eyes", "jaw", "teeth", "tongue", "breast", "chest", "torso",
        "shoulder", "clavicle", "upperarm", "forehead", "brow", "lid", "ear", "nose", "cheek", "lip",
        "lips", "chin", "hair", "face", "belly", "butt", "root",
    };

    private static readonly Regex Words = new("[A-Z]?[a-z]+|[A-Z]+(?![a-z])", RegexOptions.Compiled);

    /// <summary>
    /// Bones of a character's body other than the forearms and hands, by whole name tokens
    /// ("DEF-spine.003", "mixamorig:LeftUpLeg", "upper_arm.L"), so weapon parts such as "Slide" or
    /// "RearSight" never match.
    /// </summary>
    public static bool IsBodyBone(Skeleton skeleton, int bone)
    {
        if (skeleton.Count == 0 || bone < 0 || bone >= skeleton.Count)
            return false;
        var name = skeleton[bone].Name;
        var leaf = name[(name.LastIndexOfAny(new[] { ':', '|', '/' }) + 1)..];
        var words = Words.Matches(leaf).Select(m => m.Value.ToLowerInvariant()).ToList();
        if (words.Count == 0)
            return false;
        // A lone "root" is usually the weapon's own root; only a character's root counts.
        if (words.Count == 1 && words[0] == "root")
            return false;
        for (var i = 0; i < words.Count; i++)
        {
            if (BodyTokens.Contains(words[i]) && words[i] != "root")
                return true;
            if (i + 1 < words.Count && (words[i] + words[i + 1] is "upperarm" or "armupper" or "upleg"))
                return true;
        }
        return false;
    }

    /// <summary>The rig's camera bone (first bone named like a camera), or -1.</summary>
    public static int CameraBone(Skeleton skeleton)
    {
        for (var i = 0; i < skeleton.Count; i++)
            if (skeleton[i].Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Triangles a first-person view shows, each with its dominant bone. <paramref name="step"/>
    /// thins out very heavy meshes for the preview (1 keeps every triangle).
    /// </summary>
    public static Dictionary<int, List<int>> TrianglesByBone(WeaponAnalysis analysis, int step = 1)
    {
        var mesh = analysis.Asset.Mesh;
        var skeleton = analysis.Asset.Skeleton;
        var byBone = new Dictionary<int, List<int>>();
        // Scene dressing is a few huge triangles; nothing on the weapon or the arms is near that size.
        var maxEdge = MathF.Max(40f, 2f * analysis.WeaponBounds.Size.X);
        for (var t = 0; t < mesh.TriangleCount; t += Math.Max(1, step))
        {
            var (a, b, c) = mesh.Triangle(t);
            if (MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a))) > maxEdge)
                continue;
            var bone = Math.Clamp(mesh.TriangleBone(t), 0, Math.Max(0, skeleton.Count - 1));
            if (!byBone.TryGetValue(bone, out var list))
                byBone[bone] = list = new List<int>();
            list.Add(t);
        }
        // Without a rig camera the file is a full character: keep the viewmodel part (weapon,
        // forearms and hands) so the body doesn't block the view.
        if (CameraBone(skeleton) < 0 && byBone.Keys.Any(b => !IsBodyBone(skeleton, b)))
            foreach (var body in byBone.Keys.Where(b => IsBodyBone(skeleton, b)).ToList())
                byBone.Remove(body);
        return byBone;
    }
}
