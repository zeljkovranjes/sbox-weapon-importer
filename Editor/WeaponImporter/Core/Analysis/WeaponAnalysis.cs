#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

public enum GripStyle
{
    /// <summary>Fingers wrap a vertical handle: pistol grip, vertical foregrip.</summary>
    Wrap,
    /// <summary>Palm under a horizontal handguard, fingers up the side.</summary>
    Cradle,
    /// <summary>Hand around a shotgun pump / forend.</summary>
    Pump,
    /// <summary>Support hand cupping the primary hand on a pistol.</summary>
    Overlay,
    /// <summary>Melee handle held in a fist.</summary>
    Handle,
}

/// <summary>A place on the weapon a hand should hold.</summary>
public sealed record GripCandidate
{
    public required GripSurface Surface { get; init; }
    public required GripStyle Style { get; init; }
    public float Confidence { get; init; }
    public string Reason { get; init; } = "";
    public bool Manual { get; init; }
}

public sealed record TypeGuess(WeaponType Type, float Confidence, string Reason, IReadOnlyDictionary<WeaponType, float> Scores);

/// <summary>Overrides for the analyzer, set when the user corrects orientation or scale.</summary>
public sealed record AnalyzeOptions
{
    public Quaternion? Rotation { get; init; }
    public float? Scale { get; init; }

    /// <summary>Clip whose first frame is treated as the assembled weapon.</summary>
    public string? ReferenceClip { get; init; }
}

public sealed record OrientationGuess(Quaternion ToCanonical, float Confidence, string Reason)
{
    public bool NeedsFix => MathF.Abs(ToCanonical.W) < 0.9999f;
}

/// <summary>Per-bone motion over all clips, relative to the parent bone.</summary>
public sealed record BoneMotion(int Bone, float MaxTranslation, float MaxRotationDeg, Vector3 MainDirection, string PeakClip, int PeakFrame);

/// <summary>The analyzer's findings. Every field can be overridden in the editor.</summary>
public sealed class WeaponAnalysis
{
    /// <summary>The weapon as it was loaded.</summary>
    public required WeaponAsset Source { get; init; }

    /// <summary>Clip whose first frame was used as the assembled pose ("" for the bind pose).</summary>
    public string ReferenceClip { get; init; } = "";
    public int ReferenceFrame { get; init; }

    /// <summary>The weapon in canonical space (muzzle +X, up +Z, realistic scale). All results use this space.</summary>
    public required WeaponAsset Asset { get; init; }

    /// <summary>Rotation from the model's own space into canonical space (applied before <see cref="Scale"/>).</summary>
    public required Quaternion ModelToCanonical { get; init; }

    /// <summary>Translation applied after rotation and scale so the weapon is centred on the origin.</summary>
    public Vector3 CanonicalOffset { get; init; }

    /// <summary>Uniform scale from the model's units into inches.</summary>
    public required float Scale { get; init; }
    public string ScaleReason { get; init; } = "";
    public required int RootBone { get; init; }
    public required IReadOnlySet<int> ArmBones { get; init; }

    /// <summary>The source file's origin in canonical space (first-person exports often put the eye there).</summary>
    public Vector3 SourceOrigin { get; init; }

    /// <summary>The file is first-person arms holding nothing (fists, bare hands).</summary>
    public bool HandsOnly { get; init; }
    public required int[] WeaponTriangles { get; init; }
    public required Bounds WeaponBounds { get; init; }
    public required ShapeProfile Profile { get; init; }
    public required OrientationGuess Orientation { get; init; }
    public required IReadOnlyList<BoneMotion> Motion { get; init; }

    /// <summary>Bore line: from the rear of the receiver to the muzzle along +X at this height/offset.</summary>
    public required Vector3 BoreStart { get; init; }
    public required Vector3 BoreEnd { get; init; }
    public float BoreDiameter { get; init; }

    public List<PartDetection> Parts { get; } = new();
    public PointDetection? Muzzle { get; set; }
    public PointDetection? Eject { get; set; }
    public TypeGuess Type { get; set; } = new(WeaponType.Custom, 0f, "", new Dictionary<WeaponType, float>());
    public GripCandidate? Primary { get; set; }
    public GripCandidate? Support { get; set; }

    /// <summary>Every plausible support-hand area, best first (the chosen one included).</summary>
    public List<GripCandidate> SupportAreas { get; } = new();
    public List<AnimationGuess> Animations { get; } = new();
    public Dictionary<AnimationRole, AnimationGuess> AssignedAnimations { get; } = new();

    /// <summary>More clips for a role that plays one of several in turn (left and right punches, slashes).</summary>
    public Dictionary<AnimationRole, List<string>> AnimationVariants { get; } = new();
    public List<string> Notes { get; } = new();

    public float Length => WeaponBounds.Size.X;
    public PartDetection? Part(PartKind kind) => Parts.FirstOrDefault(p => p.Kind == kind);

    private MeshBvh? _weaponBvh;

    /// <summary>BVH over the weapon only (arms and hands excluded).</summary>
    public MeshBvh WeaponBvh => _weaponBvh ??= new MeshBvh(Asset.Mesh, WeaponTriangles);
}
