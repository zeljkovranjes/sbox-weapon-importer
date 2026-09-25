#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Output;

using Vector3 = System.Numerics.Vector3;

/// <summary>An animation entry of a generated model.</summary>
public sealed record VmdlAnimation
{
    public required string Name { get; init; }
    /// <summary>Project-relative source file (DMX/FBX).</summary>
    public required string File { get; init; }
    public int Take { get; init; }
    public bool Looping { get; init; }
    public int FrameCount { get; init; }
    public float Fps { get; init; } = 30f;
    public IReadOnlyList<(string Name, float Time)> Events { get; init; } = Array.Empty<(string, float)>();
}

public sealed record VmdlAttachment(string Name, string Bone, Vector3 Position, Quaternion Rotation);

/// <summary>
/// Builds ModelDoc text for a generated weapon: one render mesh file, material remaps,
/// animations with generic events, and attachments.
/// </summary>
public sealed class VmdlBuilder
{
    public string MeshFile { get; set; } = "";
    public float ImportScale { get; set; } = 1f;
    public List<(string From, string To)> MaterialRemaps { get; } = new();
    public List<VmdlAnimation> Animations { get; } = new();
    public List<VmdlAttachment> Attachments { get; } = new();
    public List<string> KeepBones { get; } = new();
    public string AnimGraph { get; set; } = "";
    public string BaseModel { get; set; } = "";

    public string Build()
    {
        var children = new KvArray();

        if (!string.IsNullOrEmpty(MeshFile))
        {
            var meshFile = new KvObject()
                .Add("_class", "RenderMeshFile")
                .Add("name", Path.GetFileNameWithoutExtension(MeshFile))
                .Add("filename", MeshFile)
                .Add("import_translation", 0.0, 0.0, 0.0)
                .Add("import_rotation", 0.0, 0.0, 0.0)
                .Add("import_scale", (double)ImportScale)
                .Add("align_origin_x_type", "None")
                .Add("align_origin_y_type", "None")
                .Add("align_origin_z_type", "None")
                .Add("parent_bone", "")
                .Add("import_filter", new KvObject().Add("exclude_by_default", false).Add("exception_list", new KvArray()));
            children.Add(new KvObject().Add("_class", "RenderMeshList").Add("children", new KvArray().Add(meshFile)));
        }

        if (MaterialRemaps.Count > 0)
        {
            var remaps = new KvArray();
            foreach (var (from, to) in MaterialRemaps)
                remaps.Add(new KvObject().Add("from", from).Add("to", to));
            children.Add(new KvObject().Add("_class", "MaterialGroupList").Add("children", new KvArray().Add(
                new KvObject().Add("_class", "DefaultMaterialGroup").Add("remaps", remaps).Add("use_global_default", false).Add("global_default_material", ""))));
        }

        // Keep moving parts even when no mesh is skinned to them (culled bones break sequences).
        var markup = new KvArray();
        foreach (var bone in KeepBones.Distinct())
            markup.Add(new KvObject().Add("_class", "BoneMarkup").Add("target_bone", bone).Add("ignore_Translation", false).Add("ignore_rotation", false).Add("do_not_discard", true));
        children.Add(new KvObject().Add("_class", "BoneMarkupList").Add("children", markup).Add("bone_cull_type", "None"));

        if (Animations.Count > 0)
        {
            var anims = new KvArray();
            foreach (var a in Animations)
            {
                var anim = new KvObject().Add("_class", "AnimFile").Add("name", a.Name);
                if (a.Events.Count > 0)
                {
                    var events = new KvArray();
                    // The engine never dispatches an event on the last (seam) frame.
                    var last = Math.Max(0, a.FrameCount - 2);
                    foreach (var (name, time) in a.Events)
                    {
                        var frame = Math.Clamp((int)MathF.Round(time * Math.Max(1, a.FrameCount - 1)), 0, last);
                        events.Add(new KvObject()
                            .Add("_class", "AnimEvent")
                            .Add("event_class", "AE_GENERIC_EVENT")
                            .Add("event_frame", frame)
                            .Add("event_keys", new KvObject().Add("TypeName", name).Add("Int", frame).Add("Float", (double)(frame / MathF.Max(1f, a.Fps))).Add("StringData", a.Name)));
                    }
                    anim.Add("children", events);
                }
                anim.Add("activity_name", "")
                    .Add("activity_weight", 1)
                    .Add("weight_list_name", "")
                    .Add("fade_in_time", a.Looping ? 0.2 : 0.1)
                    .Add("fade_out_time", a.Looping ? 0.2 : 0.1)
                    .Add("looping", a.Looping)
                    .Add("delta", false)
                    .Add("worldSpace", false)
                    .Add("hidden", false)
                    .Add("anim_markup_ordered", false)
                    .Add("disable_compression", false)
                    .Add("disable_interpolation", false)
                    .Add("enable_scale", false)
                    .Add("source_filename", a.File)
                    .Add("start_frame", -1)
                    .Add("end_frame", -1)
                    .Add("framerate", -1.0)
                    .Add("take", a.Take)
                    .Add("reverse", false);
                anims.Add(anim);
            }
            children.Add(new KvObject().Add("_class", "AnimationList").Add("children", anims).Add("default_root_bone_name", ""));
        }

        if (Attachments.Count > 0)
        {
            var list = new KvArray();
            foreach (var at in Attachments)
            {
                var angles = ToAngles(at.Rotation);
                list.Add(new KvObject()
                    .Add("_class", "Attachment")
                    .Add("name", at.Name)
                    .Add("parent_bone", at.Bone)
                    .Add("relative_origin", at.Position.X, at.Position.Y, at.Position.Z)
                    .Add("relative_angles", angles.X, angles.Y, angles.Z)
                    .Add("weight", 1.0)
                    .Add("ignore_rotation", false));
            }
            children.Add(new KvObject().Add("_class", "AttachmentList").Add("children", list));
        }

        var rootNode = new KvObject()
            .Add("_class", "RootNode")
            .Add("children", children)
            .Add("model_archetype", "")
            .Add("primary_associated_entity", "")
            .Add("anim_graph_name", AnimGraph)
            .Add("base_model_name", BaseModel);
        return Kv3.Serialize(new KvObject().Add("rootNode", rootNode));
    }

