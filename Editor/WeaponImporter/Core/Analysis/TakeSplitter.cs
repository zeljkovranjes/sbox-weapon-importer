#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>One action found inside a longer take: frames [Start, End], inclusive.</summary>
public sealed record TakePart(int Start, int End, bool Rest)
{
    public int Frames => End - Start + 1;
}

/// <summary>
/// Many first-person packs export every action on one timeline ("CINEMA_4D_Main", "Scene",
/// "Take 001"). The actions are found from the motion itself: exporters join them with a pose
/// jump (the hands teleport from the end of one action to the start of the next) or a held,
/// duplicated frame, and animators leave the hands at rest between actions. Each part becomes
/// its own clip; the take itself stays available.
/// </summary>
public static class TakeSplitter
{
    /// <summary>Shortest action kept on its own (shorter pieces join a neighbour).</summary>
    public const int MinPartFrames = 6;

    /// <summary>Shortest still stretch that counts as the hands resting between actions (seconds).</summary>
    public const float MinRestSeconds = 0.4f;

    /// <summary>A take worth splitting: long, one of few, and not named after one action.</summary>
    public static bool Candidate(WeaponAsset asset, Clip clip)
    {
        if (clip.FrameCount < 60)
            return false;
        // Packs with several named clips already split their actions.
        var named = asset.Clips.Count(c => AnimationClassifier.Classify(c.Name).Role != AnimationRole.Unknown);
        if (asset.Clips.Count > 3 && named >= 2)
            return false;
        var role = AnimationClassifier.Classify(clip.Name).Role;
        return role is AnimationRole.Unknown or AnimationRole.Idle || clip.Duration > 8f;
    }

    /// <summary>The actions in a take, in order; one part covering everything when it can't be split.</summary>
    public static List<TakePart> Parts(Skeleton skeleton, Clip clip)
    {
        var n = clip.FrameCount;
        if (n < 2)
            return new List<TakePart> { new(0, Math.Max(0, n - 1), false) };

        // Mean distance every bone travels per frame (model space, inches).
        var worlds = new XForm[n][];
        for (var f = 0; f < n; f++)
            worlds[f] = new Pose(clip.Frames[f]).ToWorld(skeleton);
        var jump = new float[n];
        for (var f = 1; f < n; f++)
        {
            var sum = 0f;
            for (var b = 0; b < skeleton.Count; b++)
                sum += Vector3.Distance(worlds[f][b].Pos, worlds[f - 1][b].Pos);
            jump[f] = sum / skeleton.Count;
        }

        var cuts = new SortedSet<int>();
        // Joins: a pose jump far above both neighbours, or a held duplicate frame between motion.
        for (var f = 1; f < n; f++)
        {
            var prev = f > 1 ? jump[f - 1] : 0f;
            var next = f + 1 < n ? jump[f + 1] : 0f;
            if (jump[f] > 0.25f && jump[f] > 4f * MathF.Max(prev, next))
                cuts.Add(f);
            else if (jump[f] < 1e-4f && (prev > 0.02f || next > 0.02f))
                cuts.Add(f);
        }

        // Rests: stretches much slower than the take's busy motion.
        var sorted = jump.Skip(1).OrderBy(x => x).ToArray();
        var busy = sorted[(int)(sorted.Length * 0.9f)];
        var still = MathF.Max(0.004f, busy * 0.12f);
        var minRest = Math.Max(3, (int)MathF.Round(MinRestSeconds * clip.Fps));
        var rests = new List<(int Start, int End)>();
        for (var f = 1; f < n;)
        {
            if (jump[f] >= still)
            {
                f++;
                continue;
            }
            var s = f;
            while (f < n && jump[f] < still)
                f++;
            if (f - s >= minRest)
                rests.Add((s, f - 1));
        }
        foreach (var (s, e) in rests)
        {
            cuts.Add(s);
            if (e + 1 < n)
                cuts.Add(e + 1);
        }

        // Parts between cuts; tiny ones join the previous part.
        var bounds = new List<int> { 0 };
        bounds.AddRange(cuts.Where(c => c > 0 && c < n));
        bounds.Add(n);
        var parts = new List<TakePart>();
        for (var i = 0; i + 1 < bounds.Count; i++)
        {
            var start = bounds[i];
            var end = bounds[i + 1] - 1;
            if (end < start)
                continue;
            var rest = rests.Any(r => r.Start <= start && end <= r.End);
            if (parts.Count > 0 && end - start + 1 < MinPartFrames)
            {
                var last = parts[^1];
                parts[^1] = last with { End = end };
                continue;
            }
            if (parts.Count > 0 && parts[^1].Frames < MinPartFrames)
            {
                var last = parts[^1];
                parts[^1] = new TakePart(last.Start, end, rest);
                continue;
            }
            parts.Add(new TakePart(start, end, rest));
        }
        return parts.Count == 0 ? new List<TakePart> { new(0, n - 1, false) } : parts;
    }

