#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Core.Weapon;

using Vector3 = System.Numerics.Vector3;

public enum SourceKind { Fbx, Gltf, Vmdl, Synthetic }

/// <summary>An attachment point already present on the source (a VMDL's attachments).</summary>
public sealed record SourceAttachment(string Name, string Bone, XForm Local);

/// <summary>
/// Everything the importer knows about a weapon, in s&amp;box model space (inches, Z up,
/// +X forward). The editor fills it from a compiled model; tests build it from FBX/glTF
/// or synthetic meshes.
/// </summary>
public sealed class WeaponAsset
{
    public required string Name { get; init; }
    public required SourceKind Kind { get; init; }
    public string SourcePath { get; init; } = "";

    /// <summary>Weapon skeleton. A static prop gets a single root bone.</summary>
    public required Skeleton Skeleton { get; init; }

    /// <summary>Bind-pose geometry with per-vertex dominant bone.</summary>
    public required TriMesh Mesh { get; init; }

    /// <summary>Clips sampled on the skeleton (local transforms per frame).</summary>
    public IReadOnlyList<Clip> Clips { get; init; } = Array.Empty<Clip>();

    public IReadOnlyList<SourceAttachment> Attachments { get; init; } = Array.Empty<SourceAttachment>();

    /// <summary>
    /// Camera objects of the file kept as bones, with the local axes they look along and hold
    /// up (FBX cameras look down their +X, up +Y, carried through the axis conversion).
    /// </summary>
    public IReadOnlyDictionary<string, (Vector3 Forward, Vector3 Up)> CameraViews { get; init; } = new Dictionary<string, (Vector3 Forward, Vector3 Up)>();

    /// <summary>Import notes (unit fixes, skipped meshes...).</summary>
    public List<string> Notes { get; } = new();

    private MeshBvh? _bvh;

    /// <summary>BVH over the whole bind-pose mesh (built on first use).</summary>
    public MeshBvh Bvh => _bvh ??= new MeshBvh(Mesh);

    public Clip? FindClip(string name) => Clips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>World transform of a bone at a clip frame (bind pose when clip is null).</summary>
    public XForm BoneWorld(int bone, Clip? clip = null, int frame = 0)
    {
        if (clip is null || clip.FrameCount == 0)
            return Skeleton.RestWorld[bone];
        var locals = clip.Frames[Math.Clamp(frame, 0, clip.FrameCount - 1)];
        var world = XForm.Identity;
        var chain = new List<int>();
        for (var b = bone; b >= 0; b = Skeleton[b].ParentIndex)
            chain.Add(b);
        for (var i = chain.Count - 1; i >= 0; i--)
            world = XForm.Compose(world, locals[chain[i]]);
        return world;
    }

    /// <summary>Bones whose vertices make up the named part (by dominant skinning bone).</summary>
    public IEnumerable<int> TrianglesOfBone(int bone, bool includeChildren = true)
    {
        var set = new HashSet<int> { bone };
        if (includeChildren)
            for (var b = 0; b < Skeleton.Count; b++)
                if (Skeleton[b].ParentIndex >= 0 && set.Contains(Skeleton[b].ParentIndex))
                    set.Add(b);
        for (var t = 0; t < Mesh.TriangleCount; t++)
            if (set.Contains(Mesh.TriangleBone(t)))
                yield return t;
    }

    /// <summary>
    /// A copy rotated about the origin and uniformly scaled: mesh, skeleton roots and clip
    /// root tracks all move together, so animation stays consistent with the geometry.
    /// </summary>
    public WeaponAsset Transformed(Quaternion rotation, float scale)
    {
        rotation = MathQ.Normalize(rotation);
        XForm Root(XForm x) => new(Vector3.Transform(x.Pos, rotation) * scale, MathQ.Normalize(rotation * x.Rot));
        XForm Child(XForm x) => new(x.Pos * scale, x.Rot);

        var defs = new List<BoneDefinition>(Skeleton.Count);
        for (var i = 0; i < Skeleton.Count; i++)
        {
            var b = Skeleton[i];
            defs.Add(new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : Skeleton[b.ParentIndex].Name, b.ParentIndex < 0 ? Root(b.RestLocal) : Child(b.RestLocal)));
        }
        var skeleton = Rig.Skeleton.Create(defs);
        var map = Enumerable.Range(0, Skeleton.Count).Select(i => skeleton.IndexOf(Skeleton[i].Name)).ToArray();

        var clips = Clips.Select(c =>
        {
            var frames = c.Frames.Select(f =>
            {
                var locals = new XForm[skeleton.Count];
                for (var i = 0; i < f.Length && i < map.Length; i++)
                    locals[map[i]] = Skeleton[i].ParentIndex < 0 ? Root(f[i]) : Child(f[i]);
                return locals;
            }).ToList();
            return new Clip(c.Name, c.Fps, c.Looping, frames, c.NativeFps);
        }).ToList();

