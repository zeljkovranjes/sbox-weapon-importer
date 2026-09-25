#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A hand grip captured from an animation (any rig) in a form that transfers to another hand
/// and another weapon: finger angles, and the palm's place and orientation relative to the grip
/// surface it holds, normalised by hand length. Anatomical axes (fingers, palm) are used, never
/// bone axes, so rigs with different bone orientations agree.
/// </summary>
public sealed class GripPreset
{
    public string Name { get; set; } = "";

    /// <summary>Where it came from: "this weapon's first-person animation", a pack file, "built-in".</summary>
    public string Source { get; set; } = "";

    public Side Side { get; set; }
    public GripStyle Style { get; set; }

    /// <summary>Palm centre in the grip surface frame, divided by the hand length.</summary>
    public float[] Palm { get; set; } = new float[3];

    /// <summary>Anatomical hand frame (x = fingers, y = out of the palm) in the grip surface frame.</summary>
    public float[] Orientation { get; set; } = { 0, 0, 0, 1 };

    /// <summary>
    /// Each finger's segment directions (x,y,z per joint, proximal first) in the anatomical hand
    /// frame. Applied by fitting the target hand's joint angles to them.
    /// </summary>
    public Dictionary<FingerKind, float[]> Directions { get; set; } = new();

    /// <summary>
    /// Preference added to the fit score when grips compete. A grip captured from this very
    /// weapon's own first-person animation gets <see cref="GripExtractor.OwnPriority"/>: it was
    /// authored for exactly this geometry, so it wins unless it fits clearly worse.
    /// </summary>
    [JsonIgnore] public float Priority { get; set; }

    /// <summary>Weapon type it was captured on (ranking prefers the same kind).</summary>
    public string WeaponType { get; set; } = "";
}

/// <summary>Captures grips from posed hands and applies them to other hands and weapons.</summary>
public static class GripLibrary
{
    /// <summary>
    /// Grip surface frame with a canonical sign: z out of the surface, x along the grip axis
    /// pointing up/forward (so a pistol grip and a handguard never flip between weapons).
    /// </summary>
    public static XForm SurfaceFrame(GripSurface s)
    {
        var n = Vector3.Normalize(s.Normal);
        var axis = s.Axis - n * Vector3.Dot(s.Axis, n);
        if (axis.LengthSquared() < 1e-6f)
            axis = MathQ.Perpendicular(n);
        axis = Vector3.Normalize(axis);
        if (Vector3.Dot(axis, Vector3.UnitZ + Vector3.UnitX * 0.3f) < 0f)
            axis = -axis;
        var y = Vector3.Cross(n, axis);
        return new XForm(s.Contact, MathQ.FromAxes(axis, y));
    }

    /// <summary>The hand's anatomical frame in the hand bone's own frame.</summary>
    public static Quaternion AnatomicalLocal(HandRig rig) => MathQ.FromAxes(rig.FingerDirection, rig.PalmNormal);

    /// <summary>
    /// Captures the grip of a posed hand. <paramref name="world"/> are the posed bone transforms
    /// of <paramref name="rig"/>'s skeleton in the weapon's space; <paramref name="surface"/> is
    /// the surface the hand holds, in the same space.
    /// </summary>
    public static GripPreset Capture(HandRig rig, IReadOnlyList<XForm> world, GripSurface surface, GripStyle style, string name, string source)
    {
        var frame = SurfaceFrame(surface);
        var wrist = world[rig.Hand];
        var palm = wrist.TransformPoint(rig.PalmCenter);
        var anat = MathQ.Normalize(wrist.Rot * AnatomicalLocal(rig));
        var palmLocal = Vector3.Transform(palm - frame.Pos, Quaternion.Conjugate(frame.Rot)) / MathF.Max(0.5f, rig.HandLength);
        var orientation = MathQ.Normalize(Quaternion.Conjugate(frame.Rot) * anat);

        var preset = new GripPreset
        {
            Name = name,
            Source = source,
            Side = rig.Side,
            Style = style,
            Palm = V.A(palmLocal),
            Orientation = V.A(orientation),
        };
        // Fingers as segment directions in the anatomical hand frame: independent of how each
        // rig's bones are oriented and of its rest pose (first-person rigs are often bound with
        // the fingers already curled).
        foreach (var finger in rig.Fingers)
            preset.Directions[finger.Kind] = FingerDirections(rig, finger, world, anat).SelectMany(d => new[] { d.X, d.Y, d.Z }).ToArray();
        return preset;
    }