    /// <summary>
    /// Quaternion -> Source angles (pitch, yaw, roll in degrees) for a Z-up, X-forward frame:
    /// R = Rz(yaw) * Ry(pitch) * Rx(roll).
    /// </summary>
    public static Vector3 ToAngles(Quaternion q)
    {
        q = MathQ.Normalize(q);
        var m = Matrix4x4.CreateFromQuaternion(q);
        // Row-vector matrix: row i = image of axis i. Forward = row 0, left = row 1, up = row 2.
        var fx = m.M11; var fy = m.M12; var fz = m.M13;
        var lz = m.M23; var uz = m.M33;
        var pitch = MathF.Asin(Math.Clamp(-fz, -1f, 1f));
        float yaw, roll;
        if (MathF.Abs(fz) < 0.9999f)
        {
            yaw = MathF.Atan2(fy, fx);
            roll = MathF.Atan2(lz, uz);
        }
        else
        {
            yaw = MathF.Atan2(-m.M21, m.M22);
            roll = 0f;
        }
        const float deg = 180f / MathF.PI;
        return new Vector3(pitch * deg, yaw * deg, roll * deg);
    }

    /// <summary>Inverse of <see cref="ToAngles"/>.</summary>
    public static Quaternion FromAngles(Vector3 angles)
    {
        const float rad = MathF.PI / 180f;
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angles.Y * rad);
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angles.X * rad);
        var roll = Quaternion.CreateFromAxisAngle(Vector3.UnitX, angles.Z * rad);
        return MathQ.Normalize(yaw * pitch * roll);
    }
}
