#nullable enable annotations

using WeaponImporter.Core.Formats.Fbx;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Import;

/// <summary>
/// Animations shipped as separate files: many packs export the rigged mesh once and every
/// action as its own animation-only FBX ("Fbx/animations/RIG_Fire.fbx"). Such files beside the
/// weapon (same folder or a subfolder) whose skeleton is the weapon's own are read and their
/// clips added to the weapon, mapped onto its skeleton by bone name.
/// </summary>
public static class AnimationFiles
{
    public const int MaxFiles = 64;
    public const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>Share of an animation file's bones the weapon must have for the file to belong to it.</summary>
    public const float MinBoneMatch = 0.9f;

    /// <summary>Other FBX files in the weapon's folder and up to two subfolder levels below it.</summary>
    public static IEnumerable<string> Candidates(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return Array.Empty<string>();
        var self = Path.GetFullPath(path);
        IEnumerable<string> Files(string folder, int depth)
        {
            IEnumerable<string> here;
            try
            {
                here = Directory.EnumerateFiles(folder, "*.fbx").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                yield break;
            }
            foreach (var f in here)
                yield return f;
            if (depth == 0)
                yield break;
            List<string> subs;
            try
            {
                subs = Directory.EnumerateDirectories(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                yield break;
            }
            foreach (var sub in subs)
                foreach (var f in Files(sub, depth - 1))
                    yield return f;
        }
        var stem = Path.GetFileNameWithoutExtension(path);
        return Files(dir, 2)
            .Where(f => !string.Equals(Path.GetFullPath(f), self, StringComparison.OrdinalIgnoreCase) && LooksLikeAnimationOf(stem, dir, f))
            .Take(MaxFiles * 4);
    }

    /// <summary>
    /// Only files named or placed like this weapon's animations are opened (a pack folder can hold
    /// dozens of other large weapons): "Rifle_Fire.fbx" or "Rifle@Fire.fbx" beside "Rifle.fbx", or
    /// anything under a folder with "anim" in its name.
    /// </summary>
    public static bool LooksLikeAnimationOf(string weaponStem, string weaponDir, string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (name.StartsWith(weaponStem, StringComparison.OrdinalIgnoreCase) || name.Contains('@') || name.Contains("anim", StringComparison.OrdinalIgnoreCase))
            return true;
        var folder = Path.GetRelativePath(weaponDir, Path.GetDirectoryName(file) ?? weaponDir);
        return folder != "." && folder.Contains("anim", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The weapon with the clips of every matching animation-only file next to it added. Files
    /// with geometry (other weapons, other exports of this one) and files whose skeleton isn't
    /// this weapon's are left alone.
    /// </summary>
    public static WeaponAsset Merge(WeaponAsset asset, string path)
    {
        if (asset.Kind != SourceKind.Fbx || asset.Skeleton.Count < 2 || string.IsNullOrEmpty(path))
            return asset;
        var clips = asset.Clips.ToList();
        var names = new HashSet<string>(clips.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var stem = Path.GetFileNameWithoutExtension(path);
        var used = new List<string>();
        var read = 0;
        foreach (var file in Candidates(path))
        {
            if (read >= MaxFiles)
                break;
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes)
                    continue;
                var data = File.ReadAllBytes(file);
                var added = Read(data, asset.Skeleton, ClipName(stem, Path.GetFileNameWithoutExtension(file)));
                if (added is null)
                    continue;
                read++;
                var any = false;
                foreach (var clip in added)
                {
                    if (!names.Add(clip.Name))
                        continue;
                    clips.Add(clip);
                    any = true;
                }
                if (any)
                    used.Add(Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(path))!, file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or EndOfStreamException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException or ArithmeticException)
            {
                // Not an animation of this weapon (or unreadable): skip it.
            }
        }
        if (used.Count == 0)
            return asset;
        var copy = new WeaponAsset { Name = asset.Name, Kind = asset.Kind, SourcePath = asset.SourcePath, Skeleton = asset.Skeleton, Mesh = asset.Mesh, Clips = clips, Attachments = asset.Attachments };
        copy.Notes.AddRange(asset.Notes);
        var folders = used.Select(u => Path.GetDirectoryName(u) is { Length: > 0 } d ? d + Path.DirectorySeparatorChar : "").Distinct().ToList();
        copy.Notes.Add($"{clips.Count - asset.Clips.Count} animations loaded from {used.Count} separate file{(used.Count == 1 ? "" : "s")}{(folders.Count == 1 && folders[0].Length > 0 ? $" in {folders[0]}" : "")}.");
        return copy;
    }

    /// <summary>
    /// Clips of an animation-only FBX on <paramref name="target"/>, or null when the file has
    /// geometry or its skeleton isn't the target's. One take is named <paramref name="name"/>;
    /// several are named after their takes.
    /// </summary>
    public static List<Clip>? Read(byte[] data, Skeleton target, string name)
    {
        var scene = FbxScene.Build(FbxTokenizer.Parse(data));
        if (scene.ObjectsById.Values.Any(o => o.NodeType == "Geometry" && o.SubClass == "Mesh"))
            return null;
        if (scene.Stacks.Count == 0)
            return null;
        var imported = FbxImporter.Import(data, new FbxImportOptions { SampleFps = 30f });
        if (imported.Clips.Count == 0)
            return null;
        var conversion = SpaceConversion.For(imported);
        var skeleton = conversion.Skeleton(imported.Skeleton);
        var clips = conversion.Clips(imported.Clips, imported.Skeleton, skeleton);

        // Same skeleton: nearly every bone of the file is one of the weapon's.
        var map = new int[target.Count];
        var shared = 0;
        for (var i = 0; i < target.Count; i++)
        {
            map[i] = skeleton.IndexOf(target[i].Name);
            if (map[i] >= 0)
                shared++;
        }
        if (shared < Math.Max(2, MinBoneMatch * skeleton.Count))
            return null;

        // Exported with other bone axes than the model (its never-moving armature root turned by
        // 90/180 degrees): re-express every bone in the model's axes, keeping how it moves in the
        // world. The file's first frame is the pose it was authored from (the model's rest).
        var convert = ConventionDiffers(target, skeleton, clips, map);
        XForm[]? toModel = null;
        if (convert && clips.FirstOrDefault(c => c.FrameCount > 0) is { } first)
        {
            var refWorld = new Pose(first.Frames[0]).ToWorld(skeleton);
            var restWorld = target.RestWorld;
            toModel = new XForm[target.Count];
            for (var i = 0; i < target.Count; i++)
                toModel[i] = map[i] >= 0 ? XForm.Compose(refWorld[map[i]].Inverse(), restWorld[i]) : XForm.Identity;
        }

        var result = new List<Clip>(clips.Count);
        foreach (var clip in clips)
        {
            if (clip.FrameCount == 0)
                continue;
            var frames = new List<XForm[]>(clip.FrameCount);
            foreach (var frame in clip.Frames)
            {
                var locals = new XForm[target.Count];
                if (toModel is not null)
                {
                    var world = new Pose(frame).ToWorld(skeleton);
                    var modelWorld = new XForm[target.Count];
                    for (var i = 0; i < target.Count; i++)
                    {
                        var parent = target[i].ParentIndex;
                        modelWorld[i] = map[i] >= 0 ? XForm.Compose(world[map[i]], toModel[i])
                            : parent >= 0 ? XForm.Compose(modelWorld[parent], target[i].RestLocal) : target[i].RestLocal;
                        locals[i] = parent >= 0 ? XForm.Compose(modelWorld[parent].Inverse(), modelWorld[i]) : modelWorld[i];
                    }
                }
                else
                {
                    for (var i = 0; i < target.Count; i++)
                        locals[i] = map[i] >= 0 && map[i] < frame.Length ? frame[map[i]] : target[i].RestLocal;
                }
                frames.Add(locals);
            }
            var clipName = clips.Count == 1 ? name : $"{name}_{TakeName(clip.Name)}";
            result.Add(new Clip(clipName, clip.Fps, clip.Looping, frames, clip.NativeFps));
        }
        return result;
    }

    /// <summary>
    /// The file's armature root never moves yet sits turned against the model's (by more than
    /// 60 degrees): the file was exported with other bone axes, not posed differently.
    /// </summary>
    private static bool ConventionDiffers(Skeleton target, Skeleton file, List<Clip> clips, int[] map)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (target[i].ParentIndex >= 0 || map[i] < 0)
                continue;
            var j = map[i];
            if (Angle(target[i].RestLocal.Rot, file[j].RestLocal.Rot) < 60f)
                continue;
            var still = clips.All(c => c.Frames.All(f => j < f.Length && Angle(f[j].Rot, file[j].RestLocal.Rot) < 1f && System.Numerics.Vector3.Distance(f[j].Pos, file[j].RestLocal.Pos) < 0.05f));
            if (still)
                return true;
        }
        return false;
    }

