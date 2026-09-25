#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Generation;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The rig a world model ships with: the weapon body becomes the fixed root, only bones the
/// weapon uses are kept (arms, cameras and scene helpers are dropped) and parts parented
/// elsewhere (a magazine hanging off a hand or the scene root) are re-parented under the body
/// with their motion relative to it preserved. Root motion of the body is removed, so a
/// first-person sway never moves the weapon in third person.
/// </summary>
public sealed class ExportRig
{
    public required WeaponAsset Asset { get; init; }
    public required int[] Triangles { get; init; }
    public required string RootName { get; init; }

    /// <summary>Old bone index -> new bone index (-1 when dropped).</summary>
    public required int[] Map { get; init; }

    public static ExportRig Build(WeaponAnalysis a, IEnumerable<string>? keepNames = null)
    {
        var source = a.Asset;
        var skeleton = source.Skeleton;
        var mesh = source.Mesh;

        // Body: the bone most weapon triangles are skinned to.
        var counts = new Dictionary<int, int>();
        foreach (var t in a.WeaponTriangles)
        {
            var b = mesh.TriangleBone(t);
            if (b >= 0)
                counts[b] = counts.GetValueOrDefault(b) + 1;
        }
        var body = counts.Count > 0 ? counts.OrderByDescending(kv => kv.Value).First().Key : a.RootBone;

        // Bones to keep: skinned weapon bones, named/moving parts, explicitly requested ones.
        var keep = new HashSet<int> { body };
        foreach (var b in counts.Keys)
            keep.Add(b);
        foreach (var p in a.Parts)
            if (p.Bone.Length > 0 && skeleton.IndexOf(p.Bone) is var pi && pi >= 0)
                keep.Add(pi);
        if (keepNames is not null)
            foreach (var n in keepNames)
                if (!string.IsNullOrEmpty(n) && skeleton.IndexOf(n) is var ki && ki >= 0 && !a.ArmBones.Contains(ki))
                    keep.Add(ki);
        // Close over ancestors that sit between the body and a kept bone (keeps hierarchies intact).
        foreach (var b in keep.ToList())
            for (var p = skeleton[b].ParentIndex; p >= 0 && p != body; p = skeleton[p].ParentIndex)
            {
                if (!IsDescendant(skeleton, p, body))
                    break;
                keep.Add(p);
            }

        // New parent of each kept bone: nearest kept ancestor under the body, else the body.
        int NewParent(int b)
        {
            if (b == body)
                return -1;
            for (var p = skeleton[b].ParentIndex; p >= 0; p = skeleton[p].ParentIndex)
                if (keep.Contains(p) && (p == body || IsDescendant(skeleton, p, body)))
                    return p;
            return body;
        }

        // Order: body first, then topological by new parent (stable, source order).
        var order = new List<int> { body };
        var placed = new HashSet<int> { body };
        var remaining = skeleton.Bones.Select(x => x.Index).Where(i => keep.Contains(i) && i != body).ToList();
        while (remaining.Count > 0)
        {
            var progress = false;
            foreach (var b in remaining.ToList())
                if (placed.Contains(NewParent(b)))
                {
                    order.Add(b);
                    placed.Add(b);
                    remaining.Remove(b);
                    progress = true;
                }
            if (!progress)
                break;
        }

        var rest = skeleton.RestWorld;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var defs = new List<BoneDefinition>();
        foreach (var b in order)
        {
            var parent = NewParent(b);
            var local = parent < 0 ? rest[b] : XForm.ToLocal(rest[parent], rest[b]);
            defs.Add(new BoneDefinition(skeleton[b].Name, parent < 0 ? null : skeleton[parent].Name, local));
            names.Add(skeleton[b].Name);
        }
        var exportSkeleton = Skeleton.Create(defs);
        var map = new int[skeleton.Count];
        for (var i = 0; i < map.Length; i++)
            map[i] = keep.Contains(i) ? exportSkeleton.IndexOf(skeleton[i].Name) : -1;

        // Vertices skinned to dropped bones fall back to their nearest kept ancestor (or the body).
        int MapBone(int b)
        {
            for (var x = b; x >= 0; x = skeleton[x].ParentIndex)
                if (map[x] >= 0)
                    return map[x];
            return map[body];
        }
        var vertexBone = mesh.VertexBone.Select(b => b >= 0 ? MapBone(b) : map[body]).ToArray();
        var exportMesh = mesh.WithVertices(mesh.Positions, vertexBone, _ => Quaternion.Identity);

        // Clips: re-express every kept bone relative to its new parent, frame by frame.
        var clips = new List<Clip>();
        foreach (var clip in source.Clips)
        {
            var frames = new List<XForm[]>(clip.FrameCount);
            foreach (var locals in clip.Frames)
            {
                var world = new XForm[skeleton.Count];
                for (var i = 0; i < skeleton.Count; i++)
                {
                    var p = skeleton[i].ParentIndex;
                    world[i] = p < 0 ? locals[i] : XForm.Compose(world[p], locals[i]);
                }
                // Body stays at its rest placement; everything else keeps its motion relative to it.
                var bodyWorld = world[body];
                var toRest = XForm.Compose(rest[body], bodyWorld.Inverse());
                var outLocals = new XForm[exportSkeleton.Count];
                foreach (var b in order)
                {
                    var nb = map[b];
                    var parent = NewParent(b);
                    if (parent < 0)
                        outLocals[nb] = rest[body];
                    else
                        outLocals[nb] = XForm.ToLocal(XForm.Compose(toRest, world[parent]), XForm.Compose(toRest, world[b]));
                }
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
        return new ExportRig { Asset = asset, Triangles = a.WeaponTriangles, RootName = skeleton[body].Name, Map = map };
    }

    private static bool IsDescendant(Skeleton skeleton, int bone, int ancestor)
    {
        for (var p = skeleton[bone].ParentIndex; p >= 0; p = skeleton[p].ParentIndex)
            if (p == ancestor)
                return true;
        return false;
    }
}
