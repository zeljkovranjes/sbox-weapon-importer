#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Formats.Dmx;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Core.Generation;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Writes one clip as an animation DMX (<c>keyvalues2_noids</c>) with the element layout of
/// fbx2dmx output: a root DmElement holding an inline DmeModel (joint refs + bind base state),
/// a DmeAnimationList with one DmeChannelsClip carrying a position and an orientation channel
/// per bone, and top-level DmeTransform / DmeJoint elements the channels reference. Joint names
/// come from <see cref="DmxNames.Bones"/>, identical to <see cref="ModelDmxWriter"/>. Output is
/// deterministic (ids hashed from the name, fixed export tags).
/// </summary>
public static class AnimationDmxWriter
{
    /// <param name="excludedBones">Bone indices that get no channels (the engine drives them).</param>
    public static string Write(Skeleton skeleton, Clip clip, DmxSpace? space, string name, IReadOnlySet<int>? excludedBones = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(clip);
        space ??= DmxSpace.SourceZUpInches;
        if (clip.FrameCount == 0)
            throw new ArgumentException("Clip has no frames.", nameof(clip));
        if (skeleton.Count == 0)
            throw new ArgumentException("Skeleton has no bones.", nameof(skeleton));
        name = string.IsNullOrWhiteSpace(name) ? clip.Name : name;

        var boneNames = DmxNames.Bones(skeleton);
        var rest = new XForm[skeleton.Count];
        for (var i = 0; i < skeleton.Count; i++)
            rest[i] = space.Local(skeleton[i].RestLocal, skeleton[i].ParentIndex < 0);

        // Frames in file space; missing / zero entries fall back to the rest local.
        var frames = new XForm[clip.FrameCount][];
        for (var f = 0; f < clip.FrameCount; f++)
        {
            var src = clip.Frames[f];
            var dst = new XForm[skeleton.Count];
            for (var i = 0; i < skeleton.Count; i++)
                dst[i] = i < src.Length && src[i].Rot.LengthSquared() > 0.5f
                    ? space.Local(new XForm(src[i].Pos, MathQ.Normalize(src[i].Rot)), skeleton[i].ParentIndex < 0)
                    : rest[i];
            frames[f] = dst;
        }

        var scope = "weapon-anim:" + name;
        var animListId = DmxIds.Guid(scope, "animationList");
        var jointIds = boneNames.Select(b => DmxIds.Guid(scope, "joint:" + b)).ToArray();
        var transformIds = boneNames.Select(b => DmxIds.Guid(scope, "transform:" + b)).ToArray();

        var w = new Kv2Writer(blankAfterInline: true);
        w.Raw(Kv2Writer.ModelHeader);

        w.BeginTop("DmElement");
        w.Attr("name", "string", "root");
        w.BeginInline("skeleton", "DmeModel");
        w.Attr("name", "string", name);
        w.BeginInline("transform", "DmeTransform");
        w.Attr("position", "vector3", "0 0 0");
        w.Attr("orientation", "quaternion", "0 0 0 1");
        w.EndInline();
        w.Attr("shape", "element", "");
        w.Attr("visible", "bool", "1");
        var roots = Enumerable.Range(0, skeleton.Count).Where(i => skeleton[i].ParentIndex < 0).Select(i => jointIds[i]).ToList();
        w.Refs("children", roots);
        w.Refs("jointList", jointIds);

        w.BeginArray("baseStates");
        w.BeginArrayElement("DmeTransformList");
        w.Attr("name", "string", "bind");
        w.BeginArray("transforms");
        for (var i = 0; i < skeleton.Count; i++)
        {
            w.BeginArrayElement("DmeTransform");
            w.Attr("name", "string", boneNames[i]);
            w.Attr("position", "vector3", Kv2Writer.Vec(rest[i].Pos));
            w.Attr("orientation", "quaternion", Kv2Writer.Quat(rest[i].Rot));
            w.EndArrayElement(i == skeleton.Count - 1);
        }
        w.EndArray();
        w.EndArrayElement(true);
        w.EndArray();

        space.WriteAxisSystem(w);
        w.Attr("animationList", "element", animListId);
        w.EndInline(); // skeleton

        w.BeginInline("makefile", "DmeDCCMakefile");
        w.Attr("name", "string", "makefile");
        w.BeginArray("sources");
        w.BeginArrayElement("DmeSource");
        w.Attr("name", "string", clip.Name);
        w.EndArrayElement(true);
        w.EndArray();
        w.EndInline();

        w.BeginInline("exportTags", "DmeExportTags");
        w.Attr("name", "string", "exportTags");
        w.Attr("date", "string", "2026/01/01");
        w.Attr("time", "string", "12:00:00 am");
        w.Attr("user", "string", "weaponimporter");
        w.Attr("machine", "string", "weaponimporter");
        w.Attr("app", "string", "weapon-importer");
        w.Attr("appVersion", "string", "1.0");
        w.Attr("cmdLine", "string", "weapon-importer");
        w.Attr("pwd", "string", "");
        w.EndInline();

        w.Attr("animationList", "element", animListId);
        w.EndTop();

        w.BeginTop("DmeAnimationList");
        w.Attr("id", "elementid", animListId);
        w.Attr("name", "string", "anim");
        w.BeginArray("animations");
        w.BeginArrayElement("DmeChannelsClip");
        w.Attr("name", "string", "anim");
        w.BeginInline("timeFrame", "DmeTimeFrame");
        w.Attr("start", "time", Kv2Writer.Time(0));
        w.Attr("duration", "time", Kv2Writer.Time((clip.FrameCount - 1) / (double)clip.Fps));
        w.Attr("offset", "time", Kv2Writer.Time(0));
        w.Attr("scale", "float", "1");
        w.EndInline();
        w.Attr("color", "color", "0 0 0 0");
        w.Attr("text", "string", "");
        w.Attr("mute", "bool", "0");
        w.BeginArray("trackGroups");
        w.EndArray();
        w.Attr("displayScale", "float", "1");

        var channelBones = Enumerable.Range(0, skeleton.Count).Where(i => excludedBones is null || !excludedBones.Contains(i)).ToList();
        w.BeginArray("channels");
        for (var n = 0; n < channelBones.Count; n++)
        {
            var i = channelBones[n];
            Channel(w, boneNames[i], transformIds[i], frames, i, clip.Fps, position: true, last: false);
            Channel(w, boneNames[i], transformIds[i], frames, i, clip.Fps, position: false, last: n == channelBones.Count - 1);
        }
        w.EndArray();
        w.Attr("frameRate", "int", Kv2Writer.I((int)MathF.Round(clip.Fps)));
        w.EndArrayElement(true);
        w.EndArray();
        w.EndTop();

        for (var i = 0; i < skeleton.Count; i++)
        {
            w.BeginTop("DmeTransform");
            w.Attr("id", "elementid", transformIds[i]);
            w.Attr("name", "string", boneNames[i]);
            w.Attr("position", "vector3", Kv2Writer.Vec(rest[i].Pos));
            w.Attr("orientation", "quaternion", Kv2Writer.Quat(rest[i].Rot));
            w.EndTop();
        }

        for (var i = 0; i < skeleton.Count; i++)
        {
            w.BeginTop("DmeJoint");
            w.Attr("id", "elementid", jointIds[i]);
            w.Attr("name", "string", boneNames[i]);
            w.Attr("transform", "element", transformIds[i]);
            w.Attr("shape", "element", "");
            w.Attr("visible", "bool", "1");
            var children = Enumerable.Range(0, skeleton.Count).Where(c => skeleton[c].ParentIndex == i).Select(c => jointIds[c]).ToList();
            w.Refs("children", children);
            w.EndTop();
        }

        return w.ToString();
    }