    private static float Angle(System.Numerics.Quaternion a, System.Numerics.Quaternion b)
        => 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(System.Numerics.Quaternion.Dot(a, b)))) * 57.29578f;

    /// <summary>"RIG_Comando_Fire" beside "RIG_Comando" is "Fire"; unrelated names stay whole.</summary>
    public static string ClipName(string weaponStem, string fileStem)
    {
        var n = 0;
        while (n < weaponStem.Length && n < fileStem.Length && char.ToLowerInvariant(weaponStem[n]) == char.ToLowerInvariant(fileStem[n]))
            n++;
        // Cut at a separator so "Rifle_Fire" beside "Rifle" gives "Fire", not "_Fire" or "ire".
        while (n > 0 && !IsSeparator(fileStem[n - 1]) && !(n == weaponStem.Length && n < fileStem.Length && IsSeparator(fileStem[n])))
            n--;
        var rest = fileStem[n..].TrimStart('_', '-', ' ', '.', '@');
        return rest.Length > 0 ? rest : fileStem;
    }

    private static bool IsSeparator(char c) => c is '_' or '-' or ' ' or '.' or '@';

    /// <summary>"Armature|Fire" -> "Fire".</summary>
    private static string TakeName(string take)
    {
        var bar = take.LastIndexOf('|');
        return bar >= 0 && bar + 1 < take.Length ? take[(bar + 1)..] : take;
    }
}
