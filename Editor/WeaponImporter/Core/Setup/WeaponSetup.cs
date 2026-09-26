#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Everything the importer decided or the user changed for one weapon, saved next to the
/// generated assets (<c>&lt;name&gt;.weapon.json</c>) so reimports and templates keep every choice.
/// Positions are in canonical weapon space (inches, muzzle +X, up +Z) unless noted.
/// </summary>
public sealed class WeaponSetup
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "weapon";
    public string Source { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string OutputFolder { get; set; } = "";

    public WeaponType Type { get; set; } = WeaponType.Custom;
    public bool TypeManual { get; set; }

    /// <summary>
    /// The character's hold animation set (citizen holdtype: 1 pistol, 2 rifle, 3 shotgun,
    /// 4 item, 6 melee, 7 rpg); -1 = the weapon type's default. Chosen automatically as the
    /// hold the weapon fits best unless <see cref="HoldTypeManual"/>.
    /// </summary>
    public int HoldType { get; set; } = -1;
    public bool HoldTypeManual { get; set; }

    [JsonIgnore] public int EffectiveHoldType => HoldType >= 0 ? HoldType : WeaponTypes.HoldType(Type);

    /// <summary>Model space -> canonical rotation, and model units -> inches.</summary>
    public float[] ModelRotation { get; set; } = { 0, 0, 0, 1 };
    public float Scale { get; set; } = 1f;
    public bool OrientationManual { get; set; }
    public string ReferenceClip { get; set; } = "";
    public string RootBone { get; set; } = "";

    /// <summary>Attach Textures: use images found beside the model for material slots the file left empty.</summary>
    public bool AttachTextures { get; set; } = true;

    /// <summary>Texture decisions per "material|slot": the chosen image path, or "" to leave the slot empty.</summary>
    public Dictionary<string, string> TextureChoices { get; set; } = new();

    public List<PartSetup> Parts { get; set; } = new();
    public PointSetup? Muzzle { get; set; }
    public PointSetup? Eject { get; set; }

    public GripSetup Primary { get; set; } = new();
    public GripSetup? Support { get; set; }
    public bool UseSupportHand { get; set; } = true;
    public bool IndexOnTrigger { get; set; } = true;

    /// <summary>Weapon's own clips (first-person / weapon-part animation) per role.</summary>
    public Dictionary<AnimationRole, AnimationBinding> WeaponAnimations { get; set; } = new();

    /// <summary>What the character plays for each role in third person.</summary>
    public Dictionary<AnimationRole, CharacterAnimation> ThirdPerson { get; set; } = new();

    /// <summary>
    /// How long the character's own action runs (measured through its animgraph) for roles it
    /// triggers itself; third-person actions last this long so hands and body stay in step.
    /// </summary>
    public Dictionary<AnimationRole, float> ActionSeconds { get; set; } = new();

    /// <summary>
    /// Character model and holdtype <see cref="ActionSeconds"/> were measured for. While they
    /// match, a re-import reuses the measurement (the animgraph's random idle layers make each
    /// measurement differ by a frame or so).
    /// </summary>
    public string ActionSecondsKey { get; set; } = "";

    /// <summary>
    /// Also bake the first-person viewmodel (<c>&lt;name&gt;_fp.vmdl</c>: the file's own arms,
    /// weapon and camera with every animation as authored) when the file has first-person arms.
    /// </summary>
    public bool ExportFirstPerson { get; set; } = true;

    /// <summary>
    /// First-person arms from another file (packs that ship one arms model for all their weapons):
    /// "" finds them automatically, <see cref="NoArms"/> uses none, else the arms model's path.
    /// </summary>
    public string ArmsSource { get; set; } = "";

    public const string NoArms = "none";

    /// <summary>
    /// Takes split into actions by hand: take name -> the frames where each action starts (0 is
    /// implied). An empty list keeps the take whole. Takes not listed are split automatically.
    /// </summary>
    public Dictionary<string, List<int>> TakeSplits { get; set; } = new();

    /// <summary>Largest texture side written by the bake (bigger images are scaled down); 0 keeps them as they are.</summary>
    public int MaxTextureSize { get; set; } = 2048;

    /// <summary>
    /// Fingerprints of the editable files the last bake wrote (graphs, prefab). A file that no
    /// longer matches was edited by hand and is kept on the next bake.
    /// </summary>
    public Dictionary<string, string> GeneratedHashes { get; set; } = new();

    /// <summary>
    /// Third-person reloads move the support hand onto this weapon's magazine where the
    /// character's reload works its own (off by default: the hand follows the animation).
    /// </summary>
    public bool ReloadTouchesMagazine { get; set; }

    /// <summary>Bake also writes the corrected third-person animations (new clips; the originals are kept).</summary>
    public bool BakeCorrectedAnimations { get; set; } = true;

    /// <summary>The prefab plays the baked corrected clips instead of correcting the live animation.</summary>
    public bool UseCorrectedAnimations { get; set; }

    /// <summary>Model holding the baked corrected clips, and the clip per role (filled by Bake).</summary>
    public string CorrectedModel { get; set; } = "";
    public Dictionary<AnimationRole, string> CorrectedClips { get; set; } = new();

    /// <summary>
    /// Grips generated for this weapon by a learned grasp model (GrabNet), kept with the setup so
    /// the import reproduces: they are tied to this weapon's geometry.
    /// </summary>
    public List<Grip.GripPreset> LearnedGrips { get; set; } = new();

    /// <summary>Hand contact over time, per role.</summary>
    public Dictionary<AnimationRole, ContactTrack> Contacts { get; set; } = new();

    public List<WeaponEvent> Events { get; set; } = new();

    public IkSettings Ik { get; set; } = new();

    /// <summary>Character the grips are fitted to: "human", "citizen" or a model path.</summary>
    public string Character { get; set; } = "human";

    /// <summary>Weapon used as a template, if any.</summary>
    public string Template { get; set; } = "";

    /// <summary>Solved grip, filled by the bake.</summary>
    public BakedGrip? Baked { get; set; }

    // ------------------------------------------------------------------ helpers

    [JsonIgnore]
    public Quaternion ModelRotationQ
    {
        get => new(ModelRotation[0], ModelRotation[1], ModelRotation[2], ModelRotation[3]);
        set => ModelRotation = new[] { value.X, value.Y, value.Z, value.W };
    }

    public PartSetup? Part(PartKind kind) => Parts.FirstOrDefault(p => p.Kind == kind);

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static WeaponSetup FromJson(string json)
    {
        WeaponSetup? setup;
        try
        {
            setup = JsonSerializer.Deserialize<WeaponSetup>(json, Json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Weapon setup file is not valid JSON: {e.Message}", e);
        }
        if (setup is null)
            throw new FormatException("Weapon setup file is empty.");
        if (setup.Version > CurrentVersion)
            throw new FormatException($"Weapon setup version {setup.Version} is newer than this importer ({CurrentVersion}).");
        setup.Parts ??= new();
        setup.WeaponAnimations ??= new();
        setup.ThirdPerson ??= new();
        setup.Contacts ??= new();
        setup.ActionSeconds ??= new();
        setup.TextureChoices ??= new();
        setup.CorrectedClips ??= new();
        setup.Events ??= new();
        setup.Ik ??= new();
        setup.Primary ??= new();
        if (setup.ModelRotation is not { Length: 4 })
            setup.ModelRotation = new float[] { 0, 0, 0, 1 };
        return setup;
    }
}

public sealed class PartSetup
{
    public PartKind Kind { get; set; }
    public string Bone { get; set; } = "";
    public List<string> Meshes { get; set; } = new();
    public float Confidence { get; set; }
    public bool Manual { get; set; }
    public float[] Center { get; set; } = new float[3];
}

/// <summary>A point with a direction attached to a weapon bone (bone-local, model units).</summary>
public sealed class PointSetup
{
    public string Bone { get; set; } = "";
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = { 0, 0, 0, 1 };
    /// <summary>Same point in canonical space (for display).</summary>
    public float[] Canonical { get; set; } = new float[3];
    public float Confidence { get; set; }
    public bool Manual { get; set; }

    [JsonIgnore] public XForm Local => new(V.Of(Position), V.Q(Rotation));
}

public sealed class GripSetup
{
    public GripStyle Style { get; set; } = GripStyle.Wrap;
    public float[] Contact { get; set; } = new float[3];
    public float[] Normal { get; set; } = { 0, 0, -1 };
    public float[] Axis { get; set; } = { 1, 0, 0 };
    public float Confidence { get; set; }
    public string Reason { get; set; } = "";
    public bool Manual { get; set; }

    /// <summary>Hand pose edited by the user, replacing the generated fit.</summary>
    public HandPoseSetup? Pose { get; set; }

    /// <summary>
    /// Grip to use from the library ("" = automatic: every library grip and the procedural fit
    /// compete, the best fit wins; "Procedural" = only the procedural fit).
    /// </summary>
    public string Preset { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

/// <summary>Serializable <see cref="HandPose"/>.</summary>
public sealed class HandPoseSetup
{
    public float[] WristPosition { get; set; } = new float[3];
    public float[] WristRotation { get; set; } = { 0, 0, 0, 1 };
    public Dictionary<FingerKind, float[]> Flex { get; set; } = new();
    public Dictionary<FingerKind, float> Spread { get; set; } = new();
    public float ThumbOpposition { get; set; }

    public static HandPoseSetup From(HandPose pose) => new()
    {
        WristPosition = V.A(pose.Wrist.Pos),
        WristRotation = V.A(pose.Wrist.Rot),
        Flex = pose.Flex.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
        Spread = pose.Spread.ToDictionary(kv => kv.Key, kv => kv.Value),
        ThumbOpposition = pose.ThumbOpposition,
    };

    public HandPose ToPose()
    {
        var pose = new HandPose(new XForm(V.Of(WristPosition), V.Q(WristRotation))) { ThumbOpposition = ThumbOpposition };
        foreach (var (k, v) in Flex) pose.Flex[k] = v.ToArray();
        foreach (var (k, v) in Spread) pose.Spread[k] = v;
        return pose;
    }
}

public sealed class AnimationBinding
{
    public string Clip { get; set; } = "";

    /// <summary>More clips played in turn with <see cref="Clip"/> (left and right punches, a combo of slashes).</summary>
    public List<string> Variants { get; set; } = new();
    public float Confidence { get; set; }
    public bool Manual { get; set; }
}

public enum CharacterAnimationSource
{
    /// <summary>The character's own animgraph action (holdtype + trigger parameter).</summary>
    Graph,
    /// <summary>A specific sequence from a model (the weapon's retargeted clips or any project model).</summary>
    Sequence,
    /// <summary>Nothing plays for this role.</summary>
    None,
}

public sealed class CharacterAnimation
{
    public CharacterAnimationSource Source { get; set; } = CharacterAnimationSource.Graph;
    public string Model { get; set; } = "";
    public string Sequence { get; set; } = "";
    public bool Manual { get; set; }

    public string Describe() => Source switch
    {
        CharacterAnimationSource.Graph => "Character animgraph",
        CharacterAnimationSource.Sequence => string.IsNullOrEmpty(Sequence) ? "(no sequence)" : Sequence,
        _ => "None",
    };
}

public enum ContactState { Locked, Released }

/// <summary>A change of hand contact at a moment of an animation (normalized time 0..1).</summary>
public sealed class ContactKey
{
    public Side Hand { get; set; } = Side.Left;
    public float Time { get; set; }
    public ContactState State { get; set; } = ContactState.Released;
    /// <summary>Blend time into the new state, seconds.</summary>
    public float Blend { get; set; } = 0.15f;
    /// <summary>Optional adjustment of the hand target while locked (weapon space).</summary>
    public float[]? Offset { get; set; }
    /// <summary>Optional weapon bone the hand follows while locked (magazine during a reload).</summary>
    public string Follow { get; set; } = "";

    /// <summary>What a lock holds, for display: "grip", "magazine" or "" (the grip).</summary>
    public string Target { get; set; } = "";
}

public sealed class ContactTrack
{
    public List<ContactKey> Keys { get; set; } = new();

    /// <summary>Planned by the importer (re-planned when the grip changes); false once the user edits it.</summary>
    public bool Generated { get; set; }

    /// <summary>Why the importer planned it this way (shown in the timeline tooltip).</summary>
    public string Note { get; set; } = "";

    /// <summary>Contact weight (0 released, 1 locked) of a hand at normalized time t.</summary>
    public float Weight(Side hand, float t, float durationSeconds)
    {
        var keys = Keys.Where(k => k.Hand == hand).OrderBy(k => k.Time).ToList();
        var weight = 1f;
        foreach (var key in keys)
        {
            if (key.Time > t)
                break;
            var target = key.State == ContactState.Locked ? 1f : 0f;
            var blendNorm = durationSeconds > 1e-3f ? key.Blend / durationSeconds : 0f;
            var u = blendNorm <= 1e-5f ? 1f : Math.Clamp((t - key.Time) / blendNorm, 0f, 1f);
            u = u * u * (3f - 2f * u); // smoothstep
            weight += (target - weight) * u;
        }
        return Math.Clamp(weight, 0f, 1f);
    }
}

public enum WeaponEventKind { Fire, MuzzleFlash, ShellEject, MagazineDetach, MagazineInsert, BoltPull, BoltRelease, ReloadComplete }

public sealed class WeaponEvent
{
    public AnimationRole Role { get; set; }
    public WeaponEventKind Kind { get; set; }
    /// <summary>Normalized time 0..1 in the role's clip.</summary>
    public float Time { get; set; }
    public bool Manual { get; set; }

    public static string EngineName(WeaponEventKind kind) => kind switch
    {
        WeaponEventKind.Fire => "weapon_fire",
        WeaponEventKind.MuzzleFlash => "muzzle_flash",
        WeaponEventKind.ShellEject => "shell_eject",
        WeaponEventKind.MagazineDetach => "magazine_detach",
        WeaponEventKind.MagazineInsert => "magazine_insert",
        WeaponEventKind.BoltPull => "bolt_pull",
        WeaponEventKind.BoltRelease => "bolt_release",
        WeaponEventKind.ReloadComplete => "reload_complete",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static string Label(WeaponEventKind kind) => kind switch
    {
        WeaponEventKind.MuzzleFlash => "Muzzle flash",
        WeaponEventKind.ShellEject => "Shell eject",
        WeaponEventKind.MagazineDetach => "Magazine detach",
        WeaponEventKind.MagazineInsert => "Magazine insert",
        WeaponEventKind.BoltPull => "Bolt pull",
        WeaponEventKind.BoltRelease => "Bolt release",
        WeaponEventKind.ReloadComplete => "Reload complete",
        _ => kind.ToString(),
    };
}

public sealed class IkSettings
{
    /// <summary>0 = aim exactly along the character's forward, 1 = keep the animated wrist.</summary>
    public float WristPreference { get; set; } = 0.35f;
    /// <summary>Weapon nudge in the hand (canonical space).</summary>
    public float[] WeaponOffsetPosition { get; set; } = new float[3];
    public float[] WeaponOffsetRotation { get; set; } = { 0, 0, 0, 1 };
    /// <summary>Temporal smoothing of hand corrections at runtime (0 = none).</summary>
    public float Smoothing { get; set; } = 0.35f;
    /// <summary>How far past arm length the support hand may be pulled before it lets go.</summary>
    public float ReachSlack { get; set; } = 1.5f;

    [JsonIgnore] public XForm WeaponOffset => new(V.Of(WeaponOffsetPosition), V.Q(WeaponOffsetRotation));
}

/// <summary>Result of the grip solve in the form the runtime component consumes.</summary>
public sealed class BakedGrip
{
    public string Character { get; set; } = "";
    public string HoldBone { get; set; } = "hold_R";
    /// <summary>Weapon model (as compiled) relative to the hold bone.</summary>
    public float[] WeaponInHold { get; set; } = new float[7];
    /// <summary>Hand bones in canonical weapon space.</summary>
    public float[] RightHand { get; set; } = new float[7];
    public float[]? LeftHand { get; set; }
    /// <summary>Finger local rotations: bone name -> quaternion.</summary>
    public Dictionary<string, float[]> RightFingers { get; set; } = new();
    public Dictionary<string, float[]> LeftFingers { get; set; } = new();
    /// <summary>Elbow positions of the fitted reference pose, weapon space (IK hints).</summary>
    public float[]? RightElbow { get; set; }
    public float[]? LeftElbow { get; set; }
    public string Quality { get; set; } = "";

    /// <summary>Largest change treated as sampling noise for hand, weapon and finger values.</summary>
    public const float PoseNoise = 1e-3f;

    /// <summary>Largest change treated as sampling noise for the elbow hints (inches).</summary>
    public const float ElbowNoise = 0.5f;

    /// <summary>
    /// The character's animgraph has random idle layers, so poses sampled from it differ slightly
    /// between sessions. Values that moved less than that noise keep their previous value, so
    /// re-importing and re-baking an unchanged weapon writes identical files; real edits (far
    /// larger) come through unchanged.
    /// </summary>
    public BakedGrip SettledOn(BakedGrip? previous)
    {
        if (previous is null || previous.Character != Character || previous.HoldBone != HoldBone)
            return this;
        WeaponInHold = Keep(WeaponInHold, previous.WeaponInHold, PoseNoise)!;
        RightHand = Keep(RightHand, previous.RightHand, PoseNoise)!;
        LeftHand = Keep(LeftHand, previous.LeftHand, PoseNoise);
        RightElbow = Keep(RightElbow, previous.RightElbow, ElbowNoise);
        LeftElbow = Keep(LeftElbow, previous.LeftElbow, ElbowNoise);
        foreach (var name in RightFingers.Keys.ToList())
            if (previous.RightFingers.TryGetValue(name, out var old))
                RightFingers[name] = Keep(RightFingers[name], old, PoseNoise)!;
        foreach (var name in LeftFingers.Keys.ToList())
            if (previous.LeftFingers.TryGetValue(name, out var old))
                LeftFingers[name] = Keep(LeftFingers[name], old, PoseNoise)!;
        if (RightHand == previous.RightHand && LeftHand == previous.LeftHand)
            Quality = previous.Quality;
        return this;
    }

    private static float[]? Keep(float[]? next, float[]? previous, float tolerance)
    {
        if (next is null || previous is null || next.Length != previous.Length)
            return next;
        for (var i = 0; i < next.Length; i++)
            if (MathF.Abs(next[i] - previous[i]) > tolerance)
                return next;
        return previous;
    }
}

/// <summary>Array conversions for the serializable types.</summary>
public static class V
{
    public static float[] A(Vector3 v) => new[] { v.X, v.Y, v.Z };
    public static float[] A(Quaternion q) => new[] { q.X, q.Y, q.Z, q.W };
    public static float[] A(XForm x) => new[] { x.Pos.X, x.Pos.Y, x.Pos.Z, x.Rot.X, x.Rot.Y, x.Rot.Z, x.Rot.W };
    public static Vector3 Of(float[]? a) => a is { Length: >= 3 } ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
    public static Quaternion Q(float[]? a) => a is { Length: >= 4 } ? MathQ.Normalize(new Quaternion(a[0], a[1], a[2], a[3])) : Quaternion.Identity;
    public static XForm X(float[]? a) => a is { Length: >= 7 } ? new XForm(new Vector3(a[0], a[1], a[2]), MathQ.Normalize(new Quaternion(a[3], a[4], a[5], a[6]))) : XForm.Identity;
}
