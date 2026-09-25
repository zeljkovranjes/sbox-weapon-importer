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
        var camera = ViewmodelParts.CameraBone(skeleton);
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

        var (axes, eye) = EyeOf(a, camera);
        return new FirstPersonRig
        {
            Asset = asset,
            Triangles = triangles,
            CameraBone = camera >= 0 ? skeleton[camera].Name : "",
            CameraAxes = axes,
            Eye = eye,
        };
    }

    /// <summary>
    /// The view the rig's camera gives in the reference pose. Exporters disagree on a camera's
    /// axes: forward is the bone axis pointing most at the weapon, up the one closest to world up.
    /// Without a camera the eye sits behind, above and left of the grip, like a usual viewmodel.
    /// </summary>
    private static (Quaternion Axes, XForm Eye) EyeOf(WeaponAnalysis a, int camera)
    {
        var world = GripExtractor.ReferenceWorld(a);
        var center = a.WeaponBounds.Center;
        if (camera < 0)
        {
            var grip = a.Primary?.Surface.Contact ?? center;
            return (Quaternion.Identity, new XForm(grip + new Vector3(-14f, 5f, 6f), Quaternion.Identity));
        }
        var cam = world[camera];
        var toWeapon = center - cam.Pos;
        toWeapon = toWeapon.LengthSquared() > 1e-6f ? Vector3.Normalize(toWeapon) : Vector3.UnitX;
        var axes = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ }
            .Select(v => Vector3.Transform(v, cam.Rot)).ToArray();
        var forward = axes.OrderByDescending(v => Vector3.Dot(v, toWeapon)).First();
        if (Vector3.Dot(forward, toWeapon) < 0.6f)
            forward = toWeapon;
        var candidates = axes.Where(v => MathF.Abs(Vector3.Dot(v, forward)) < 0.5f).ToList();
        var up = candidates.Count > 0 ? candidates.OrderByDescending(v => v.Z).First() : Vector3.UnitZ;
        var view = MathQ.FromAxes(forward, Vector3.Cross(up, forward));
        var fix = MathQ.Normalize(Quaternion.Conjugate(cam.Rot) * view);
        return (fix, new XForm(cam.Pos, view));
    }
}
