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

    /// <summary>
    /// The rig's camera bone ("Camera", "Head_Cam", "cam_01"...: a "camera" or whole-word "cam"
    /// in its name, end bones skipped), or -1.
    /// </summary>
    /// <summary>A bone named as a camera ("Camera_007", "cam", "FP_Cam"), not a bone-chain end.</summary>
    public static bool IsCameraName(string name)
        => !name.EndsWith("_end", StringComparison.OrdinalIgnoreCase)
           && (name.Contains("camera", StringComparison.OrdinalIgnoreCase) || WordsOf(name).Contains("cam"));

    public static int CameraBone(Skeleton skeleton)
    {
        var fallback = -1;
        for (var i = 0; i < skeleton.Count; i++)
        {
            var name = skeleton[i].Name;
            if (name.EndsWith("_end", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Contains("camera", StringComparison.OrdinalIgnoreCase))
                return i;
            if (fallback < 0 && WordsOf(name).Contains("cam"))
                fallback = i;
        }
        return fallback;
    }

    /// <summary>
    /// The character's eyes in a full-body file (midpoint of its eye bones, else its head bone),
    /// or null. A first-person view of such a file is what the character itself sees.
    /// </summary>
    public static Vector3? EyePoint(Skeleton skeleton, IReadOnlyList<Maths.XForm> world)
    {
        var eyes = new List<Vector3>();
        var head = -1;
        for (var i = 0; i < skeleton.Count; i++)
        {
            var name = skeleton[i].Name;
            if (name.EndsWith("_end", StringComparison.OrdinalIgnoreCase))
                continue;
            var words = WordsOf(name);
            if (words.Contains("eye") && !words.Contains("lid") && !words.Contains("brow"))
                eyes.Add(world[i].Pos);
            else if (head < 0 && words.Contains("head") && !words.Contains("cam"))
                head = i;
        }
        // Rigs also carry eye aim targets out in front of the face: only eyes near the head count.
        if (head >= 0)
            eyes = eyes.Where(e => Vector3.Distance(e, world[head].Pos) < 8f).ToList();
        if (eyes.Count >= 2)
            return eyes.Aggregate(Vector3.Zero, (a, b) => a + b) / eyes.Count;
        return head >= 0 ? world[head].Pos : null;
    }

    private static readonly HashSet<string> TorsoTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "spine", "neck", "head", "pelvis", "hip", "hips", "chest", "torso", "belly", "face", "jaw",
    };

    /// <summary>A bone of a character's torso or head (not arms or hands).</summary>
    private static bool IsTorsoOrHead(Skeleton skeleton, int bone)
        => bone >= 0 && bone < skeleton.Count && WordsOf(skeleton[bone].Name).Any(TorsoTokens.Contains);

    private static List<string> WordsOf(string name)
    {
        var leaf = name[(name.LastIndexOfAny(new[] { ':', '|', '/' }) + 1)..];
        return Words.Matches(leaf).Select(m => m.Value.ToLowerInvariant()).ToList();
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
        // A full character (geometry on its torso or head): keep the viewmodel part (weapon,
        // forearms and hands) so the body doesn't block the view. Arms-only rigs keep everything.
        if (byBone.Keys.Any(b => IsTorsoOrHead(skeleton, b)) && byBone.Keys.Any(b => !IsBodyBone(skeleton, b)))
            foreach (var body in byBone.Keys.Where(b => IsBodyBone(skeleton, b)).ToList())
                byBone.Remove(body);
        return byBone;
    }
}
