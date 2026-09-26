#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Inspects a <see cref="WeaponAsset"/>: separates the weapon from any first-person arms,
/// measures part motion, finds the bore, muzzle and ejection port, classifies the weapon and
/// locates grip surfaces on the actual geometry. Every result carries a confidence so the
/// editor only asks about the uncertain ones.
/// </summary>
public static class WeaponAnalyzer
{
    private static readonly AsyncLocal<Action<string>?> _trace = new();

    /// <summary>Optional diagnostics sink (tests and the editor log); flows with the current async context.</summary>
    public static Action<string>? Trace
    {
        get => _trace.Value;
        set => _trace.Value = value;
    }

    private static readonly string[] ArmTokens =
    {
        "arm", "arms", "hand", "hands", "finger", "thumb", "index", "middle", "ring", "pinky", "little", "palm",
        "wrist", "elbow", "forearm", "upperarm", "clavicle", "shoulder", "camera", "cam", "spine", "neck", "head",
        "glove", "gloves", "sleeve", "watch", "body", "skin", "l", "r", "left", "right", "ik", "pole",
    };

    // Tokens that on their own identify an arm bone; "l"/"r"/"left" only count alongside these.
    private static readonly string[] StrongArmTokens =
    {
        "arm", "arms", "hand", "hands", "finger", "thumb", "palm", "wrist", "elbow", "forearm", "upperarm", "clavicle",
        "shoulder", "camera", "cam", "spine", "neck", "head", "glove", "gloves", "sleeve", "watch", "pinky",
    };

    private static readonly (PartKind Kind, string[] Aliases, string[]? Joined)[] PartNames =
    {
        (PartKind.Magazine, new[] { "mag", "magazine", "clip", "ammo", "drum", "cartridge", "mags" }, new[] { "magazine", "magwell" }),
        (PartKind.ChargingHandle, new[] { "charging", "charginghandle", "ch", "cockinghandle", "cocking" }, new[] { "charginghandle", "chargehandle", "cockinghandle" }),
        (PartKind.Slide, new[] { "slide", "slider" }, null),
        (PartKind.Bolt, new[] { "bolt", "carrier", "breech", "boltcarrier" }, new[] { "boltcarrier" }),
        (PartKind.Trigger, new[] { "trigger", "trig" }, null),
        (PartKind.Hammer, new[] { "hammer", "striker" }, null),
        (PartKind.Cylinder, new[] { "cylinder", "cyl", "revolver_drum" }, null),
        (PartKind.Pump, new[] { "pump", "forend", "foreend", "slidehandle" }, null),
        (PartKind.Foregrip, new[] { "foregrip", "vfg", "vertgrip", "frontgrip", "handstop", "angledgrip" }, new[] { "foregrip", "frontgrip", "verticalgrip" }),
        (PartKind.Stock, new[] { "stock", "butt", "buttstock" }, null),
        (PartKind.Scope, new[] { "scope", "optic", "sight", "reddot", "holo", "acog" }, null),
        (PartKind.Barrel, new[] { "barrel" }, null),
    };

    public static WeaponAnalysis Analyze(WeaponAsset asset, CancellationToken cancel = default)
        => Analyze(asset, new AnalyzeOptions(), cancel);

    public static WeaponAnalysis Analyze(WeaponAsset source, AnalyzeOptions options, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new AnalyzeOptions();

        // 0. Analyze the assembled weapon: the idle (or first) clip's first frame, not the bind pose.
        var loaded = source;
        var (reference, referenceFrame) = ReferencePose(source, options.ReferenceClip);
        // The file's origin, followed through every move below (a viewmodel's eye often sits there).
        var origin = Vector3.Zero;
        if (reference is not null)
        {
            // First-person clips move the whole weapon around the camera; put the weapon root
            // back where it rests so the assembled weapon keeps its modelled orientation.
            var bindArms = FindArmBones(loaded.Skeleton);
            var bindTris = WeaponTriangles(loaded, bindArms, out _);
            var bindRoot = bindTris.Length > 0 ? WeaponRoot(loaded, bindArms, bindTris) : 0;
            var posed = source.WithReferencePose(reference, referenceFrame);
            var back = XForm.Compose(loaded.Skeleton.RestWorld[bindRoot], posed.Skeleton.RestWorld[bindRoot].Inverse());
            source = posed.Moved(back);
            origin = back.TransformPoint(origin);
        }

        // 1. Find the weapon inside the file (drop arms, backgrounds, helper geometry).
        var sourceArms = FindArmBones(source.Skeleton);
        var sourceTris = WeaponTriangles(source, sourceArms, out var dropped);
        // Arms holding nothing (fists): the whole file stands in as the "weapon" for measuring.
        var handsOnly = sourceTris.Length == 0 && sourceArms.Count > 0;
        if (sourceTris.Length == 0)
            sourceTris = Enumerable.Range(0, source.Mesh.TriangleCount).ToArray();
        sourceTris = DropSpareMagazines(source, sourceTris, dropped);
        // Two copies, one per hand: everything below measures the right one.
        var dual = handsOnly ? null : DualWeapons.Find(source, sourceArms, sourceTris);
        var secondTris = Array.Empty<int>();
        if (dual is not null)
        {
            var left = dual.LeftTriangles.ToHashSet();
            secondTris = dual.LeftTriangles;
            sourceTris = sourceTris.Where(t => !left.Contains(t)).ToArray();
        }

        // 2. Bring it into canonical space: muzzle +X, up +Z, realistic size.
        var sourceBounds = TriangleBounds(source.Mesh, sourceTris);
        var orientation = GuessOrientation(source.Mesh, sourceTris, sourceBounds, OrientationHints(source, sourceTris));
        var rotation = options.Rotation ?? (orientation.Confidence >= 0.45f ? orientation.ToCanonical : Quaternion.Identity);
        var rotatedLength = RotatedLength(source.Mesh, sourceTris, rotation);
        var (scale, scaleReason) = options.Scale is { } forced
            ? (forced, "set manually")
            : GuessScale(rotatedLength, WeaponClassifier.FromNames(source));
        // First-person arms in the file are the better yardstick: a forearm is about ten inches
        // whatever the weapon (an item's plausible size spans too much to tell the units).
        if (options.Scale is null && ForearmLength(source.Skeleton, sourceArms) is { } forearm && forearm * scale is < 7f or > 15f)
        {
            scale = HumanForearm / forearm;
            scaleReason = $"sized by its first-person arms (forearm {forearm:0.##} units)";
        }
        var asset = MathF.Abs(scale - 1f) < 1e-6f && MathF.Abs(rotation.W) > 0.99999f ? source : source.Transformed(rotation, scale);
        origin = Vector3.Transform(origin, rotation) * scale;

        // 3. Centre the weapon on its own bounds: first-person rigs park weapons far from the
        //    origin (at camera height), which would give the world model a useless pivot.
        var centre = TriangleBounds(asset.Mesh, sourceTris).Center;
        if (centre.Length() > 1e-3f)
            asset = asset.Moved(new XForm(-centre, Quaternion.Identity));

        var skeleton = asset.Skeleton;
        var mesh = asset.Mesh;
        var armBones = FindArmBones(skeleton);
        var weaponTris = sourceTris;
        var bounds = TriangleBounds(mesh, weaponTris);

        var root = WeaponRoot(asset, armBones, weaponTris);
        var motion = MeasureMotion(asset, armBones, cancel);
        var profile = ShapeProfile.Build(mesh, weaponTris);
        var (boreStart, boreEnd, boreDiameter) = FindBore(profile, bounds);

        var analysis = new WeaponAnalysis
        {
            Source = loaded,
            ReferenceClip = reference?.Name ?? "",
            ReferenceFrame = referenceFrame,
            Asset = asset,
            ModelToCanonical = rotation,
            CanonicalOffset = -centre,
            Scale = scale,
            ScaleReason = scaleReason,
            RootBone = root,
            ArmBones = armBones,
            HandsOnly = handsOnly,
            SourceOrigin = origin - centre,
            WeaponTriangles = weaponTris,
            SecondWeaponBone = dual is null ? null : source.Skeleton[dual.Left].Name,
            SecondWeaponTriangles = secondTris,
            WeaponBounds = bounds,
            Profile = profile,
            Orientation = orientation,
            Motion = motion,
            BoreStart = boreStart,
            BoreEnd = boreEnd,
            BoreDiameter = boreDiameter,
        };

        if (armBones.Count > 0)
            analysis.Notes.Add($"First-person arms found ({armBones.Count} bones); they are excluded from the weapon.");
        foreach (var d in dropped)
            analysis.Notes.Add($"Ignored '{d}' (not part of the weapon).");
        if (orientation.NeedsFix && options.Rotation is null && orientation.Confidence >= 0.45f)
            analysis.Notes.Add($"Weapon was turned to face forward ({orientation.Reason}).");
        if (MathF.Abs(scale - 1f) > 1e-3f)
            analysis.Notes.Add($"Weapon scaled by {scale:0.####} ({scaleReason}).");

        cancel.ThrowIfCancellationRequested();
        DetectParts(analysis);
        analysis.Type = WeaponClassifier.Classify(analysis);
        analysis.Muzzle = DetectMuzzle(analysis);
        analysis.Eject = DetectEject(analysis);
        ClassifyAnimations(analysis);
        cancel.ThrowIfCancellationRequested();
        GripFinder.FindGrips(analysis);
        return analysis;
    }