    private static void Channel(Kv2Writer w, string bone, string transformId, XForm[][] frames, int index, float fps, bool position, bool last)
    {
        var logClass = position ? "DmeVector3Log" : "DmeQuaternionLog";
        var layerClass = position ? "DmeVector3LogLayer" : "DmeQuaternionLogLayer";
        var logName = position ? "vector3 log" : "quaternion log";

        w.BeginArrayElement("DmeChannel");
        w.Attr("name", "string", bone + (position ? "_p" : "_o"));
        w.Attr("fromElement", "element", "");
        w.Attr("fromAttribute", "string", "");
        w.Attr("fromIndex", "int", "0");
        w.Attr("toElement", "element", transformId);
        w.Attr("toAttribute", "string", position ? "position" : "orientation");
        w.Attr("toIndex", "int", "0");
        w.Attr("mode", "int", "3");

        w.BeginInline("log", logClass);
        w.Attr("name", "string", logName);
        w.BeginArray("layers");
        w.BeginArrayElement(layerClass);
        w.Attr("name", "string", logName);

        w.BeginArray("times", "time_array");
        for (var f = 0; f < frames.Length; f++)
            w.Value(Kv2Writer.Time(f / (double)fps), f == frames.Length - 1);
        w.EndArray();
        w.BeginArray("curvetypes", "int_array");
        w.EndArray();

        w.BeginArray("values", position ? "vector3_array" : "quaternion_array");
        // Hemisphere-align orientations: the engine interpolates samples numerically.
        var prev = Quaternion.Identity;
        for (var f = 0; f < frames.Length; f++)
        {
            var x = frames[f][index];
            string value;
            if (position)
                value = Kv2Writer.Vec(x.Pos);
            else
            {
                var q = x.Rot;
                if (f > 0 && Quaternion.Dot(prev, q) < 0f)
                    q = Quaternion.Negate(q);
                prev = q;
                value = Kv2Writer.Quat(q);
            }
            w.Value(value, f == frames.Length - 1);
        }
        w.EndArray();
        w.EmptyBinary("compressed");
        w.EndArrayElement(true);
        w.EndArray();

        w.Attr("curveinfo", "element", "");
        w.Attr("usedefaultvalue", "bool", "0");
        w.Attr("defaultvalue", position ? "vector3" : "quaternion", position ? "0 0 0" : "0 0 0 1");
        w.BeginArray("bookmarksX", "time_array");
        w.EndArray();
        w.BeginArray("bookmarksY", "time_array");
        w.EndArray();
        w.BeginArray("bookmarksZ", "time_array");
        w.EndArray();
        w.EndInline();

        w.EndArrayElement(last);
    }
}
