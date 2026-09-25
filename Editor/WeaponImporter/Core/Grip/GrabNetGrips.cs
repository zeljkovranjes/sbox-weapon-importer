#nullable enable annotations

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Grasp;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>One hand from a learned grasp generator: 21 keypoints in canonical weapon space (inches).</summary>
public sealed record LearnedGrasp(Side Side, Vector3[] Joints);

/// <summary>
/// GrabNet (and other MANO-based grasp generators) as a grip source. The network runs outside
/// the editor: <see cref="ExportObj"/> writes the weapon the way it expects (metres, centred), and
/// the grasps it produces come back as 21 hand keypoints per hand. Each grasp becomes an ordinary
/// <see cref="GripPreset"/> captured on the weapon surface under its palm, so it competes in Auto
/// Grip, can be picked per hand and is fitted to any character's fingers like the other grips.
/// </summary>
/// <remarks>
/// File format (JSON): <c>{ "units": "m", "layout": "openpose", "grasps": [ { "side": "right",
/// "joints": [[x,y,z] × 21] } ] }</c>. Units: m (default), cm, mm or in. Layout: "openpose"
/// (wrist, then thumb, index, middle, ring and little finger, base to tip) or "mano" (the MANO
/// layer's raw order: wrist, index, middle, little, ring, thumb joints, then the five tips).
/// Keypoints are in the frame of the exported OBJ.
/// </remarks>
public static class GrabNetGrips
{
    public const string Source = "GrabNet";

    /// <summary>Ranking bonus: grasps generated for this exact weapon beat generic library grips.</summary>
    public const float Priority = 1f;

    private const float MetresToInches = 39.3701f;

    /// <summary>Raw MANO layer joint index for each OpenPose keypoint.</summary>
    private static readonly int[] ManoToOpenPose = { 0, 13, 14, 15, 16, 1, 2, 3, 17, 4, 5, 6, 18, 10, 11, 12, 19, 7, 8, 9, 20 };

    private static readonly (FingerKind Kind, string Name)[] Fingers =
    {
        (FingerKind.Thumb, "thumb"), (FingerKind.Index, "index"), (FingerKind.Middle, "middle"), (FingerKind.Ring, "ring"), (FingerKind.Pinky, "pinky"),
    };

    /// <summary>The weapon (weapon triangles only, canonical space) as an OBJ in metres.</summary>
    public static string ExportObj(WeaponAnalysis a)
    {
        var mesh = a.Asset.Mesh;
        var sb = new StringBuilder();
        sb.AppendLine($"# {a.Asset.Name}: canonical weapon space (muzzle +X, up +Z), metres");
        var index = new Dictionary<int, int>();
        var faces = new StringBuilder();
        foreach (var t in a.WeaponTriangles)
        {
            faces.Append('f');
            for (var corner = 0; corner < 3; corner++)
            {
                var v = mesh.Indices[t * 3 + corner];
                if (!index.TryGetValue(v, out var i))
                {
                    index[v] = i = index.Count + 1;
                    var p = mesh.Positions[v] / MetresToInches;
                    sb.Append(CultureInfo.InvariantCulture, $"v {p.X:0.######} {p.Y:0.######} {p.Z:0.######}\n");
                }
                faces.Append(CultureInfo.InvariantCulture, $" {i}");
            }
            faces.Append('\n');
        }
        sb.Append(faces);
        return sb.ToString();
    }

    /// <summary>Reads a grasp file; throws with a clear message when it doesn't describe hands.</summary>
    public static List<LearnedGrasp> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var units = root.TryGetProperty("units", out var u) ? u.GetString() ?? "m" : "m";
        var scale = units.ToLowerInvariant() switch
        {
            "m" or "meters" or "metres" => MetresToInches,
            "cm" => MetresToInches / 100f,
            "mm" => MetresToInches / 1000f,
            "in" or "inches" => 1f,
            _ => throw new FormatException($"Unknown units '{units}' (use m, cm, mm or in)."),
        };
        var layout = root.TryGetProperty("layout", out var l) ? l.GetString() ?? "openpose" : "openpose";
        if (layout is not ("openpose" or "mano"))
            throw new FormatException($"Unknown layout '{layout}' (use openpose or mano).");
        if (!root.TryGetProperty("grasps", out var grasps) || grasps.ValueKind != JsonValueKind.Array)
            throw new FormatException("The file has no \"grasps\" array.");