    /// <summary>The clip whose first frame shows the weapon assembled: idle, else the first clip.</summary>
    public static Clip? ReferenceClip(WeaponAsset asset, string? preferred = null) => ReferencePose(asset, preferred).Clip;

    /// <summary>
    /// Picks the clip frame that shows the weapon assembled. Idle frame 0 is preferred, but some
    /// exports leave magazines unkeyed in idle (they only look right after a reload), so the
    /// frame where named magazines sit closest to the rest of the weapon wins.
    /// </summary>
    public static (Clip? Clip, int Frame) ReferencePose(WeaponAsset asset, string? preferred = null)
    {
        if (asset.Clips.Count == 0)
            return (null, 0);
        if (!string.IsNullOrEmpty(preferred) && asset.FindClip(preferred) is { } chosen)
            return (chosen, 0);

        // A take's first-frame still (made for items) is often the start of a draw, not a held pose.
        var guesses = asset.Clips.Where(c => !c.Name.EndsWith(" start pose", StringComparison.Ordinal))
            .Select(c => (Clip: c, Guess: AnimationClassifier.Classify(c.Name))).ToList();
        // Rests found inside split takes are held poses too.
        guesses.AddRange(asset.Clips.Where(c => c.Looping && AnimationClassifier.Classify(c.Name).Role == AnimationRole.Unknown)
            .Select(c => (Clip: c, Guess: new AnimationGuess(c.Name, AnimationRole.Idle, 0.3f, "a rest"))));
        var candidates = new List<(Clip Clip, int Frame)>();
        void Add(AnimationRole role, bool last)
        {
            foreach (var g in guesses.Where(g => g.Guess.Role == role).OrderByDescending(g => g.Guess.Confidence))
                candidates.Add((g.Clip, last ? g.Clip.FrameCount - 1 : 0));
        }
        Add(AnimationRole.Idle, false);
        Add(AnimationRole.Reload, true);
        Add(AnimationRole.EmptyReload, true);
        Add(AnimationRole.TacticalReload, true);
        Add(AnimationRole.Fire, false);
        Add(AnimationRole.Draw, true);
        candidates.Add((asset.Clips[0], 0));

        var magBones = new HashSet<int>();
        for (var b = 0; b < asset.Skeleton.Count; b++)
        {
            if (IsMagazineName(asset.Skeleton[b].Name))
                magBones.Add(b);
        }
        if (magBones.Count == 0)
            return candidates[0];

        var arms = FindArmBones(asset.Skeleton);
        var best = candidates[0];
        var bestGap = float.MaxValue;
        foreach (var candidate in candidates.Distinct())
        {
            var posed = asset.WithReferencePose(candidate.Clip, candidate.Frame);
            var mesh = posed.Mesh;
            var body = new List<int>();
            var mags = new Dictionary<int, List<int>>();
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                var bone = mesh.TriangleBone(t);
                if (bone < 0 || arms.Contains(bone))
                    continue;
                if (magBones.Contains(bone))
                {
                    if (!mags.TryGetValue(bone, out var list))
                        mags[bone] = list = new List<int>();
                    list.Add(t);
                }
                else
                {
                    body.Add(t);
                }
            }
            if (mags.Count == 0 || body.Count == 0)
                return candidates[0];

            // An inserted magazine touches the receiver: the closest magazine vertex lies on (or
            // inside) the body surface. Spares may stay parked, so the best magazine decides.
            var bvh = new MeshBvh(mesh, body);
            var gap = float.MaxValue;
            foreach (var tris in mags.Values)
            {
                var step = Math.Max(1, tris.Count / 150);
                for (var i = 0; i < tris.Count; i += step)
                {
                    var (x, _, _) = mesh.Triangle(tris[i]);
                    var closest = bvh.Closest(x, gap);
                    if (closest is { } c)
                        gap = MathF.Min(gap, c.Inside ? 0f : c.Distance);
                }
            }
            Trace?.Invoke($"reference candidate {candidate.Clip.Name}@{candidate.Frame}: magazine gap {gap:0.###}");
            if (gap < bestGap - 1e-3f)
            {
                bestGap = gap;
                best = candidate;
            }
            if (bestGap <= 0.01f)
                break;
        }
        return best;
    }

    private static Bounds TriangleBounds(TriMesh mesh, IEnumerable<int> tris)
    {
        var b = Bounds.Empty;
        foreach (var t in tris)
        {
            var (x, y, z) = mesh.Triangle(t);
            b = b.Encapsulate(x).Encapsulate(y).Encapsulate(z);
        }
        return b;
    }

    private static float RotatedLength(TriMesh mesh, int[] tris, Quaternion rotation)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var t in tris)
            for (var k = 0; k < 3; k++)
            {
                var x = Vector3.Transform(mesh.Positions[mesh.Indices[t * 3 + k]], rotation).X;
                min = MathF.Min(min, x);
                max = MathF.Max(max, x);
            }
        return max > min ? max - min : 0f;
    }

    private static readonly float[] CleanScales = { 1f, 0.1f, 0.01f, 0.001f, 10f, 100f, 1f / 2.54f, 2.54f, 0.254f, 0.0254f, 39.3701f, 0.393701f, 0.0393701f, 0.00393701f };

    /// <summary>
    /// Picks a uniform scale that gives the weapon a believable length. Unit mix-ups (metres
    /// vs centimetres vs inches) are preferred over arbitrary factors.
    /// </summary>
    /// <summary>A human forearm (elbow to wrist), inches.</summary>
    public const float HumanForearm = 10.2f;

    /// <summary>
    /// Elbow-to-wrist length of the file's arms (the longer side), in file units, or null when
    /// there are no arms with a recognisable hand.
    /// </summary>
    public static float? ForearmLength(Skeleton skeleton, IReadOnlySet<int> armBones)
    {
        if (armBones.Count == 0)
            return null;
        float? best = null;
        var rest = skeleton.RestWorld;
        foreach (var side in new[] { Hands.Side.Right, Hands.Side.Left })
        {
            if (Hands.HandRig.Build(skeleton, side) is not { } hand || !armBones.Contains(hand.Hand))
                continue;
            // The forearm bone starts at the elbow: the farthest of the nearby ancestors named like
            // one (twist bones and helpers sit along it), else the hand's parent.
            var elbow = -1;
            var farthest = 0f;
            var steps = 0;
            for (var p = skeleton[hand.Hand].ParentIndex; p >= 0 && steps < 4; p = skeleton[p].ParentIndex, steps++)
            {
                var tokens = NameTokens.Split(skeleton[p].Name);
                var joined = NameTokens.Joined(tokens);
                if (!NameTokens.Has(tokens, "forearm", "fore", "elbow", "lowerarm") && !joined.Contains("lowerarm") && !joined.Contains("forearm") && !joined.Contains("armlower"))
                    continue;
                var d = Vector3.Distance(rest[hand.Hand].Pos, rest[p].Pos);
                if (d > farthest)
                {
                    farthest = d;
                    elbow = p;
                }
            }
            if (elbow < 0)
                elbow = skeleton[hand.Hand].ParentIndex;
            if (elbow < 0)
                continue;
            var length = Vector3.Distance(rest[hand.Hand].Pos, rest[elbow].Pos);
            if (length > 1e-3f && (best is null || length > best))
                best = length;
        }
        return best;
    }

    public static (float Scale, string Reason) GuessScale(float length, WeaponType? nameType)
    {
        if (!(length > 1e-4f))
            return (1f, "no measurable length");
        var (min, max) = nameType is { } t ? WeaponTypes.TypicalLength(t) : (5f, 60f);
        if (length >= min * 0.6f && length <= max * 1.5f)
            return (1f, "size looks right");
        var mid = MathF.Sqrt(min * max);
        var best = CleanScales
            .Where(s => length * s >= min * 0.75f && length * s <= max * 1.3f)
            .OrderBy(s => MathF.Abs(MathF.Log(length * s / mid)))
            .Cast<float?>()
            .FirstOrDefault();
        if (best is { } clean)
            return (clean, $"{length:0.#} in long before, {length * clean:0.#} in after (unit mix-up)");
        var factor = mid / length;
        return (factor, $"{length:0.#} in long; rescaled to a typical {(nameType is { } nt ? WeaponTypes.Label(nt).ToLowerInvariant() : "weapon")} length");
    }

    /// <summary>
    /// Reload rigs often carry a second magazine parked somewhere. Only magazines that touch the
    /// rest of the weapon are part of it.
    /// </summary>
    private static int[] DropSpareMagazines(WeaponAsset asset, int[] tris, List<string> dropped)
    {
        var mesh = asset.Mesh;
        var groups = new Dictionary<int, List<int>>();
        var body = new List<int>();
        foreach (var t in tris)
        {
            var bone = mesh.TriangleBone(t);
            if (bone >= 0 && IsMagazineName(asset.Skeleton[bone].Name))
            {
                if (!groups.TryGetValue(bone, out var list))
                    groups[bone] = list = new List<int>();
                list.Add(t);
            }
            else
            {
                body.Add(t);
            }
        }
        if (groups.Count < 2 || body.Count == 0)
            return tris;
        var bvh = new MeshBvh(mesh, body);
        var gaps = groups.ToDictionary(g => g.Key, g =>
        {
            var gap = float.MaxValue;
            var step = Math.Max(1, g.Value.Count / 200);
            for (var i = 0; i < g.Value.Count; i += step)
            {
                var (x, _, _) = mesh.Triangle(g.Value[i]);
                if (bvh.Closest(x, gap) is { } c)
                    gap = MathF.Min(gap, c.Inside ? 0f : c.Distance);
            }
            return gap;
        });
        var bestGap = gaps.Values.Min();
        var size = TriangleBounds(mesh, body).Size.Length();
        var spares = gaps.Where(g => g.Value > bestGap + size * 0.01f).Select(g => g.Key).ToHashSet();
        if (spares.Count == 0 || spares.Count == groups.Count)
            return tris;
        foreach (var s in spares)
            dropped.Add($"{asset.Skeleton[s].Name} (spare magazine)");
        return tris.Where(t => !spares.Contains(mesh.TriangleBone(t))).ToArray();
    }

    /// <summary>"mag", "Magazine_L", "pmag2", "UZI Mag", "clipMag" — but not "magnum" or "image".</summary>
    public static bool IsMagazineName(string name)
    {
        var tokens = NameTokens.Split(name);
        if (NameTokens.Has(tokens, "mag", "magazine", "mags", "drum"))
            return true;
        foreach (var t in tokens)
        {
            var i = t.IndexOf("mag", StringComparison.Ordinal);
            if (i < 0 || t.Contains("magnum") || t.Contains("image") || t.Contains("magic"))
                continue;
            // Allow short prefixes/suffixes around "mag" (pmag, mag2, emag, magl).
            if (t.Length - 3 <= 3)
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- arms

    public static bool IsArmBoneName(string name)
    {
        var tokens = NameTokens.Split(name);
        if (NameTokens.Has(tokens, StrongArmTokens))
            return true;
        var joined = NameTokens.Joined(tokens);
        return joined.Contains("upperarm") || joined.Contains("forearm") || joined.Contains("lowerarm")
            || (tokens.Length >= 2 && NameTokens.Has(tokens, "index", "middle", "ring", "little") && NameTokens.Has(tokens, "l", "r", "left", "right", "0", "1", "2", "3", "01", "02", "03"));
    }

    private static HashSet<int> FindArmBones(Skeleton skeleton)
    {
        var arms = new HashSet<int>();
        // Everything hanging from a socket ("hand_item_r", "weapon_attach") is the held item, not
        // the hand, whatever its bones are called (a shotgun's pump is its "Forearm").
        var held = new HashSet<int>();
        for (var i = 0; i < skeleton.Count; i++)
        {
            var parent = skeleton[i].ParentIndex;
            if (parent >= 0 && (held.Contains(parent) || (arms.Contains(parent) && IsSocketName(skeleton[parent].Name))))
            {
                held.Add(i);
                continue;
            }
            if (IsArmBoneName(skeleton[i].Name) || (parent >= 0 && arms.Contains(parent) && !IsWeaponName(skeleton[i].Name)))
                arms.Add(i);
        }
        // A skeleton that is "all arms" is really a mislabelled weapon; keep it.
        if (arms.Count >= skeleton.Count)
            return new HashSet<int>();
        // Arms end in hands: a lone "Forearm" or "Camera" bone without a hand or fingers is a
        // part of the weapon (a shotgun's pump is its forearm).
        if (!arms.Any(b => NameTokens.Has(NameTokens.Split(skeleton[b].Name), "hand", "hands", "finger", "thumb", "palm", "wrist", "index", "pinky")))
            return new HashSet<int>();
        return arms;
    }

    /// <summary>A bone items are attached to: "hand_item_r", "prop_R", "weapon_socket".</summary>
    public static bool IsSocketName(string name)
        => NameTokens.Has(NameTokens.Split(name), "item", "prop", "socket", "attach", "attachment", "holder", "weapon", "gun", "wpn");

    private static bool IsWeaponName(string name)
    {
        var tokens = NameTokens.Split(name);
        return NameTokens.Has(tokens, "weapon", "gun", "wpn", "rifle", "pistol", "shotgun", "smg", "sniper", "launcher", "revolver") || WeaponClassifier.NamesAType(name) || PartNames.Any(p => NameTokens.Has(tokens, p.Aliases));
    }

    private static readonly string[] NonWeaponParts =
    {
        "arm", "arms", "hand", "hands", "glove", "gloves", "sleeve", "watch", "body", "skin", "cloth", "cloths", "character",
        "fpsarms", "face", "head", "eye", "eyes", "hair", "teeth", "tongue", "background", "backdrop", "floor", "ground",
        "shadow", "camera", "light", "lamp", "sky", "skybox", "aim", "aimbottom", "crosshair", "collision", "collider", "ucx", "ubx",
    };

    private static int[] WeaponTriangles(WeaponAsset asset, HashSet<int> armBones, out List<string> dropped)
    {
        var mesh = asset.Mesh;
        dropped = new List<string>();
        var excludedParts = new HashSet<int>();
        for (var p = 0; p < mesh.PartNames.Count; p++)
        {
            var tokens = NameTokens.Split(mesh.PartNames[p]);
            if (NameTokens.Has(tokens, NonWeaponParts))
                excludedParts.Add(p);
        }

        // Groups are (mesh part, bone) so a spare magazine inside one skinned mesh can be dropped.
        var byPart = new Dictionary<(int Part, int Bone), List<int>>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var part = mesh.TrianglePart[t];
            if (excludedParts.Contains(part))
                continue;
            var bone = mesh.TriangleBone(t);
            if (bone >= 0 && armBones.Contains(bone))
                continue;
            if (!byPart.TryGetValue((part, bone), out var list))
                byPart[(part, bone)] = list = new List<int>();
            list.Add(t);
        }
        foreach (var p in excludedParts)
            if (mesh.TrianglePart.Contains(p))
                dropped.Add(mesh.PartNames[p]);
        if (byPart.Count == 0)
            return Array.Empty<int>();

        // Grow a cluster from the biggest group; groups far away or giant flat cards are scenery.
        var partBounds = byPart.ToDictionary(kv => kv.Key, kv => TriangleBounds(mesh, kv.Value));
        var main = byPart.OrderByDescending(kv => kv.Value.Count).First().Key;
        var mainCount = byPart[main].Count;
        var cluster = new HashSet<(int Part, int Bone)> { main };
        var clusterBounds = partBounds[main];
        var changed = true;
        while (changed)
        {
            changed = false;
            var diag = clusterBounds.Size.Length();
            foreach (var (part, b) in partBounds)
            {
                if (cluster.Contains(part))
                    continue;
                var size = b.Size;
                // Backdrops: a handful of triangles, flat, and much bigger than the weapon.
                var thinnest = MathF.Min(size.X, MathF.Min(size.Y, size.Z));
                var widest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
                var flatCard = byPart[part].Count <= 6 && thinnest < widest * 0.02f && size.Length() > diag * 1.5f;
                if (flatCard)
                    continue;
                var gap = Vector3.Max(Vector3.Zero, Vector3.Max(clusterBounds.Min - b.Max, b.Min - clusterBounds.Max)).Length();
                // Small detached pieces (a parked spare magazine) must touch the weapon.
                var small = byPart[part].Count < mainCount * 0.25f;
                if (gap <= diag * (small ? 0.06f : 0.35f) && size.Length() < diag * 3f)
                {
                    cluster.Add(part);
                    clusterBounds = clusterBounds.Encapsulate(b);
                    changed = true;
                }
            }
        }
        foreach (var key in byPart.Keys.Where(p => !cluster.Contains(p)))
            dropped.Add(key.Bone >= 0 ? $"{mesh.PartNames[key.Part]} / {asset.Skeleton[key.Bone].Name}" : mesh.PartNames[key.Part]);

        return byPart.Where(kv => cluster.Contains(kv.Key)).SelectMany(kv => kv.Value).OrderBy(t => t).ToArray();
    }

    private static int WeaponRoot(WeaponAsset asset, HashSet<int> armBones, int[] weaponTris)
    {
        var skeleton = asset.Skeleton;
        var counts = new Dictionary<int, int>();
        foreach (var t in weaponTris)
        {
            var b = asset.Mesh.TriangleBone(t);
            if (b >= 0)
                counts[b] = counts.GetValueOrDefault(b) + 1;
        }
        if (counts.Count == 0)
            return 0;
        var dominant = counts.OrderByDescending(kv => kv.Value).First().Key;
        // Climb while the parent is still a weapon bone, so the root is the weapon's own base
        // (not a magazine that happens to be densely tessellated).
        var bone = dominant;
        while (skeleton[bone].ParentIndex >= 0 && !armBones.Contains(skeleton[bone].ParentIndex))
        {
            var parent = skeleton[bone].ParentIndex;
            var name = NameTokens.Split(skeleton[parent].Name);
            if (NameTokens.Has(name, "root", "scene", "armature", "rootnode") && skeleton[parent].ParentIndex < 0 && counts.GetValueOrDefault(parent) == 0)
                break;
            bone = parent;
        }
        return bone;
    }

    // ---------------------------------------------------------------- motion

    private static List<BoneMotion> MeasureMotion(WeaponAsset asset, HashSet<int> armBones, CancellationToken cancel)
    {
        var skeleton = asset.Skeleton;
        var result = new List<BoneMotion>();
        for (var b = 0; b < skeleton.Count; b++)
        {
            if (armBones.Contains(b) || skeleton[b].ParentIndex < 0)
                continue;
            var rest = skeleton[b].RestLocal;
            var parentWorld = skeleton.RestWorld[skeleton[b].ParentIndex];
            float maxT = 0f, maxR = 0f;
            var dir = Vector3.Zero;
            string clipName = "";
            var peakFrame = 0;
            foreach (var clip in asset.Clips)
            {
                cancel.ThrowIfCancellationRequested();
                for (var f = 0; f < clip.FrameCount; f++)
                {
                    var local = clip.Frames[f][b];
                    var d = local.Pos - rest.Pos;
                    var t = d.Length();
                    var r = MathQ.AngleBetween(local.Rot, rest.Rot) * 180f / MathF.PI;
                    if (t > maxT)
                    {
                        maxT = t;
                        // Direction in model space (through the parent's rest rotation).
                        dir = t > 1e-5f ? Vector3.Normalize(parentWorld.TransformVector(d)) : Vector3.Zero;
                        clipName = clip.Name;
                        peakFrame = f;
                    }
                    if (r > maxR)
                    {
                        maxR = r;
                        if (maxT < 1e-3f)
                        {
                            clipName = clip.Name;
                            peakFrame = f;
                        }
                    }
                }
            }
            result.Add(new BoneMotion(b, maxT, maxR, dir, clipName, peakFrame));
        }
        return result;
    }

    // ---------------------------------------------------------------- orientation

    /// <summary>
    /// Weapons are longest along their barrel, and grips/magazines hang below the bore, so the
    /// longest axis is forward and the side with sparse coverage is down. The muzzle end is the
    /// thinner end.
    /// </summary>
    /// <summary>
    /// Named parts that tell up from down: triggers and grips hang below the bore, scopes and
    /// sights sit on top. Weight says how reliable each cue is.
    /// </summary>
    public static List<(Vector3 Point, float Side, float Weight)> OrientationHints(WeaponAsset asset, int[] tris)
    {
        var hints = new List<(Vector3, float, float)>();
        var mesh = asset.Mesh;
        var byBone = new Dictionary<int, List<int>>();
        var byPart = new Dictionary<int, List<int>>();
        foreach (var t in tris)
        {
            var b = mesh.TriangleBone(t);
            if (b >= 0)
            {
                if (!byBone.TryGetValue(b, out var l)) byBone[b] = l = new List<int>();
                l.Add(t);
            }
            var part = mesh.TrianglePart[t];
            if (!byPart.TryGetValue(part, out var pl)) byPart[part] = pl = new List<int>();
            pl.Add(t);
        }
        void Consider(string name, List<int> group)
        {
            var tokens = NameTokens.Split(name);
            if (NameTokens.Has(tokens, "trigger", "trig", "triggerguard"))
                hints.Add((Centroid(mesh, group), -1f, 2f));
            else if (NameTokens.Has(tokens, "grip", "handle", "pistolgrip") && !NameTokens.Has(tokens, "charging", "cocking"))
                hints.Add((Centroid(mesh, group), -1f, 1f));
            else if (NameTokens.Has(tokens, "scope", "optic", "sight", "reddot", "holo", "acog", "rail"))
                hints.Add((Centroid(mesh, group), 1f, 1f));
        }
        foreach (var (bone, group) in byBone)
            Consider(asset.Skeleton[bone].Name, group);
        foreach (var (part, group) in byPart)
            Consider(mesh.PartNames[part], group);
        return hints;
    }

    public static OrientationGuess GuessOrientation(TriMesh mesh, int[] tris, Bounds bounds, IReadOnlyList<(Vector3 Point, float Side, float Weight)>? hints = null)
    {
        // Area-weighted samples.
        var points = new List<Vector3>();
        var weights = new List<float>();
        foreach (var t in tris)
        {
            var (a, b, c) = mesh.Triangle(t);
            var area = Vector3.Cross(b - a, c - a).Length() * 0.5f + 1e-6f;
            points.Add(a); points.Add(b); points.Add(c); points.Add((a + b + c) / 3f);
            weights.Add(area); weights.Add(area); weights.Add(area); weights.Add(area * 3f);
        }
        if (points.Count < 4)
            return new OrientationGuess(Quaternion.Identity, 0.1f, "not enough geometry");

        // Principal axes; snapped to the file axes when close (most weapons are modelled axis aligned).
        var (axes, extents) = PrincipalAxes(points);
        static Vector3 Snap(Vector3 v)
        {
            var abs = Vector3.Abs(v);
            var axis = abs.X >= abs.Y && abs.X >= abs.Z ? new Vector3(MathF.Sign(v.X), 0, 0) : abs.Y >= abs.Z ? new Vector3(0, MathF.Sign(v.Y), 0) : new Vector3(0, 0, MathF.Sign(v.Z));
            return Vector3.Dot(axis, v) > MathF.Cos(35f * MathF.PI / 180f) ? axis : v;
        }
        var forward = Snap(axes[0]);
        var upAxis = Snap(Vector3.Normalize(axes[1] - forward * Vector3.Dot(axes[1], forward)));
        upAxis = Vector3.Normalize(upAxis - forward * Vector3.Dot(upAxis, forward));

        float Along(Vector3 p) => Vector3.Dot(p, forward);
        float Up(Vector3 p) => Vector3.Dot(p, upAxis);
        var min = points.Min(Along);
        var len = MathF.Max(1e-4f, points.Max(Along) - min);
        var side = Vector3.Cross(upAxis, forward);
        var widthExtent = MathF.Max(1e-4f, points.Max(p => Up(p)) - points.Min(p => Up(p)));
        if (len < widthExtent * 1.25f)
            return CompactOrientation(points, hints);

        // Thin end = muzzle: compare cross-section spread in the first and last 12%.
        float Spread(Func<float, bool> inSlice)
        {
            var slice = points.Where(p => inSlice((Along(p) - min) / len)).ToList();
            if (slice.Count == 0)
                return 0f;
            return (slice.Max(Up) - slice.Min(Up)) + (slice.Max(p => Vector3.Dot(p, side)) - slice.Min(p => Vector3.Dot(p, side)));
        }
        var lowEnd = Spread(u => u < 0.12f);
        var highEnd = Spread(u => u > 0.88f);
        var forwardSign = highEnd <= lowEnd ? 1f : -1f;
        var endConfidence = MathF.Abs(highEnd - lowEnd) / MathF.Max(1e-3f, MathF.Max(highEnd, lowEnd));

        // Up: the half with sparse lengthwise coverage (grip, magazine) is down.
        var upMin = points.Min(Up);
        var upLen = MathF.Max(1e-3f, points.Max(Up) - upMin);
        float Coverage(bool top)
        {
            var bins = new HashSet<int>();
            foreach (var p in points)
            {
                var u = (Up(p) - upMin) / upLen;
                if (top ? u > 0.7f : u < 0.3f)
                    bins.Add((int)((Along(p) - min) / len * 40f));
            }
            return bins.Count / 41f;
        }
        var topCover = Coverage(true);
        var bottomCover = Coverage(false);
        var vote = (topCover - bottomCover) * 1.5f;

        // Named parts compare against the bore line at the muzzle end.
        if (hints is { Count: > 0 })
        {
            var front = points.Where(p => forwardSign > 0 ? (Along(p) - min) / len > 0.9f : (Along(p) - min) / len < 0.1f).Select(Up).OrderBy(v => v).ToList();
            var boreUp = front.Count > 0 ? front[front.Count / 2] : (upMin + upLen * 0.5f);
            foreach (var (point, sideHint, weight) in hints)
            {
                var d = Up(point) - boreUp;
                if (MathF.Abs(d) < upLen * 0.03f)
                    continue;
                vote += MathF.Sign(d) * sideHint * weight;
            }
        }
        var upSign = vote >= 0f ? 1f : -1f;
        var upConfidence = Math.Clamp(MathF.Abs(vote) / 2f, 0f, 1f);

        var f = forward * forwardSign;
        var u2 = upAxis * upSign;
        var from = MathQ.FromAxes(f, Vector3.Cross(u2, f));
        var toCanonical = MathQ.Normalize(Quaternion.Conjugate(from));
        var confidence = Math.Clamp(0.35f + endConfidence * 0.4f + upConfidence * 0.6f, 0f, 1f);
        var reason = $"forward {Describe(f)}, up {Describe(u2)}";
        return new OrientationGuess(toCanonical, confidence, reason);
    }

    /// <summary>
    /// Compact weapons (stubby pistols, cartoon props) aren't clearly longest along the barrel.
    /// Up is trusted from the file (+Z after axis conversion); forward is the horizontal axis whose
    /// rear carries the hanging grip and whose front is the thinner muzzle end.
    /// </summary>
    private static OrientationGuess CompactOrientation(List<Vector3> points, IReadOnlyList<(Vector3 Point, float Side, float Weight)>? hints)
    {
        var minZ = points.Min(p => p.Z);
        var maxZ = points.Max(p => p.Z);
        var height = MathF.Max(1e-4f, maxZ - minZ);
        var low = points.Where(p => p.Z < minZ + height * 0.3f).ToList();
        var best = Vector3.UnitX;
        var bestScore = float.MinValue;
        var scores = new List<float>();
        foreach (var f in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY })
        {
            float Along(Vector3 p) => Vector3.Dot(p, f);
            var min = points.Min(Along);
            var len = MathF.Max(1e-4f, points.Max(Along) - min);
            // Grip hangs toward the rear: low points sit behind the middle.
            var gripRear = low.Count > 0 ? 0.5f - (low.Average(Along) - min) / len : 0f;
            // Muzzle end is thin compared with the rear.
            var side = Vector3.Cross(Vector3.UnitZ, f);
            float Spread(Func<float, bool> slice)
            {
                var sel = points.Where(p => slice((Along(p) - min) / len)).ToList();
                return sel.Count == 0 ? 0f : (sel.Max(p => p.Z) - sel.Min(p => p.Z)) + (sel.Max(p => Vector3.Dot(p, side)) - sel.Min(p => Vector3.Dot(p, side)));
            }
            var rear = Spread(u => u < 0.2f);
            var front = Spread(u => u > 0.8f);
            var thinFront = (rear - front) / MathF.Max(1e-3f, MathF.Max(rear, front));
            var score = gripRear * 2f + thinFront;
            if (hints is not null)
                foreach (var (point, sideHint, weight) in hints)
                    if (sideHint < 0f) // triggers/grips sit behind the centre
                        score += (0.5f - (Along(point) - min) / len) * weight;
            scores.Add(score);
            if (score > bestScore)
            {
                bestScore = score;
                best = f;
            }
        }
        var second = scores.OrderByDescending(x => x).Skip(1).First();
        var from = MathQ.FromAxes(best, Vector3.Cross(Vector3.UnitZ, best));
        var confidence = Math.Clamp(0.45f + (bestScore - second) * 0.8f, 0.3f, 0.9f);
        return new OrientationGuess(MathQ.Normalize(Quaternion.Conjugate(from)), confidence, $"compact shape; forward {Describe(best)} from grip and muzzle, up +Z");
    }

    private static string Describe(Vector3 v)
    {
        var abs = Vector3.Abs(v);
        if (abs.X > 0.999f) return v.X > 0 ? "+X" : "-X";
        if (abs.Y > 0.999f) return v.Y > 0 ? "+Y" : "-Y";
        if (abs.Z > 0.999f) return v.Z > 0 ? "+Z" : "-Z";
        return $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
    }

    /// <summary>Eigenvectors of the point covariance, largest spread first (Jacobi iteration).</summary>
    public static (Vector3[] Axes, float[] Spread) PrincipalAxes(IReadOnlyList<Vector3> points)
    {
        var mean = Vector3.Zero;
        foreach (var p in points) mean += p;
        mean /= points.Count;
        var a = new double[3, 3];
        foreach (var p in points)
        {
            var d = p - mean;
            double[] v = { d.X, d.Y, d.Z };
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    a[i, j] += v[i] * v[j];
        }
        var e = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (var sweep = 0; sweep < 32; sweep++)
        {
            for (var pI = 0; pI < 2; pI++)
                for (var q = pI + 1; q < 3; q++)
                {
                    if (Math.Abs(a[pI, q]) < 1e-12)
                        continue;
                    var theta = (a[q, q] - a[pI, pI]) / (2 * a[pI, q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var sn = t * c;
                    for (var k = 0; k < 3; k++)
                    {
                        var akp = a[k, pI]; var akq = a[k, q];
                        a[k, pI] = c * akp - sn * akq; a[k, q] = sn * akp + c * akq;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var apk = a[pI, k]; var aqk = a[q, k];
                        a[pI, k] = c * apk - sn * aqk; a[q, k] = sn * apk + c * aqk;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var ekp = e[k, pI]; var ekq = e[k, q];
                        e[k, pI] = c * ekp - sn * ekq; e[k, q] = sn * ekp + c * ekq;
                    }
                }
        }
        var order = new[] { 0, 1, 2 }.OrderByDescending(i => a[i, i]).ToArray();
        var axes = order.Select(i => Vector3.Normalize(new Vector3((float)e[0, i], (float)e[1, i], (float)e[2, i]))).ToArray();
        var spread = order.Select(i => (float)Math.Sqrt(Math.Max(0, a[i, i]) / points.Count)).ToArray();
        return (axes, spread);
    }

    // ---------------------------------------------------------------- bore

    private static (Vector3 Start, Vector3 End, float Diameter) FindBore(ShapeProfile profile, Bounds bounds)
    {
        // Barrel = the front run of slices with a small, stable cross-section; the bore height is
        // the centre of those slices.
        var last = profile.Count - 1;
        while (last > 0 && !profile.Has(last))
            last--;
        var n = Math.Max(1, (int)(profile.Count * 0.1f));
        var zs = new List<float>();
        var ys = new List<float>();
        var ds = new List<float>();
        for (var i = last; i >= Math.Max(0, last - n); i--)
        {
            if (!profile.Has(i))
                continue;
            zs.Add((profile.Top[i] + profile.Bottom[i]) * 0.5f);
            ys.Add((profile.Left[i] + profile.Right[i]) * 0.5f);
            ds.Add(MathF.Min(profile.Height(i), profile.Width(i)));
        }
        float Median(List<float> v) { if (v.Count == 0) return 0f; v.Sort(); return v[v.Count / 2]; }
        var z = zs.Count > 0 ? Median(zs) : bounds.Center.Z;
        var y = ys.Count > 0 ? Median(ys) : bounds.Center.Y;
        var d = ds.Count > 0 ? Median(ds) : MathF.Min(bounds.Size.Y, bounds.Size.Z) * 0.3f;
        return (new Vector3(bounds.Min.X, y, z), new Vector3(bounds.Max.X, y, z), d);
    }

    // ---------------------------------------------------------------- parts

    private static void DetectParts(WeaponAnalysis a)
    {
        var skeleton = a.Asset.Skeleton;
        var mesh = a.Asset.Mesh;
        var bore = a.BoreStart.Z;

        var candidates = new List<PartDetection>();
        // Bones.
        foreach (var m in a.Motion)
        {
            var name = skeleton[m.Bone].Name;
            var tokens = NameTokens.Split(name);
            var joined = NameTokens.Joined(tokens);
            var center = PartCenter(a, m.Bone) ?? skeleton.RestWorld[m.Bone].Pos;
            foreach (var (kind, aliases, joinedAliases) in PartNames)
            {
                var named = NameTokens.Has(tokens, aliases) || (joinedAliases?.Any(j => joined.Contains(j)) ?? false)
                    || (kind == PartKind.Magazine && IsMagazineName(name));
                if (!named)
                    continue;
                // "clip" is also an animation word; only trust it on a bone that moves in a reload.
                if (kind == PartKind.Magazine && tokens.Contains("clip") && m.MaxTranslation < 1f)
                    continue;
                var conf = 0.75f + MotionAgrees(kind, m, center, bore) * 0.25f;
                candidates.Add(new PartDetection { Kind = kind, Bone = name, Center = center, Confidence = conf, Reason = $"bone '{name}'" + (m.MaxTranslation > 0.1f || m.MaxRotationDeg > 3f ? $", moves in '{m.PeakClip}'" : "") });
            }
        }

        // Unnamed moving bones: classify by how they move.
        foreach (var m in a.Motion)
        {
            var name = skeleton[m.Bone].Name;
            if (candidates.Any(c => c.Bone == name) || m.Bone == a.RootBone)
                continue;
            if (!a.WeaponTriangles.Any(t => mesh.TriangleBone(t) == m.Bone))
                continue;
            var center = PartCenter(a, m.Bone) ?? skeleton.RestWorld[m.Bone].Pos;
            var guess = GuessFromMotion(m, center, bore, a);
            if (guess is { } g)
                candidates.Add(new PartDetection { Kind = g.Kind, Bone = name, Center = center, Confidence = g.Confidence, Reason = g.Reason });
        }

        // Static meshes named after parts (props without bones).
        for (var p = 0; p < mesh.PartNames.Count; p++)
        {
            var tokens = NameTokens.Split(mesh.PartNames[p]);
            var joined = NameTokens.Joined(tokens);
            foreach (var (kind, aliases, joinedAliases) in PartNames)
            {
                if (!NameTokens.Has(tokens, aliases) && !(joinedAliases?.Any(j => joined.Contains(j)) ?? false))
                    continue;
                var tris = a.WeaponTriangles.Where(t => mesh.TrianglePart[t] == p).ToArray();
                if (tris.Length == 0)
                    continue;
                var center = Centroid(mesh, tris);
                candidates.Add(new PartDetection { Kind = kind, Meshes = new[] { mesh.PartNames[p] }, Center = center, Confidence = 0.65f, Reason = $"mesh '{mesh.PartNames[p]}'" });
            }
        }

        // One detection per kind: highest confidence wins, bone-driven preferred over mesh-only.
        foreach (var group in candidates.GroupBy(c => c.Kind))
        {
            var best = group.OrderByDescending(c => c.Confidence + (c.Bone != "" ? 0.05f : 0f)).First();
            var meshes = group.SelectMany(c => c.Meshes).Distinct().ToArray();
            a.Parts.Add(best with { Meshes = meshes.Length > 0 ? meshes : best.Meshes });
        }
    }

    private static Vector3? PartCenter(WeaponAnalysis a, int bone)
    {
        var tris = a.WeaponTriangles.Where(t => a.Asset.Mesh.TriangleBone(t) == bone).ToArray();
        return tris.Length == 0 ? null : Centroid(a.Asset.Mesh, tris);
    }

    public static Vector3 Centroid(TriMesh mesh, IReadOnlyCollection<int> tris)
    {
        var sum = Vector3.Zero;
        var area = 0f;
        foreach (var t in tris)
        {
            var (a, b, c) = mesh.Triangle(t);
            var w = Vector3.Cross(b - a, c - a).Length() * 0.5f + 1e-6f;
            sum += (a + b + c) / 3f * w;
            area += w;
        }
        return area > 0f ? sum / area : Vector3.Zero;
    }

    /// <summary>0..1: how well a bone's motion fits what the part kind does.</summary>
    private static float MotionAgrees(PartKind kind, BoneMotion m, Vector3 center, float bore)
    {
        var alongX = MathF.Abs(m.MainDirection.X);
        return kind switch
        {
            PartKind.Magazine => m.MaxTranslation > 2f ? 1f : m.MaxTranslation > 0.5f ? 0.5f : 0f,
            PartKind.Slide or PartKind.Bolt or PartKind.ChargingHandle or PartKind.Pump => m.MaxTranslation > 0.3f && alongX > 0.7f ? 1f : m.MaxTranslation > 0.1f ? 0.5f : 0f,
            PartKind.Trigger => m.MaxRotationDeg > 3f || (m.MaxTranslation > 0.02f && m.MaxTranslation < 0.5f) ? 1f : 0f,
            PartKind.Hammer => m.MaxRotationDeg > 10f ? 1f : 0f,
            PartKind.Cylinder => m.MaxRotationDeg > 20f ? 1f : 0f,
            _ => 0.5f,
        };
    }

    private static (PartKind Kind, float Confidence, string Reason)? GuessFromMotion(BoneMotion m, Vector3 center, float bore, WeaponAnalysis a)
    {
        var alongX = MathF.Abs(m.MainDirection.X);
        var below = center.Z < bore - a.BoreDiameter;
        var clip = m.PeakClip.ToLowerInvariant();
        if (m.MaxTranslation > 3f && below && clip.Contains("reload"))
            return (PartKind.Magazine, 0.7f, $"leaves the weapon during '{m.PeakClip}'");
        if (m.MaxTranslation > 3f && below)
            return (PartKind.Magazine, 0.5f, "moves far below the bore");
        if (m.MaxTranslation > 0.3f && m.MaxTranslation < 4f && alongX > 0.8f && !below)
        {
            var slideLike = a.Asset.Mesh.TriangleCount > 0 && a.Length < 13f;
            return slideLike
                ? (PartKind.Slide, 0.55f, "slides back along the barrel")
                : (PartKind.Bolt, 0.55f, "slides back along the barrel");
        }
        // A pump sits well ahead of the trigger on a long gun.
        var triggerX = a.Parts.FirstOrDefault(p => p.Kind == PartKind.Trigger)?.Center.X ?? (a.WeaponBounds.Min.X + a.Length * 0.4f);
        if (m.MaxTranslation > 0.5f && alongX > 0.8f && below && a.Length > 20f && center.X > triggerX + 3f)
            return (PartKind.Pump, 0.45f, "slides along under the barrel");
        // A cylinder is a sizeable part around the bore on a short gun.
        if (m.MaxRotationDeg > 25f && a.Length < 16f && MathF.Abs(center.Y - a.BoreStart.Y) < a.BoreDiameter * 2f && m.MaxTranslation < 0.2f && center.Z > bore - a.BoreDiameter * 2f)
            return (PartKind.Cylinder, 0.4f, "rotates around the bore axis");
        if (m.MaxRotationDeg > 3f && m.MaxRotationDeg < 40f && below && m.MaxTranslation < 0.3f)
            return (PartKind.Trigger, 0.4f, "pivots below the receiver");
        return null;
    }

    // ---------------------------------------------------------------- muzzle / eject

    private static readonly string[] MuzzleNames = { "muzzle", "flash", "muzzleflash", "barrelend", "tip", "fx", "shoot", "bullet", "spawn" };
    private static readonly string[] EjectNames = { "eject", "ejection", "shell", "brass", "casing", "ejectionport", "shelleject" };

    private static PointDetection? DetectMuzzle(WeaponAnalysis a)
    {
        var skeleton = a.Asset.Skeleton;
        var rootName = skeleton[a.RootBone].Name;

        foreach (var att in a.Asset.Attachments)
            if (NameTokens.Has(NameTokens.Split(att.Name), MuzzleNames))
            {
                var bone = skeleton.IndexOf(att.Bone);
                var world = bone >= 0 ? XForm.Compose(skeleton.RestWorld[bone], att.Local) : att.Local;
                return new PointDetection { Bone = att.Bone, Local = att.Local, Model = world, Confidence = 0.95f, Reason = $"attachment '{att.Name}'" };
            }

        for (var b = 0; b < skeleton.Count; b++)
        {
            if (a.ArmBones.Contains(b))
                continue;
            var tokens = NameTokens.Split(skeleton[b].Name);
            if (!NameTokens.Has(tokens, "muzzle", "flash", "muzzleflash", "barrelend") && !NameTokens.Joined(tokens).Contains("muzzle"))
                continue;
            var world = new XForm(skeleton.RestWorld[b].Pos, Quaternion.Identity);
            var local = XForm.ToLocal(skeleton.RestWorld[b], world);
            return new PointDetection { Bone = skeleton[b].Name, Local = local, Model = world, Confidence = 0.9f, Reason = $"bone '{skeleton[b].Name}'" };
        }

        if (a.WeaponBvh.IsEmpty)
            return null;
        // Geometry: centre of the bore at the front face, found by casting back from ahead of it.
        var front = a.BoreEnd + Vector3.UnitX * 2f;
        var hit = a.WeaponBvh.Raycast(front, -Vector3.UnitX, a.Length + 4f);
        var point = hit is { } h && h.Point.X > a.WeaponBounds.Max.X - a.Length * 0.08f
            ? new Vector3(a.WeaponBounds.Max.X, a.BoreEnd.Y, a.BoreEnd.Z)
            : a.BoreEnd;
        var model = new XForm(point, Quaternion.Identity);
        var rootWorld = skeleton.RestWorld[a.RootBone];
        return new PointDetection
        {
            Bone = rootName,
            Local = XForm.ToLocal(rootWorld, model),
            Model = model,
            Confidence = a.Type.Type == WeaponType.Melee ? 0.2f : 0.6f,
            Reason = "front of the barrel",
        };
    }

    private static PointDetection? DetectEject(WeaponAnalysis a)
    {
        var skeleton = a.Asset.Skeleton;
        foreach (var att in a.Asset.Attachments)
            if (NameTokens.Has(NameTokens.Split(att.Name), EjectNames))
            {
                var bone = skeleton.IndexOf(att.Bone);
                var world = bone >= 0 ? XForm.Compose(skeleton.RestWorld[bone], att.Local) : att.Local;
                return new PointDetection { Bone = att.Bone, Local = att.Local, Model = world, Confidence = 0.95f, Reason = $"attachment '{att.Name}'" };
            }

        for (var b = 0; b < skeleton.Count; b++)
        {
            if (a.ArmBones.Contains(b))
                continue;
            if (!NameTokens.Has(NameTokens.Split(skeleton[b].Name), EjectNames))
                continue;
            var world = new XForm(skeleton.RestWorld[b].Pos, EjectRotation);
            return new PointDetection { Bone = skeleton[b].Name, Local = XForm.ToLocal(skeleton.RestWorld[b], world), Model = world, Confidence = 0.9f, Reason = $"bone '{skeleton[b].Name}'" };
        }

        if (a.WeaponBvh.IsEmpty)
            return null;
        // Geometry: the port sits on the right (-Y) of the receiver, over the magazine or just
        // ahead of the grip, at bore height.
        var mag = a.Part(PartKind.Magazine);
        var trigger = a.Part(PartKind.Trigger);
        var slideOrBolt = a.Part(PartKind.Slide) ?? a.Part(PartKind.Bolt);
        var x = mag?.Center.X
            ?? (trigger is { } tr ? tr.Center.X + 1.2f : a.WeaponBounds.Min.X + a.Length * 0.45f);
        if (slideOrBolt is { } sb && mag is null)
            x = sb.Center.X;
        var z = a.BoreStart.Z + a.BoreDiameter * 0.25f;
        var from = new Vector3(x, a.WeaponBounds.Min.Y - 2f, z);
        var hit = a.WeaponBvh.Raycast(from, Vector3.UnitY, a.WeaponBounds.Size.Y + 4f);
        if (hit is null)
            return null;
        var model = new XForm(hit.Value.Point, EjectRotation);
        var rootWorld = skeleton.RestWorld[a.RootBone];
        return new PointDetection
        {
            Bone = skeleton[a.RootBone].Name,
            Local = XForm.ToLocal(rootWorld, model),
            Model = model,
            Confidence = mag is not null || slideOrBolt is not null ? 0.55f : 0.35f,
            Reason = "right side of the receiver",
        };
    }

    /// <summary>Shells leave to the right, slightly up and back.</summary>
    public static readonly Quaternion EjectRotation = MathQ.FromAxes(Vector3.Normalize(new Vector3(-0.25f, -1f, 0.45f)), Vector3.UnitZ);

    // ---------------------------------------------------------------- animations

    private static void ClassifyAnimations(WeaponAnalysis a)
    {
        var firearm = WeaponTypes.IsFirearm(a.Type.Type);
        var mag = a.Part(PartKind.Magazine);
        var bolt = a.Part(PartKind.Slide) ?? a.Part(PartKind.Bolt) ?? a.Part(PartKind.Pump);
        var skeleton = a.Asset.Skeleton;
        var magBone = mag is null ? -1 : skeleton.IndexOf(mag.Bone);
        var boltBone = bolt is null ? -1 : skeleton.IndexOf(bolt.Bone);
        var ammo = AmmoBones(a);
        // Takes split into actions: their parts are used, not the whole take.
        bool Split(Clip clip) => a.Asset.FindClip(TakeSplitter.PartName(clip.Name, 0)) is not null;
        // One action of such a take ("Take 001 3" of "Take 001").
        bool Part(Clip clip)
        {
            var space = clip.Name.LastIndexOf(' ');
            return space > 0 && int.TryParse(clip.Name[(space + 1)..], out _) && a.Asset.FindClip(clip.Name[..space]) is not null;
        }

        foreach (var clip in a.Asset.Clips)
        {
            var hint = new AnimationMotionHint(
                clip.Duration,
                magBone >= 0 ? Travel(clip, magBone, skeleton) : 0f,
                boltBone >= 0 ? Travel(clip, boltBone, skeleton) : 0f,
                a.RootBone >= 0 ? Travel(clip, a.RootBone, skeleton) : 0f,
                a.Motion.Count == 0 ? 0f : a.Motion.Max(m => m.MaxTranslation));
            var guess = AnimationClassifier.Classify(clip.Name, hint);
            // Parts of a split take carry no telling name: judge them by how the hands move.
            if (clip.Name.EndsWith(" start pose", StringComparison.Ordinal))
                guess = guess with { Role = AnimationRole.Unknown, Confidence = 0f, Reason = "still first frame of a take" };
            else if (firearm && Split(clip))
                guess = guess with { Role = AnimationRole.Unknown, Confidence = 0f, Reason = "holds several actions (its parts are used)" };
            else if (firearm && Part(clip) && (guess.Role is AnimationRole.Unknown or AnimationRole.Bolt || guess.Confidence < 0.6f))
            {
                // A firearm's actions in one nameless take: the rounds coming out and going in
                // make a reload, a short kick that settles back is a shot.
                if (clip.Duration >= 0.9f && ammo.Count > 0 && ammo.Max(b => TravelFromWeapon(a, clip, b)) > 1f)
                    guess = guess with { Role = AnimationRole.Reload, Confidence = 0.5f, Reason = "the rounds come out and go in" };
                else if (clip.Duration <= 0.8f && !clip.Looping && HandPeakSpeed(a, clip) > 25f && Kick(a, clip) is > 1f and < 10f)
                    guess = guess with { Role = AnimationRole.Fire, Confidence = 0.45f, Reason = "a short kick that settles back" };
                else if (clip.Looping)
                    guess = guess with { Role = AnimationRole.Idle, Confidence = 0.55f, Reason = "the hands rest" };
            }
            else if (guess.Role == AnimationRole.Unknown || (guess.Role == AnimationRole.Idle && guess.Confidence < 0.6f && !clip.Looping))
            {
                if (clip.Looping)
                    guess = guess with { Role = AnimationRole.Idle, Confidence = 0.55f, Reason = "the hands rest" };
                else if (a.Type.Type is WeaponType.Melee or WeaponType.Unarmed && clip.Duration < 2.2f && HandPeakSpeed(a, clip) > 60f)
                    guess = guess with { Role = AnimationRole.Fire, Confidence = 0.5f, Reason = "a fast swing of the hands" };
            }
            a.Animations.Add(guess);
        }
        // Roles this kind of weapon doesn't have ("reload" for a syringe): the clip is its use instead.
        for (var i = 0; i < a.Animations.Count; i++)
        {
            var g = a.Animations[i];
            if (g.Role == AnimationRole.Unknown || AnimationRoles.AppliesTo(g.Role, a.Type.Type))
                continue;
            var replacement = a.Type.Type == WeaponType.Item && !a.Asset.FindClip(g.Animation)!.Looping ? AnimationRole.Use
                : g.Role == AnimationRole.Melee ? AnimationRole.Fire : AnimationRole.Unknown;
            a.Animations[i] = g with { Role = replacement, Confidence = g.Confidence * 0.8f, Reason = replacement == AnimationRole.Unknown ? g.Reason : $"{g.Reason} (as {AnimationRoles.Label(replacement, a.Type.Type).ToLowerInvariant()})" };
        }
        foreach (var kv in AnimationClassifier.Assign(a.Animations, AnimationPerspective.FirstPerson))
            a.AssignedAnimations[kv.Key] = kv.Value;

        // An item's one action: the longest take that isn't a rest is its use.
        if (a.Type.Type == WeaponType.Item && !a.AssignedAnimations.ContainsKey(AnimationRole.Use)
            && a.Asset.Clips.Where(c => !c.Looping && !a.AssignedAnimations.Values.Any(v => v.Animation == c.Name)).OrderByDescending(c => c.Duration).FirstOrDefault() is { Duration: > 0.5f } use)
            a.AssignedAnimations[AnimationRole.Use] = new AnimationGuess(use.Name, AnimationRole.Use, 0.45f, "the item's longest action");

        // A take holding several actions (draw, drink, holster...): the use is its longest action
        // part, not the whole take (the rests between actions keep the idle).
        if (a.Type.Type == WeaponType.Item && a.AssignedAnimations.TryGetValue(AnimationRole.Use, out var takeUse))
        {
            var parts = a.Asset.Clips.Where(c => c.Name.StartsWith(takeUse.Animation + " ", StringComparison.Ordinal)
                && int.TryParse(c.Name[(takeUse.Animation.Length + 1)..], out _)).ToList();
            if (parts.Count >= 4 && parts.Where(c => !c.Looping).OrderByDescending(c => c.Duration).FirstOrDefault() is { } longest)
                a.AssignedAnimations[AnimationRole.Use] = new AnimationGuess(longest.Name, AnimationRole.Use, 0.45f, $"the longest action in {takeUse.Animation}");
        }

        // An item used in one long take (inject, drink): its idle is the pose the use starts from,
        // not a pause in the middle of the use.
        if (a.Type.Type == WeaponType.Item && a.AssignedAnimations.TryGetValue(AnimationRole.Idle, out var restIdle) && restIdle.Reason == "the hands rest"
            && a.AssignedAnimations.TryGetValue(AnimationRole.Use, out var wholeUse) && a.Asset.FindClip(TakeSplitter.StartPoseName(wholeUse.Animation)) is { } startPose)
            a.AssignedAnimations[AnimationRole.Idle] = new AnimationGuess(startPose.Name, AnimationRole.Idle, 0.5f, "the pose the use starts from");


        if (!firearm)
        {
            // Melee weapons, fists and items attack with the attack button: punches and slashes are attacks.
            var attacks = a.Animations.Where(g => g.Role is AnimationRole.Fire or AnimationRole.Melee && g.Confidence >= 0.45f)
                .OrderByDescending(g => g.Confidence).ThenBy(g => g.Animation, StringComparer.OrdinalIgnoreCase).ToList();
            if (!a.AssignedAnimations.ContainsKey(AnimationRole.Fire) && attacks.Count > 0)
                a.AssignedAnimations[AnimationRole.Fire] = attacks[0] with { Role = AnimationRole.Fire };
            a.AssignedAnimations.Remove(AnimationRole.Melee);
            if (a.AssignedAnimations.TryGetValue(AnimationRole.Fire, out var main))
            {
                // Packs repeat the same action in several takes: a copy is not another attack.
                var mainClip = a.Asset.FindClip(main.Animation);
                var more = attacks.Select(g => g.Animation).Where(n => n != main.Animation).Distinct()
                    .Where(n => mainClip is null || a.Asset.FindClip(n) is not { } other || !SameMotion(a.Asset.Skeleton, mainClip, other))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                if (more.Count > 0)
                    a.AnimationVariants[AnimationRole.Fire] = more;
            }
        }
        // Several shots in one take (a dual weapon firing each gun): played in turn.
        if (firearm && a.AssignedAnimations.TryGetValue(AnimationRole.Fire, out var shot) && shot.Reason == "a short kick that settles back")
        {
            var more = a.Animations.Where(g => g.Role == AnimationRole.Fire && g.Reason == shot.Reason && g.Animation != shot.Animation).Select(g => g.Animation).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (more.Count > 0)
                a.AnimationVariants[AnimationRole.Fire] = more;
        }
        if (a.AssignedAnimations.TryGetValue(AnimationRole.Attack2, out var heavy))
        {
            var more = a.Animations.Where(g => g.Role == AnimationRole.Attack2 && g.Animation != heavy.Animation).Select(g => g.Animation).Distinct().ToList();
            if (more.Count > 0)
                a.AnimationVariants[AnimationRole.Attack2] = more;
        }
    }

    private static readonly string[] AmmoNames = { "shell", "shells", "slug", "slugs", "round", "rounds", "bullet", "bullets", "cartridge", "cartridges", "casing", "ammo", "mag", "magazine", "clip" };

    /// <summary>Weapon bones that are rounds or a magazine (by name).</summary>
    private static List<int> AmmoBones(WeaponAnalysis a)
    {
        var skeleton = a.Asset.Skeleton;
        var bones = new List<int>();
        for (var i = 0; i < skeleton.Count; i++)
            if (!a.ArmBones.Contains(i) && NameTokens.Has(NameTokens.Split(skeleton[i].Name), AmmoNames))
                bones.Add(i);
        return bones;
    }

    /// <summary>How far a bone moves relative to the weapon root during a clip (inches).</summary>
    private static float TravelFromWeapon(WeaponAnalysis a, Clip clip, int bone)
    {
        var skeleton = a.Asset.Skeleton;
        var root = a.RootBone >= 0 ? a.RootBone : 0;
        Vector3? first = null;
        var max = 0f;
        foreach (var frame in clip.Frames)
        {
            var world = new Pose(frame).ToWorld(skeleton);
            var local = XForm.ToLocal(world[root], world[bone]).Pos;
            first ??= local;
            max = MathF.Max(max, Vector3.Distance(local, first.Value));
        }
        return max;
    }

    /// <summary>
    /// How far the weapon kicks in a clip (inches): the most either copy's root moves from where
    /// it starts. A shot kicks a few inches; a draw or a flourish sweeps much further.
    /// </summary>
    private static float Kick(WeaponAnalysis a, Clip clip)
    {
        var skeleton = a.Asset.Skeleton;
        var bones = new List<int> { a.RootBone >= 0 ? a.RootBone : 0 };
        if (a.SecondWeaponBone is { } second && skeleton.IndexOf(second) is var s and >= 0)
            bones.Add(s);
        var start = new Pose(clip.Frames[0]).ToWorld(skeleton);
        var kick = 0f;
        foreach (var frame in clip.Frames)
        {
            var world = new Pose(frame).ToWorld(skeleton);
            foreach (var b in bones)
                kick = MathF.Max(kick, Vector3.Distance(world[b].Pos, start[b].Pos));
        }
        return kick;
    }

    /// <summary>Two clips play the same motion (the same take copied under another name).</summary>
    private static bool SameMotion(Skeleton skeleton, Clip x, Clip y)
    {
        if (Math.Abs(x.FrameCount - y.FrameCount) > 1)
            return false;
        var n = Math.Min(x.FrameCount, y.FrameCount);
        for (var f = 0; f < n; f += Math.Max(1, n / 8))
        {
            var wx = new Pose(x.Frames[f]).ToWorld(skeleton);
            var wy = new Pose(y.Frames[f]).ToWorld(skeleton);
            for (var b = 0; b < skeleton.Count; b++)
                if (Vector3.Distance(wx[b].Pos, wy[b].Pos) > 0.25f)
                    return false;
        }
        return true;
    }

    /// <summary>Fastest any arm bone (else any bone) moves during a clip, inches per second.</summary>
    private static float HandPeakSpeed(WeaponAnalysis a, Clip clip)
    {
        var skeleton = a.Asset.Skeleton;
        var bones = a.ArmBones.Count > 0 ? a.ArmBones.ToArray() : Enumerable.Range(0, skeleton.Count).ToArray();
        var peak = 0f;
        XForm[]? prev = null;
        foreach (var frame in clip.Frames)
        {
            var world = new Pose(frame).ToWorld(skeleton);
            if (prev is not null)
                foreach (var b in bones)
                    peak = MathF.Max(peak, Vector3.Distance(world[b].Pos, prev[b].Pos) * clip.Fps);
            prev = world;
        }
        return peak;
    }

    private static float Travel(Clip clip, int bone, Skeleton skeleton)
    {
        var rest = skeleton[bone].RestLocal.Pos;
        var max = 0f;
        foreach (var frame in clip.Frames)
            max = MathF.Max(max, Vector3.Distance(frame[bone].Pos, rest));
        return max;
    }
}
