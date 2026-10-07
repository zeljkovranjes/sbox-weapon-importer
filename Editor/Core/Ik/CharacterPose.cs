#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Ik;

using Vector3 = System.Numerics.Vector3;

/// <summary>A character pose: parent-relative locals with lazily derived world transforms.</summary>
public sealed class CharacterPose
{
    public Skeleton Skeleton { get; }
    public XForm[] Locals { get; }
    private XForm[]? _world;

    public CharacterPose(Skeleton skeleton, XForm[] locals)
    {
        if (locals.Length != skeleton.Count)
            throw new ArgumentException("Pose bone count does not match the skeleton.", nameof(locals));
        Skeleton = skeleton;
        Locals = locals;
    }

    public static CharacterPose Rest(Skeleton skeleton) => new(skeleton, skeleton.Bones.Select(b => b.RestLocal).ToArray());

    public CharacterPose Clone() => new(Skeleton, Locals.ToArray());

    /// <summary>
    /// This pose with the upper body (the <paramref name="upperRoot"/> subtree: spine, head and
    /// arms) replaced by another pose's model-space transforms, matched by bone name. It is what
    /// Weapon Hold shows while a hold pose plays: the legs keep the animgraph, the rest follows
    /// the pose. Bones the other pose lacks keep theirs.
    /// </summary>
    public CharacterPose WithUpperBody(IReadOnlyDictionary<string, XForm> overlayModel, string upperRoot = "spine_0")
    {
        var root = Skeleton.IndexOf(upperRoot);
        if (root < 0)
            root = Skeleton.IndexOf("spine_1");
        if (root < 0 || overlayModel.Count == 0)
            return Clone();
        var inside = new bool[Skeleton.Count];
        inside[root] = true;
        for (var i = 0; i < Skeleton.Count; i++)
            if (Skeleton[i].ParentIndex >= 0 && inside[Skeleton[i].ParentIndex])
                inside[i] = true;
        var world = World.ToArray();
        // Anchored at the upper body's root: the overlay bends the upper body but stays on the
        // hips this pose has (as Weapon Hold plays it).
        var offset = overlayModel.TryGetValue(Skeleton[root].Name, out var anchor) ? world[root].Pos - anchor.Pos : System.Numerics.Vector3.Zero;
        for (var i = 0; i < Skeleton.Count; i++)
        {
            if (inside[i] && overlayModel.TryGetValue(Skeleton[i].Name, out var w))
                world[i] = new XForm(w.Pos + offset, w.Rot);
            else if (inside[i] && Skeleton[i].ParentIndex >= 0)
                world[i] = XForm.Compose(world[Skeleton[i].ParentIndex], Locals[i]);
        }
        var locals = Locals.ToArray();
        for (var i = 0; i < Skeleton.Count; i++)
            if (inside[i])
                locals[i] = Skeleton[i].ParentIndex >= 0 ? XForm.ToLocal(world[Skeleton[i].ParentIndex], world[i]) : world[i];
        return new CharacterPose(Skeleton, locals);
    }

    public XForm[] World => _world ??= ComputeWorld();

    public void Invalidate() => _world = null;

    private XForm[] ComputeWorld()
    {
        var w = new XForm[Locals.Length];
        for (var i = 0; i < w.Length; i++)
        {
            var p = Skeleton[i].ParentIndex;
            w[i] = p < 0 ? Locals[i] : XForm.Compose(w[p], Locals[i]);
        }
        return w;
    }

    /// <summary>Sets a bone's world rotation (children follow), keeping its position.</summary>
    public void SetWorldRotation(int bone, Quaternion world)
    {
        var parent = Skeleton[bone].ParentIndex;
        var parentRot = parent < 0 ? Quaternion.Identity : World[parent].Rot;
        Locals[bone].Rot = MathQ.Normalize(Quaternion.Conjugate(parentRot) * world);
        Invalidate();
    }

    /// <summary>Sets a bone's world transform (children follow).</summary>
    public void SetWorld(int bone, XForm world)
    {
        var parent = Skeleton[bone].ParentIndex;
        Locals[bone] = parent < 0 ? world : XForm.ToLocal(World[parent], world);
        Invalidate();
    }
}