        var list = new List<LearnedGrasp>();
        var n = 0;
        foreach (var g in grasps.EnumerateArray())
        {
            n++;
            var sideText = g.TryGetProperty("side", out var s) ? s.GetString() ?? "right" : "right";
            var side = sideText.StartsWith("l", StringComparison.OrdinalIgnoreCase) ? Side.Left : Side.Right;
            if (!g.TryGetProperty("joints", out var joints) || joints.GetArrayLength() != 21)
                throw new FormatException($"Grasp {n}: \"joints\" must hold 21 keypoints.");
            var raw = joints.EnumerateArray().Select(j =>
            {
                var c = j.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                if (c.Length != 3)
                    throw new FormatException($"Grasp {n}: every keypoint needs x, y and z.");
                return new Vector3(c[0], c[1], c[2]) * scale;
            }).ToArray();
            var ordered = layout == "mano" ? ManoToOpenPose.Select(i => raw[i]).ToArray() : raw;
            var handLength = Vector3.Distance(ordered[0], ordered[12]); // wrist to middle fingertip
            if (handLength is < 3f or > 14f)
                throw new FormatException($"Grasp {n}: the hand is {handLength:0.0} in long. Check \"units\" (a hand is about 7 in, 0.18 m).");
            list.Add(new LearnedGrasp(side, ordered));
        }
        return list;
    }

    /// <summary>
    /// Grips from learned grasps on this weapon: each hand captured on the surface under its palm.
    /// Grasps whose palm isn't on the weapon are skipped (reported in <paramref name="skipped"/>).
    /// </summary>
    public static List<GripPreset> ToPresets(WeaponAnalysis a, IReadOnlyList<LearnedGrasp> grasps, string source, out List<string> skipped)
    {
        skipped = new List<string>();
        var list = new List<GripPreset>();
        if (a.WeaponBvh.IsEmpty)
            return list;
        for (var i = 0; i < grasps.Count; i++)
        {
            var grasp = grasps[i];
            var label = $"{source} {i + 1} · {(grasp.Side == Side.Right ? "right" : "left")}";
            var (skeleton, world) = Skeletonize(grasp);
            var rig = HandRig.Build(skeleton, grasp.Side);
            if (rig is null || rig.Fingers.Count < 5)
            {
                skipped.Add($"{label}: not a hand");
                continue;
            }
            var palm = world[rig.Hand].TransformPoint(rig.PalmCenter);
            // Same surface rule as the weapon's own arms: on the detected grip when the palm is
            // there (grips are applied relative to it), else the surface under the palm.
            var candidate = grasp.Side == Side.Right ? a.Primary : a.Support;
            var style = grasp.Side == Side.Right ? (a.Type.Type == WeaponType.Melee ? GripStyle.Handle : GripStyle.Wrap) : GripStyle.Cradle;
            GripSurface? surface;
            if (candidate is not null && Vector3.Distance(candidate.Surface.Contact, palm) < rig.HandLength)
            {
                surface = candidate.Surface;
                style = candidate.Style;
            }
            else
            {
                surface = SurfaceProbe.FromPoint(a.WeaponBvh, palm, grasp.Side == Side.Right ? Vector3.UnitZ : Vector3.UnitX);
            }
            if (surface is null || Vector3.Distance(surface.Contact, palm) > GripExtractor.HoldingDistance + rig.PalmThickness)
            {
                skipped.Add($"{label}: the palm isn't on the weapon");
                continue;
            }
            var preset = GripLibrary.Capture(rig, world, surface, style, label, source);
            if (preset.Palm[2] < -0.12f)
            {
                skipped.Add($"{label}: the palm is inside the weapon");
                continue;
            }
            preset.WeaponType = a.Type.Type.ToString();
            preset.Priority = Priority;
            list.Add(preset);
        }
        return list;
    }

    /// <summary>
    /// A minimal hand skeleton on the keypoints (citizen bone names, so the hand is found the usual
    /// way): rest pose with the finger joints at the keypoints; the posed transforms turn each
    /// distal joint toward its fingertip, which the rest pose can't express.
    /// </summary>
    internal static (Skeleton Skeleton, XForm[] World) Skeletonize(LearnedGrasp grasp)
    {
        var k = grasp.Joints;
        var s = grasp.Side == Side.Right ? "R" : "L";
        var wrist = k[0];
        var knuckles = (k[5] + k[9] + k[13] + k[17]) / 4f;
        var forward = Vector3.Normalize(knuckles - wrist);
        var forearm = Vector3.Distance(wrist, knuckles) * 2.6f;
        var bones = new List<BoneDefinition>
        {
            new($"clavicle_{s}", null, new XForm(wrist - forward * forearm * 2.6f, Quaternion.Identity)),
            new($"arm_upper_{s}", $"clavicle_{s}", new XForm(forward * forearm * 0.6f, Quaternion.Identity)),
            new($"arm_lower_{s}", $"arm_upper_{s}", new XForm(forward * forearm, Quaternion.Identity)),
            new($"hand_{s}", $"arm_lower_{s}", new XForm(forward * forearm, Quaternion.Identity)),
        };
        for (var f = 0; f < Fingers.Length; f++)
        {
            var parent = $"hand_{s}";
            var parentPos = wrist;
            for (var j = 0; j < 3; j++)
            {
                var name = $"finger_{Fingers[f].Name}_{j}_{s}";
                var pos = k[1 + f * 4 + j];
                bones.Add(new BoneDefinition(name, parent, new XForm(pos - parentPos, Quaternion.Identity)));
                parent = name;
                parentPos = pos;
            }
        }
        var skeleton = Skeleton.Create(bones);
        var world = skeleton.RestWorld.ToArray();
        for (var f = 0; f < Fingers.Length; f++)
        {
            var distal = skeleton.IndexOf($"finger_{Fingers[f].Name}_2_{s}");
            var pip = k[1 + f * 4 + 1];
            var dip = k[1 + f * 4 + 2];
            var tip = k[1 + f * 4 + 3];
            if (Vector3.Distance(dip, pip) < 1e-4f || Vector3.Distance(tip, dip) < 1e-4f)
                continue;
            // The rig's distal segment points along pip→dip at rest; pose it along dip→tip.
            world[distal] = new XForm(world[distal].Pos, MathQ.FromTo(Vector3.Normalize(dip - pip), Vector3.Normalize(tip - dip)));
        }
        return (skeleton, world);
    }
}

/// <summary>Learned grasps as a grip source of their own (the Grips list offers them per hand too).</summary>
public sealed class GrabNetGripGenerator : IGripGenerator
{
    private readonly PresetGripGenerator _presets;
    private readonly int _count;

    public GrabNetGripGenerator(IEnumerable<GripPreset> presets)
    {
        var list = presets.ToList();
        _count = list.Count;
        _presets = new PresetGripGenerator(list);
    }

    public string Name => GrabNetGrips.Source;
    public bool IsAvailable => _count > 0;

    public IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default) => _presets.Generate(request, cancel);
}
