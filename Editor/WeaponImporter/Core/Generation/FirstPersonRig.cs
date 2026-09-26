#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Generation;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The first-person viewmodel of a file with its own arms: weapon, forearms, hands and camera,
/// with every animation exactly as authored (nothing re-parented, no root motion removed). Only
/// bones the shown triangles, the camera or the kept attachments need are exported.
/// </summary>
public sealed class FirstPersonRig
{
    public required WeaponAsset Asset { get; init; }
    public required int[] Triangles { get; init; }

    /// <summary>The rig's camera bone ("" when it has none).</summary>
    public string CameraBone { get; init; } = "";

    /// <summary>Camera bone frame -> view frame (forward +X, up +Z).</summary>
    public Quaternion CameraAxes { get; init; } = Quaternion.Identity;

    /// <summary>The eye in model space (reference pose): the camera, or a placement behind the grip.</summary>
    public XForm Eye { get; init; } = XForm.Identity;

    /// <summary>Where the eye came from ("camera bone X", "the character's eyes", "behind the weapon").</summary>
    public string EyeSource { get; init; } = "";

    /// <summary>Largest height of the view over the weapon's bore for rigs without a camera (inches).</summary>
    public const float ViewAboveBore = 3.5f;

    /// <summary>Smallest offset of the view to the left of the bore (the weapon sits right of center).</summary>
    public const float ViewLeftOfBore = 4f;

    /// <summary>Null when the file has no first-person arms (the weapon alone makes no viewmodel).</summary>
    public static FirstPersonRig? Build(WeaponAnalysis a, IEnumerable<string>? keepNames = null)
    {
        if (a.ArmBones.Count == 0)
            return null;
        var source = a.Asset;
        var skeleton = source.Skeleton;
        var mesh = source.Mesh;
        var triangles = ViewmodelParts.TrianglesByBone(a).Values.SelectMany(t => t).OrderBy(t => t).ToArray();
        if (triangles.Length == 0)
            return null;

        // Bones: everything the shown triangles are skinned to, the camera and kept attachment
        // bones, with all their ancestors (so every kept bone keeps its own parent and animation).
        var keep = new HashSet<int>();
        void Up(int bone)
        {
            for (var b = bone; b >= 0 && keep.Add(b); b = skeleton[b].ParentIndex)
            {
            }
        }
        foreach (var t in triangles)
            for (var k = 0; k < 3; k++)
                if (mesh.VertexBone[mesh.Indices[t * 3 + k]] is var vb && vb >= 0)
                    Up(vb);
        var camera = FindCamera(a);
        if (camera >= 0)
            Up(camera);
        if (keepNames is not null)
            foreach (var name in keepNames)
                if (!string.IsNullOrEmpty(name) && skeleton.IndexOf(name) is var i && i >= 0)
                    Up(i);
        if (keep.Count == 0)
            Up(0);

        var order = skeleton.Bones.Select(b => b.Index).Where(keep.Contains).ToList();
        var defs = order.Select(b => new BoneDefinition(skeleton[b].Name, skeleton[b].ParentIndex >= 0 ? skeleton[skeleton[b].ParentIndex].Name : null, skeleton[b].RestLocal)).ToList();
        var exportSkeleton = Skeleton.Create(defs);
        var map = new int[skeleton.Count];
        for (var i = 0; i < map.Length; i++)
            map[i] = keep.Contains(i) ? exportSkeleton.IndexOf(skeleton[i].Name) : -1;
        var vertexBone = mesh.VertexBone.Select(b => b >= 0 && map[b] >= 0 ? map[b] : 0).ToArray();
        var exportMesh = mesh.WithVertices(mesh.Positions, vertexBone, null);

        var clips = new List<Clip>();
        foreach (var clip in source.Clips)
        {
            var frames = new List<XForm[]>(clip.FrameCount);
            foreach (var locals in clip.Frames)
            {
                var outLocals = new XForm[exportSkeleton.Count];
                foreach (var b in order)
                    outLocals[map[b]] = locals[b];
                frames.Add(outLocals);
            }
            clips.Add(new Clip(clip.Name, clip.Fps, clip.Looping, frames, clip.NativeFps));
        }

        var asset = new WeaponAsset
        {
            Name = source.Name,
            Kind = source.Kind,
            SourcePath = source.SourcePath,
            Skeleton = exportSkeleton,
            Mesh = exportMesh,
            Clips = clips,
            Attachments = source.Attachments,
        };

        var (axes, eye, eyeSource) = EyeOf(a, camera);
        return new FirstPersonRig
        {
            Asset = asset,
            Triangles = triangles,
            CameraBone = camera >= 0 ? skeleton[camera].Name : "",
            CameraAxes = axes,
            Eye = eye,
            EyeSource = eyeSource,
        };
    }