    /// <summary>Direction of each segment of a finger in the anatomical hand frame.</summary>
    private static Vector3[] FingerDirections(HandRig rig, FingerRig finger, IReadOnlyList<XForm> world, Quaternion anatWorld)
    {
        var inverse = Quaternion.Conjugate(anatWorld);
        var dirs = new Vector3[finger.Joints.Length];
        for (var j = 0; j < dirs.Length; j++)
        {
            var along = Vector3.Transform(finger.Along[j], world[finger.Joints[j]].Rot);
            dirs[j] = Vector3.Normalize(Vector3.Transform(along, inverse));
        }
        return dirs;
    }

    /// <summary>
    /// Finger angles of <paramref name="rig"/> that reproduce captured segment directions, fitted
    /// per finger (flex per joint, base spread, and thumb opposition) by coordinate descent.
    /// </summary>
    private static void FitFingers(GripPreset preset, HandRig rig, HandPose pose)
    {
        var anat = AnatomicalLocal(rig);
        foreach (var finger in rig.Fingers)
        {
            if (!preset.Directions.TryGetValue(finger.Kind, out var flat) || flat.Length < 3)
                continue;
            var target = Enumerable.Range(0, flat.Length / 3).Select(i => new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2])).ToArray();
            var n = finger.Joints.Length;
            var thumb = finger.Kind == FingerKind.Thumb;
            var x = new float[n + 1 + (thumb ? 1 : 0)];
            var lo = new float[x.Length];
            var hi = new float[x.Length];
            for (var j = 0; j < n; j++)
            {
                lo[j] = -0.35f;
                hi[j] = finger.MaxFlex[j];
            }
            lo[n] = -0.6f;
            hi[n] = 0.6f;
            if (thumb)
            {
                lo[n + 1] = -1.2f;
                hi[n + 1] = 1.2f;
            }

            float Error()
            {
                pose.Flex[finger.Kind] = x.Take(n).ToArray();
                pose.Spread[finger.Kind] = x[n];
                if (thumb)
                    pose.ThumbOpposition = x[n + 1];
                var probe = pose.Clone();
                probe.Wrist = XForm.Identity;
                var world = HandShape.Of(rig, probe).JointWorld;
                var e = 0f;
                for (var j = 0; j < n; j++)
                {
                    var t = target[Math.Min(j, target.Length - 1)];
                    var along = Vector3.Normalize(Vector3.Transform(Vector3.Transform(finger.Along[j], world[finger.Joints[j]].Rot), Quaternion.Conjugate(anat)));
                    // Base segments matter most: they place the whole finger.
                    e += (1f - Vector3.Dot(along, t)) * (j == 0 ? 2f : 1f);
                }
                return e;
            }

            var best = Error();
            for (var step = 0.5f; step > 2e-3f; step *= 0.5f)
            {
                var improved = true;
                for (var guard = 0; improved && guard < 50; guard++)
                {
                    improved = false;
                    for (var k = 0; k < x.Length; k++)
                        foreach (var sign in new[] { 1f, -1f })
                        {
                            var old = x[k];
                            x[k] = Math.Clamp(old + sign * step, lo[k], hi[k]);
                            if (x[k] == old)
                                continue;
                            var e = Error();
                            if (e < best - 1e-7f)
                            {
                                best = e;
                                improved = true;
                            }
                            else
                            {
                                x[k] = old;
                            }
                        }
                }
            }
            pose.Flex[finger.Kind] = x.Take(n).ToArray();
            pose.Spread[finger.Kind] = x[n];
            if (thumb)
                pose.ThumbOpposition = x[n + 1];
        }
    }

    /// <summary>A preset placed on a surface for a (possibly different) hand, in the surface's space.</summary>
    public static HandPose Apply(GripPreset preset, HandRig rig, GripSurface surface)
    {
        var frame = SurfaceFrame(surface);
        var palm = frame.TransformPoint(V.Of(preset.Palm) * rig.HandLength);
        var anat = MathQ.Normalize(frame.Rot * V.Q(preset.Orientation));
        var wristRot = MathQ.Normalize(anat * Quaternion.Conjugate(AnatomicalLocal(rig)));
        var wristPos = palm - Vector3.Transform(rig.PalmCenter, wristRot);
        var pose = HandPose.Open(rig, new XForm(wristPos, wristRot));
        FitFingers(preset, rig, pose);
        return pose;
    }

    // ------------------------------------------------------------------ files

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(IEnumerable<GripPreset> presets) => JsonSerializer.Serialize(presets.ToList(), Json);

    public static List<GripPreset> FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GripPreset>>(json, Json) ?? new List<GripPreset>();
        }
        catch (JsonException e)
        {
            throw new FormatException($"Grip library is not valid JSON: {e.Message}", e);
        }
    }
}