    /// <summary>Parts from explicit start frames (the user's splits): each part runs to the next start.</summary>
    public static List<TakePart> FromStarts(Clip clip, IEnumerable<int> starts)
    {
        var n = clip.FrameCount;
        var list = starts.Where(s => s > 0 && s < n).Distinct().OrderBy(s => s).ToList();
        list.Insert(0, 0);
        var parts = new List<TakePart>();
        for (var i = 0; i < list.Count; i++)
            parts.Add(new TakePart(list[i], (i + 1 < list.Count ? list[i + 1] : n) - 1, false));
        return parts;
    }

    /// <summary>Name of the still clip made from a take's first frame ("Scene start pose").</summary>
    public static string StartPoseName(string take) => $"{take} start pose";

    /// <summary>
    /// A one-second still of the take's first frame: an item's use starts from the pose it is
    /// held in, which is its idle when the file has no idle of its own.
    /// </summary>
    public static Clip StartPose(Clip take)
    {
        var frame = take.Frames[0];
        var frames = Enumerable.Range(0, Math.Max(2, (int)MathF.Round(take.Fps))).Select(_ => frame.ToArray()).ToList();
        return new Clip(StartPoseName(take.Name), take.Fps, true, frames, take.NativeFps);
    }

    /// <summary>Name of a part's clip: "CINEMA_4D_Main 3".</summary>
    public static string PartName(string take, int index) => $"{take} {index + 1}";

    /// <summary>The asset with a clip per part of each split take (the takes themselves are kept).</summary>
    public static WeaponAsset Apply(WeaponAsset asset, IReadOnlyDictionary<string, List<TakePart>> splits)
    {
        if (splits.Count == 0)
            return asset;
        var clips = asset.Clips.ToList();
        var names = new HashSet<string>(clips.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        // Every split take also offers its first frame as a still pose.
        foreach (var take in asset.Clips.Where(c => splits.ContainsKey(c.Name) && c.FrameCount > 0))
            if (names.Add(StartPoseName(take.Name)))
                clips.Add(StartPose(take));
        foreach (var take in asset.Clips)
        {
            if (!splits.TryGetValue(take.Name, out var parts) || parts.Count < 2)
                continue;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var name = PartName(take.Name, i);
                if (!names.Add(name) || part.Start < 0 || part.End >= take.FrameCount || part.End < part.Start)
                    continue;
                var frames = take.Frames.GetRange(part.Start, part.Frames).Select(f => f.ToArray()).ToList();
                // A rest loops; an action plays once.
                clips.Add(new Clip(name, take.Fps, part.Rest, frames, take.NativeFps));
                added++;
            }
        }
        if (added == 0)
            return asset;
        var copy = new WeaponAsset { CameraViews = asset.CameraViews, Name = asset.Name, Kind = asset.Kind, SourcePath = asset.SourcePath, Skeleton = asset.Skeleton, Mesh = asset.Mesh, Clips = clips, Attachments = asset.Attachments };
        copy.Notes.AddRange(asset.Notes);
        copy.Notes.Add($"Split {splits.Count(s => s.Value.Count >= 2)} take{(splits.Count(s => s.Value.Count >= 2) == 1 ? "" : "s")} into {added} actions.");
        return copy;
    }
}