    /// <summary>
    /// The view the rig's camera gives in the reference pose. Exporters disagree on a camera's
    /// axes: forward is the bone axis pointing most at the weapon, up the one closest to world up.
    /// Without a camera the eye sits behind, above and left of the grip, like a usual viewmodel.
    /// </summary>
    /// <summary>
    /// The first-person camera bone, or -1. A scene can hold several cameras (a Blender file's
    /// render cameras come along): the one nearest the hands is the player's view.
    /// </summary>
    public static int FindCamera(WeaponAnalysis a)
    {
        var skeleton = a.Asset.Skeleton;
        var cameras = Enumerable.Range(0, skeleton.Count).Where(i => ViewmodelParts.IsCameraName(skeleton[i].Name)).ToList();
        if (cameras.Count <= 1)
            return cameras.Count == 1 ? cameras[0] : -1;
        var world = GripExtractor.ReferenceWorld(a);
        var target = HandsCenter(a, world) ?? a.WeaponBounds.Center;
        // Cameras that know where they look: the one aimed at the hands (within a few feet);
        // others by distance.
        float Score(int i)
        {
            var distance = Vector3.Distance(world[i].Pos, target);
            if (!a.Asset.CameraViews.TryGetValue(skeleton[i].Name, out var view) || distance < 1e-3f)
                return 1000f + distance;
            var look = Vector3.Normalize(Vector3.Transform(view.Forward, world[i].Rot));
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(look, (target - world[i].Pos) / distance), -1f, 1f)) * 57.29578f;
            return angle + (distance > 60f ? 500f : distance * 0.2f);
        }
        return cameras.OrderBy(Score).First();
    }

    /// <summary>Midpoint of the file's two hands in the reference pose, or null.</summary>
    private static Vector3? HandsCenter(WeaponAnalysis a, IReadOnlyList<XForm> world)
    {
        var r = Hands.HandRig.Build(a.Asset.Skeleton, Hands.Side.Right);
        var l = Hands.HandRig.Build(a.Asset.Skeleton, Hands.Side.Left);
        if (r is null || !a.ArmBones.Contains(r.Hand))
            return null;
        return l is not null && a.ArmBones.Contains(l.Hand) ? (world[r.Hand].Pos + world[l.Hand].Pos) * 0.5f : world[r.Hand].Pos;
    }

    /// <summary>
    /// The eye of arms exported without a camera, the way first-person rigs place it: about a
    /// foot behind the hands and a few inches above, looking level along where the arms reach. Worked out in the file's own axes, since the analysis
    /// turns the held object (a knife, a bottle) in ways that have nothing to do with the view.
    /// </summary>
    private static (Quaternion Axes, XForm Eye, string Source)? ArmsEye(WeaponAnalysis a, IReadOnlyList<XForm> world)
    {
        var skeleton = a.Asset.Skeleton;
        var r = Hands.HandRig.Build(skeleton, Hands.Side.Right);
        var l = Hands.HandRig.Build(skeleton, Hands.Side.Left);
        if (r is null || !a.ArmBones.Contains(r.Hand))
            return null;
        var toFile = Quaternion.Conjugate(a.ModelToCanonical);
        Vector3 File(Vector3 c) => Vector3.Transform((c - a.CanonicalOffset) / a.Scale, toFile);
        Vector3 Canonical(Vector3 f) => Vector3.Transform(f, a.ModelToCanonical) * a.Scale + a.CanonicalOffset;

        var both = l is not null && a.ArmBones.Contains(l.Hand);
        var shoulderR = File(world[r.UpperArm].Pos);
        var shoulderL = both ? File(world[l!.UpperArm].Pos) : shoulderR;
        var handR = File(world[r.Hand].Pos);
        var hands = both ? (handR + File(world[l!.Hand].Pos)) * 0.5f : handR;
        var shoulders = (shoulderR + shoulderL) * 0.5f;
        var reach = hands - shoulders;

        var up = Vector3.UnitZ;
        var forward = reach - up * Vector3.Dot(reach, up);
        forward = forward.Length() > 1f / a.Scale ? Vector3.Normalize(forward) : -Vector3.UnitY;
        // First-person cameras sit about a foot behind the hands and a few inches above them
        // (measured on rigs that ship a camera): place the eye from the hands, level.
        var eye = hands - forward * (11f / a.Scale) + up * (5f / a.Scale);
        if (Vector3.Dot(hands - eye, forward) < 1f / a.Scale)
            return null;
        var view = MathQ.FromAxes(forward, Vector3.Cross(up, forward));
        return (Quaternion.Identity, new XForm(Canonical(eye), MathQ.Normalize(a.ModelToCanonical * view)), "the arms (the file has no camera)");
    }

    private static (Quaternion Axes, XForm Eye, string Source) EyeOf(WeaponAnalysis a, int camera)
    {
        var world = GripExtractor.ReferenceWorld(a);
        var center = a.WeaponBounds.Center;
        if (camera < 0 && ArmsEye(a, world) is { } arms && ViewmodelParts.EyePoint(a.Asset.Skeleton, world) is null)
            return arms;
        if (camera < 0)
        {
            // A full character: its own eyes, looking along the weapon. Otherwise behind and
            // above the weapon's rear, offset left so the weapon sits lower right on screen.
            if (ViewmodelParts.EyePoint(a.Asset.Skeleton, world) is { } eyes)
            {
                // A character holds a weapon at chest height, well below its eyes; a viewmodel
                // sits close under the eye line and a little right: keep the eye at most
                // ViewAboveBore over the bore and at least ViewLeftOfBore to its left.
                var framed = new Vector3(eyes.X, MathF.Max(eyes.Y, a.BoreStart.Y + ViewLeftOfBore), MathF.Min(eyes.Z, a.BoreStart.Z + ViewAboveBore));
                return (Quaternion.Identity, new XForm(framed, Quaternion.Identity), "the character's eyes, framed like a viewmodel");
            }
            // Viewmodels exported for Source, Unity and most engines put the eye at the file's
            // origin, looking forward: use it when it sits where an eye would (behind the held
            // object, within a few feet, not below it).
            var o = a.SourceOrigin;
            var b = a.WeaponBounds;
            var toObject = b.Center - o;
            if (o.X < b.Center.X - 1f && toObject.Length() is > 3f and < 48f && o.Z > b.Min.Z - 2f
                && MathF.Abs(toObject.Y) < toObject.X * 1.2f && toObject.Z < toObject.X)
                return (Quaternion.Identity, new XForm(o, Quaternion.Identity), "the file's origin");
            var rear = new Vector3(a.WeaponBounds.Min.X, a.BoreStart.Y, a.BoreStart.Z);
            return (Quaternion.Identity, new XForm(rear + new Vector3(-6f, 6f, 5f), Quaternion.Identity), "behind the weapon");
        }
        var cam = world[camera];
        // A real camera object: its authored view, exactly.
        if (a.Asset.CameraViews.TryGetValue(a.Asset.Skeleton[camera].Name, out var authored))
        {
            var look = Vector3.Normalize(Vector3.Transform(authored.Forward, cam.Rot));
            var above = Vector3.Transform(authored.Up, cam.Rot);
            var authoredView = MathQ.FromAxes(look, Vector3.Normalize(Vector3.Cross(above, look)));
            return (MathQ.Normalize(Quaternion.Conjugate(cam.Rot) * authoredView), new XForm(cam.Pos, authoredView), $"camera {a.Asset.Skeleton[camera].Name}");
        }
        var toWeapon = center - cam.Pos;
        toWeapon = toWeapon.LengthSquared() > 1e-6f ? Vector3.Normalize(toWeapon) : Vector3.UnitX;
        var axes = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ }
            .Select(v => Vector3.Transform(v, cam.Rot)).ToArray();
        // What the player sees: the arms, hands and held object. The camera looks down the axis
        // that has most of them in view (exporters disagree on a camera's axes).
        var seen = new List<Vector3>();
        foreach (var side in new[] { Hands.Side.Right, Hands.Side.Left })
            if (Hands.HandRig.Build(a.Asset.Skeleton, side) is { } hand && a.ArmBones.Contains(hand.Hand))
            {
                seen.Add(world[hand.Hand].Pos);
                seen.Add(world[hand.LowerArm].Pos);
                seen.AddRange(hand.Fingers.SelectMany(f => f.Joints).Select(j => world[j].Pos));
            }
        // The held object counts as much as a hand.
        for (var i = 0; i < Math.Max(5, seen.Count / 2); i++)
            seen.Add(center);
        var sights = seen.Where(p => Vector3.Distance(p, cam.Pos) > 1f).Select(p => Vector3.Normalize(p - cam.Pos)).ToList();
        int InView(Vector3 axis) => sights.Count(d => Vector3.Dot(d, axis) > 0.8f);
        var forward = axes.OrderByDescending(InView).ThenByDescending(v => Vector3.Dot(v, toWeapon)).First();
        if (InView(forward) == 0)
            forward = sights.Count > 0 ? Vector3.Normalize(sights.Aggregate(Vector3.Zero, (s, d) => s + d)) : toWeapon;
        var candidates = axes.Where(v => MathF.Abs(Vector3.Dot(v, forward)) < 0.5f).ToList();
        var up = candidates.Count > 0 ? candidates.OrderByDescending(v => v.Z).First() : Vector3.UnitZ;
        var view = MathQ.FromAxes(forward, Vector3.Cross(up, forward));
        var fix = MathQ.Normalize(Quaternion.Conjugate(cam.Rot) * view);
        return (fix, new XForm(cam.Pos, view), $"camera bone {a.Asset.Skeleton[camera].Name}");
    }
}
