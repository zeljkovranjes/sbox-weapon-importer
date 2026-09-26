#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Import;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>How the weapon was placed in separate first-person arms.</summary>
public sealed record ArmsFit(string ArmsPath, string AttachBone, float RightError, float LeftError, float Shift, int Clips, string Reason);

/// <summary>
/// First-person arms shipped separately from the weapon: one arms model for a whole pack, with
/// per-weapon arm animations ("FP_Arms_Pistol_01_Fire.fbx") and the weapon's own animations
/// ("Pistol_01_Fire.fbx"). The weapon is put in the arms' item bone (the pack's own setup),
/// checked against where the palms are, and each arm animation is paired with the weapon's
/// animation of the same action, giving one asset with arms, like a file that has them built in.
/// </summary>
public static class FirstPersonArms
{
    private const int MaxDepth = 5;
    private const int MaxFilesScanned = 4000;
    private static readonly string[] ArmWords = { "arm", "arms", "hand", "hands", "viewmodel", "fparms", "fpsarms", "fparm" };
    private static readonly string[] ItemBones = { "hand_item_r", "item_r", "weapon", "weapon_r", "gun", "gun_r", "prop_r", "r_prop", "ik_hand_gun", "hand_gun", "attach_r", "weapon_bone", "wpn" };

    /// <summary>
    /// The pack's arms model for this weapon, or null: an arms-only model near the weapon (up to
    /// four folders up) whose own folder holds animations named after the weapon.
    /// </summary>
    public static string? Find(string weaponPath)
    {
        var stem = Path.GetFileNameWithoutExtension(weaponPath);
        if (stem.Length < 3)
            return null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(weaponPath));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var up = 0; up < 4 && !string.IsNullOrEmpty(dir) && !IsBroadFolder(dir); up++, dir = Path.GetDirectoryName(dir))
        {
            foreach (var candidate in Files(dir, MaxDepth, "*.fbx").Take(MaxFilesScanned))
            {
                if (!seen.Add(candidate) || string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(weaponPath), StringComparison.OrdinalIgnoreCase))
                    continue;
                var tokens = NameTokens.Split(Path.GetFileNameWithoutExtension(candidate));
                if (!NameTokens.Has(tokens, ArmWords) && !ArmWords.Any(w => NameTokens.Joined(tokens).Contains(w, StringComparison.Ordinal)))
                    continue;
                if (NameTokens.Joined(tokens).Contains(NameTokens.Joined(NameTokens.Split(stem)), StringComparison.Ordinal))
                    continue; // an animation of the arms, not the arms model
                if (AnimationsFor(candidate, stem).Any())
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>Animation files of the arms that belong to this weapon ("FP_Arms_Pistol_01_Fire").</summary>
    public static IEnumerable<string> AnimationsFor(string armsPath, string weaponStem)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(armsPath));
        if (string.IsNullOrEmpty(dir))
            yield break;
        // Named after both: "FP_Arms_Pistol_01_Fire" for "FP_Arms" holding "Pistol_01".
        var key = NameTokens.Joined(NameTokens.Split(weaponStem));
        var arms = NameTokens.Joined(NameTokens.Split(Path.GetFileNameWithoutExtension(armsPath)));
        foreach (var f in Files(dir, MaxDepth, "*.fbx").Take(MaxFilesScanned))
        {
            if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(armsPath), StringComparison.OrdinalIgnoreCase))
                continue;
            var name = NameTokens.Joined(NameTokens.Split(Path.GetFileNameWithoutExtension(f)));
            if (name.Contains(key, StringComparison.Ordinal) && name.Contains(arms, StringComparison.Ordinal))
                yield return f;
        }
    }

    /// <summary>
    /// The weapon held by the arms: one asset with the arms' skeleton and mesh, the weapon under
    /// the arms' item bone, and a clip per arm animation with the weapon's matching clip on it.
    /// Null when the arms have no animations for this weapon or can't hold it.
    /// </summary>
    public static WeaponAsset? Combine(WeaponAsset weapon, string weaponPath, string armsPath, out ArmsFit? fit, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        fit = null;
        var arms = WeaponLoader.Load(armsPath, withAnimationFiles: false);
        var sk = arms.Skeleton;
        var right = HandRig.Build(sk, Side.Right);
        if (right is null)
            return null;
        var left = HandRig.Build(sk, Side.Left);
        var stem = Path.GetFileNameWithoutExtension(weaponPath);
        var armsStem = Path.GetFileNameWithoutExtension(armsPath);

        // The arm animations for this weapon, named by action ("Fire", "Breathing_Aiming").
        var armClips = new List<Clip>();
        foreach (var file in AnimationsFor(armsPath, stem))
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report($"Reading arm animation {Path.GetFileName(file)}");
            try
            {
                var name = ActionName(Path.GetFileNameWithoutExtension(file), armsStem, stem);
                if (AnimationFiles.Read(File.ReadAllBytes(file), sk, name) is { } clips)
                    armClips.AddRange(clips.Where(c => armClips.All(a => !string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase))));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or EndOfStreamException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException or ArithmeticException)
            {
            }
        }
        if (armClips.Count == 0)
            return null;

        var attach = AttachBone(sk, right);
        var reference = armClips.FirstOrDefault(c => AnimationClassifier.Classify(c.Name).Role == AnimationRole.Idle) ?? armClips[0];
        var analysis = WeaponAnalyzer.Analyze(weapon);
        var place = Place(arms, reference, attach, right, left, analysis, out var rightError, out var leftError, out var shift, out var reason);

        var combined = Build(arms, weapon, attach, place, armClips);
        combined.Notes.AddRange(arms.Notes.Select(n => $"Arms: {n}"));
        combined.Notes.Add($"First-person arms from {Path.GetFileName(armsPath)}: the weapon is held in '{sk[attach].Name}' ({reason}); {armClips.Count} arm animations.");
        fit = new ArmsFit(armsPath, sk[attach].Name, rightError, leftError, shift, armClips.Count, reason);
        return combined;
    }

    /// <summary>"FP_Arms_Pistol_01_Fire_Aiming" beside "FP_Arms" for "Pistol_01" is "Fire_Aiming".</summary>
    public static string ActionName(string fileStem, string armsStem, string weaponStem)
    {
        var name = AnimationFiles.ClipName(armsStem, fileStem);
        var i = name.IndexOf(weaponStem, StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
            name = (name[..i] + name[(i + weaponStem.Length)..]).Trim('_', '-', ' ', '.', '@');
        return name.Length > 0 ? name : fileStem;
    }

    /// <summary>The bone the pack parents weapons to, else the right hand.</summary>
    private static int AttachBone(Skeleton sk, HandRig right)
    {
        foreach (var name in ItemBones)
            for (var b = 0; b < sk.Count; b++)
                if (string.Equals(sk[b].Name, name, StringComparison.OrdinalIgnoreCase))
                    return b;
        // Any "item"/"weapon" bone under the right hand.
        for (var b = 0; b < sk.Count; b++)
        {
            var t = NameTokens.Split(sk[b].Name);
            if (NameTokens.Has(t, "item", "weapon", "gun", "prop") && Descends(sk, b, right.Hand))
                return b;
        }
        return right.Hand;
    }

    private static bool Descends(Skeleton sk, int bone, int ancestor)
    {
        for (var b = bone; b >= 0; b = sk[b].ParentIndex)
            if (b == ancestor)
                return true;
        return false;
    }

    /// <summary>
    /// Weapon model space in the attach bone's space. The pack's own setup is the weapon's origin
    /// on the attach bone; when both palms miss their grips the same way (the weapon's pivot
    /// differs from the pack's scene) the weapon is shifted by that amount.
    /// </summary>
    private static XForm Place(WeaponAsset arms, Clip reference, int attach, HandRig right, HandRig? left, WeaponAnalysis a, out float rightError, out float leftError, out float shift, out string reason)
    {
        Vector3 ToModel(Vector3 c) => Vector3.Transform((c - a.CanonicalOffset) / a.Scale, Quaternion.Conjugate(a.ModelToCanonical));
        var attachWorld = arms.BoneWorld(attach, reference, 0);
        Vector3 Palm(HandRig rig) => arms.BoneWorld(rig.Hand, reference, 0).TransformPoint(rig.PalmCenter + rig.PalmNormal * rig.PalmThickness * 0.5f);

        var grip = a.Primary is { } p ? ToModel(p.Surface.Contact) : ToModel(a.WeaponBounds.Center);
        Vector3? support = a.Support is { } s && Vector3.Distance(s.Surface.Contact, a.Primary?.Surface.Contact ?? s.Surface.Contact) > 3f ? ToModel(s.Surface.Contact) : null;
        var place = XForm.Identity;
        var palmR = Palm(right);
        var dR = palmR - attachWorld.TransformPoint(grip);
        var dL = Vector3.Zero;
        if (left is not null && support is { } sup)
            dL = Palm(left) - attachWorld.TransformPoint(sup);
        rightError = dR.Length();
        leftError = support is null || left is null ? 0f : dL.Length();
        shift = 0f;
        reason = "the pack's own placement";

        // Both hands off the same way: the weapon's pivot differs from the pack's scene.
        var agree = support is null || left is null ? rightError > 2.5f : rightError > 2.5f && leftError > 2.5f && Vector3.Distance(dR, dL) < MathF.Max(2f, 0.5f * MathF.Max(rightError, leftError));
        if (agree)
        {
            var move = support is null || left is null ? dR : (dR + dL) * 0.5f;
            var local = Vector3.Transform(move, Quaternion.Conjugate(attachWorld.Rot));
            place = new XForm(local, Quaternion.Identity);
            shift = move.Length();
            rightError = (dR - move).Length();
            leftError = support is null || left is null ? 0f : (dL - move).Length();
            reason = $"shifted {shift:0.0} in onto the palms";
        }
        return place;
    }

    /// <summary>The arms with the weapon under <paramref name="attach"/>, and one clip per arm animation.</summary>
    private static WeaponAsset Build(WeaponAsset arms, WeaponAsset weapon, int attach, XForm place, List<Clip> armClips)
    {
        var ask = arms.Skeleton;
        var wsk = weapon.Skeleton;
        var names = new HashSet<string>(ask.Bones.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        string Unique(string n)
        {
            var name = n;
            for (var i = 2; names.Contains(name); i++)
                name = $"{n}_{i}";
            names.Add(name);
            return name;
        }
        var weaponNames = wsk.Bones.Select(b => Unique(b.Name)).ToArray();

        // Skeleton: the arms, then the weapon with its roots under the attach bone.
        var defs = new List<BoneDefinition>(ask.Count + wsk.Count);
        foreach (var b in ask.Bones)
            defs.Add(new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : ask[b.ParentIndex].Name, b.RestLocal));
        XForm RootLocal(XForm weaponLocal) => XForm.Compose(place, weaponLocal);
        foreach (var b in wsk.Bones)
            defs.Add(new BoneDefinition(weaponNames[b.Index], b.ParentIndex < 0 ? ask[attach].Name : weaponNames[b.ParentIndex], b.ParentIndex < 0 ? RootLocal(b.RestLocal) : b.RestLocal));
        var skeleton = Skeleton.Create(defs);
        var map = new int[wsk.Count];
        for (var i = 0; i < wsk.Count; i++)
            map[i] = skeleton.IndexOf(weaponNames[i]);
        var armMap = new int[ask.Count];
        for (var i = 0; i < ask.Count; i++)
            armMap[i] = skeleton.IndexOf(ask[i].Name);

        // Mesh: the weapon's vertices move from its rest to its place in the arms' rest.
        var weaponToCombined = XForm.Compose(skeleton.RestWorld[attach], place);
        var mesh = Merge(arms.Mesh, armMap, weapon.Mesh, map, weaponToCombined);

        // Clips: the arm animation with the weapon's clip of the same action (else its idle).
        var weaponIdle = weapon.Clips.FirstOrDefault(c => AnimationClassifier.Classify(c.Name).Role == AnimationRole.Idle);
        var clips = new List<Clip>();
        foreach (var armClip in armClips)
        {
            var own = weapon.Clips.FirstOrDefault(c => string.Equals(c.Name, armClip.Name, StringComparison.OrdinalIgnoreCase))
                ?? weapon.Clips.FirstOrDefault(c => AnimationClassifier.Classify(c.Name).Role is var r && r != AnimationRole.Unknown && r == AnimationClassifier.Classify(armClip.Name).Role)
                ?? weaponIdle;
            var frames = new List<XForm[]>(armClip.FrameCount);
            for (var f = 0; f < armClip.FrameCount; f++)
            {
                var locals = new XForm[skeleton.Count];
                for (var i = 0; i < ask.Count; i++)
                    locals[armMap[i]] = armClip.Frames[f][i];
                XForm[]? w = null;
                if (own is { FrameCount: > 0 })
                {
                    var t = f / armClip.Fps;
                    w = own.Frames[Math.Clamp((int)MathF.Round(t * own.Fps), 0, own.FrameCount - 1)];
                }
                for (var i = 0; i < wsk.Count; i++)
                {
                    var local = w is not null && i < w.Length ? w[i] : wsk[i].RestLocal;
                    locals[map[i]] = wsk[i].ParentIndex < 0 ? RootLocal(local) : local;
                }
                frames.Add(locals);
            }
            clips.Add(new Clip(armClip.Name, armClip.Fps, armClip.Looping, frames, armClip.NativeFps));
        }

        var asset = new WeaponAsset { CameraViews = arms.CameraViews, Name = weapon.Name, Kind = weapon.Kind, SourcePath = weapon.SourcePath, Skeleton = skeleton, Mesh = mesh, Clips = clips, Attachments = weapon.Attachments };
        asset.Notes.AddRange(weapon.Notes);
        return asset;
    }

    /// <summary>The arms' mesh and the weapon's (moved into place) as one, with both sets of materials.</summary>
    private static TriMesh Merge(TriMesh arms, int[] armMap, TriMesh weapon, int[] weaponMap, XForm weaponToCombined)
    {
        var positions = new List<Vector3>(arms.Positions.Length + weapon.Positions.Length);
        var bones = new List<int>();
        positions.AddRange(arms.Positions);
        bones.AddRange(Enumerable.Range(0, arms.Positions.Length).Select(v => arms.VertexBone is { } vb && vb[v] >= 0 && vb[v] < armMap.Length ? armMap[vb[v]] : 0));
        foreach (var p in weapon.Positions)
            positions.Add(weaponToCombined.TransformPoint(p));
        bones.AddRange(Enumerable.Range(0, weapon.Positions.Length).Select(v => weapon.VertexBone is { } vb && vb[v] >= 0 && vb[v] < weaponMap.Length ? weaponMap[vb[v]] : weaponMap[0]));

        var indices = arms.Indices.Concat(weapon.Indices.Select(i => i + arms.Positions.Length)).ToArray();
        var parts = arms.PartNames.Concat(weapon.PartNames).ToList();
        var triPart = Enumerable.Range(0, arms.TriangleCount).Select(t => arms.TrianglePart is { } tp ? tp[t] : 0)
            .Concat(Enumerable.Range(0, weapon.TriangleCount).Select(t => (weapon.TrianglePart is { } tp ? tp[t] : 0) + arms.PartNames.Count)).ToArray();
        var materials = arms.Materials.Concat(weapon.Materials).ToList();
        var triMaterial = Enumerable.Range(0, arms.TriangleCount).Select(t => arms.TriangleMaterial is { } tm ? tm[t] : 0)
            .Concat(Enumerable.Range(0, weapon.TriangleCount).Select(t => (weapon.TriangleMaterial is { } tm ? tm[t] : 0) + arms.Materials.Count)).ToArray();

        Vector3[]? normals = null;
        if (arms.CornerNormals is not null || weapon.CornerNormals is not null)
        {
            var armN = arms.CornerNormals ?? new TriMesh(arms.Positions, arms.Indices).EnsureNormals();
            var wN = weapon.CornerNormals ?? new TriMesh(weapon.Positions, weapon.Indices).EnsureNormals();
            normals = armN.Concat(wN.Select(n => Vector3.Transform(n, weaponToCombined.Rot))).ToArray();
        }
        Vector2[]? uvs = null;
        if (arms.CornerUVs is not null || weapon.CornerUVs is not null)
            uvs = (arms.CornerUVs ?? new Vector2[arms.Indices.Length]).Concat(weapon.CornerUVs ?? new Vector2[weapon.Indices.Length]).ToArray();
        return new TriMesh(positions.ToArray(), indices, bones.ToArray(), triPart, parts, normals, uvs, triMaterial, materials);
    }

    /// <summary>Folders too broad to search (a drive, the user's home, Desktop, Downloads, Documents).</summary>
    private static bool IsBroadFolder(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) == full)
            return true;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
        if (home.Length > 0 && string.Equals(full, home, StringComparison.OrdinalIgnoreCase))
            return true;
        var name = Path.GetFileName(full).ToLowerInvariant();
        return name is "desktop" or "downloads" or "documents" or "users" or "home";
    }

    private static IEnumerable<string> Files(string folder, int depth, string pattern)
    {
        List<string> here;
        try
        {
            here = Directory.EnumerateFiles(folder, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
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
            foreach (var f in Files(sub, depth - 1, pattern))
                yield return f;
    }
}
