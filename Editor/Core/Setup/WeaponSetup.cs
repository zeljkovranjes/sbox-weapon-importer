#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

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
    /// How the character holds and uses the weapon with the stock third-person animations (when
    /// they are installed); picked from the weapon unless <see cref="StockStyleManual"/>.
    /// </summary>
    public StockStyle StockStyle { get; set; }
    public bool StockStyleManual { get; set; }

    /// <summary>One weapon in each hand (the second copy follows the left hand).</summary>
    public bool Dual { get; set; }
    public bool DualManual { get; set; }

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
        foreach (var tp in setup.ThirdPerson.Values)
            tp.Variants ??= new();
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