        var mesh = Mesh.WithVertices(
            Mesh.Positions.Select(p => Vector3.Transform(p, rotation) * scale).ToArray(),
            Mesh.VertexBone.Select(b => b >= 0 ? map[b] : b).ToArray(),
            _ => rotation);

        var attachments = Attachments.Select(a => a with { Local = new XForm(a.Local.Pos * scale, a.Local.Rot) }).ToList();
        var copy = new WeaponAsset { CameraViews = CameraViews, Name = Name, Kind = Kind, SourcePath = SourcePath, Skeleton = skeleton, Mesh = mesh, Clips = clips, Attachments = attachments };
        copy.Notes.AddRange(Notes);
        return copy;
    }

    /// <summary>
    /// A copy whose rest pose is a clip frame: bones take the frame's locals and every vertex
    /// follows its bone (rigid skinning on the dominant bone). First-person rigs often park
    /// magazines or arms somewhere odd in the bind pose; the idle frame is the assembled weapon.
    /// </summary>
    public WeaponAsset WithReferencePose(Clip clip, int frame = 0)
    {
        if (clip.FrameCount == 0)
            return this;
        var locals = clip.Frames[Math.Clamp(frame, 0, clip.FrameCount - 1)];
        var defs = new List<BoneDefinition>(Skeleton.Count);
        for (var i = 0; i < Skeleton.Count; i++)
        {
            var b = Skeleton[i];
            var local = i < locals.Length && locals[i].Rot.LengthSquared() > 0.5f ? locals[i] : b.RestLocal;
            defs.Add(new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : Skeleton[b.ParentIndex].Name, local));
        }
        var skeleton = Rig.Skeleton.Create(defs);
        var map = Enumerable.Range(0, Skeleton.Count).Select(i => skeleton.IndexOf(Skeleton[i].Name)).ToArray();
        var delta = new XForm[Skeleton.Count];
        for (var i = 0; i < Skeleton.Count; i++)
            delta[i] = XForm.Compose(skeleton.RestWorld[map[i]], Skeleton.RestWorld[i].Inverse());

        var positions = new Vector3[Mesh.Positions.Length];
        for (var v = 0; v < positions.Length; v++)
        {
            var bone = Mesh.VertexBone[v];
            positions[v] = bone >= 0 && bone < delta.Length ? delta[bone].TransformPoint(Mesh.Positions[v]) : Mesh.Positions[v];
        }
        var mesh = Mesh.WithVertices(positions, Mesh.VertexBone.Select(b => b >= 0 ? map[b] : b).ToArray(), v =>
        {
            var bone = Mesh.VertexBone[v];
            return bone >= 0 && bone < delta.Length ? delta[bone].Rot : Quaternion.Identity;
        });
        var copy = new WeaponAsset { CameraViews = CameraViews, Name = Name, Kind = Kind, SourcePath = SourcePath, Skeleton = skeleton, Mesh = mesh, Clips = Clips, Attachments = Attachments };
        copy.Notes.AddRange(Notes);
        return copy;
    }

    /// <summary>A copy moved rigidly (mesh, skeleton roots and clip root tracks).</summary>
    public WeaponAsset Moved(XForm move)
    {
        XForm Root(XForm x) => XForm.Compose(move, x);
        var defs = new List<BoneDefinition>(Skeleton.Count);
        for (var i = 0; i < Skeleton.Count; i++)
        {
            var b = Skeleton[i];
            defs.Add(new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : Skeleton[b.ParentIndex].Name, b.ParentIndex < 0 ? Root(b.RestLocal) : b.RestLocal));
        }
        var skeleton = Rig.Skeleton.Create(defs);
        var map = Enumerable.Range(0, Skeleton.Count).Select(i => skeleton.IndexOf(Skeleton[i].Name)).ToArray();
        var clips = Clips.Select(c => new Clip(c.Name, c.Fps, c.Looping, c.Frames.Select(f =>
        {
            var locals = new XForm[skeleton.Count];
            for (var i = 0; i < f.Length && i < map.Length; i++)
                locals[map[i]] = Skeleton[i].ParentIndex < 0 ? Root(f[i]) : f[i];
            return locals;
        }).ToList(), c.NativeFps)).ToList();
        var moveRot = MathQ.Normalize(move.Rot);
        var mesh = Mesh.WithVertices(Mesh.Positions.Select(move.TransformPoint).ToArray(), Mesh.VertexBone.Select(b => b >= 0 ? map[b] : b).ToArray(), _ => moveRot);
        var copy = new WeaponAsset { CameraViews = CameraViews, Name = Name, Kind = Kind, SourcePath = SourcePath, Skeleton = skeleton, Mesh = mesh, Clips = clips, Attachments = Attachments };
        copy.Notes.AddRange(Notes);
        return copy;
    }

    /// <summary>Skeleton with a single root bone, for static props.</summary>
    public static Skeleton SingleBone(string name = "root")
        => Skeleton.Create(new[] { new BoneDefinition(name, null, XForm.Identity) });
}
