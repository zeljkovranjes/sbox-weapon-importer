#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

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

    /// <summary>
    /// A second copy of the weapon for the left hand (dual pistols): its root bone and triangles.
    /// <see cref="WeaponTriangles"/> and everything measured from them are the right-hand copy.
    /// </summary>
    public string? SecondWeaponBone { get; init; }
    public int[] SecondWeaponTriangles { get; init; } = Array.Empty<int>();
    public bool Dual => SecondWeaponBone is not null;
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
