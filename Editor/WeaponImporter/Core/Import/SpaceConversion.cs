#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Core.Import;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Converts a source scene (centimetres, file axes) into s&amp;box model space (inches, +Z up).
/// The file's up axis becomes +Z, its coordinate (right) axis becomes +X and its front axis
/// becomes -Y, which matches how ModelDoc imports Y-up FBX files.
/// </summary>
public sealed class SpaceConversion
{
    public const float CmToInch = 1f / 2.54f;

    /// <summary>Row-vector basis change: v_sbox = v_file * Basis.</summary>
    public Matrix4x4 Basis { get; }

    public float Scale { get; }

    public SpaceConversion(int upAxis, int upSign, int frontAxis, int frontSign, int coordAxis, int coordSign, float scale = CmToInch)
    {
        var m = new Matrix4x4();
        void Set(int row, int col, float v)
        {
            switch (row * 4 + col)
            {
                case 0: m.M11 = v; break; case 1: m.M12 = v; break; case 2: m.M13 = v; break;
                case 4: m.M21 = v; break; case 5: m.M22 = v; break; case 6: m.M23 = v; break;
                case 8: m.M31 = v; break; case 9: m.M32 = v; break; case 10: m.M33 = v; break;
            }
        }
        if (upAxis == frontAxis || upAxis == coordAxis || frontAxis == coordAxis)
        {
            // Malformed axis record: assume the FBX default (Y up, Z front, X right).
            (upAxis, frontAxis, coordAxis) = (1, 2, 0);
        }
        Set(coordAxis, 0, coordSign >= 0 ? 1f : -1f);
        Set(frontAxis, 1, frontSign >= 0 ? -1f : 1f);
        Set(upAxis, 2, upSign >= 0 ? 1f : -1f);
        m.M44 = 1f;
        Basis = m;
        Scale = scale;
    }

    public static SpaceConversion For(SourceScene scene)
        => new(scene.UpAxis, scene.UpAxisSign, scene.FrontAxis, scene.FrontAxisSign, scene.CoordAxis, scene.CoordAxisSign);

    public static SpaceConversion Identity { get; } = new(2, 1, 1, -1, 0, 1, 1f);

    public Vector3 Point(Vector3 cm) => Vector3.Transform(cm, Basis) * Scale;

    public Vector3 Direction(Vector3 v) => Vector3.Transform(v, Basis);

    public Quaternion Rotation(Quaternion q)
    {
        // Similarity transform keeps a proper rotation even when the file axes are mirrored.
        var r = Matrix4x4.CreateFromQuaternion(q);
        var t = Matrix4x4.Transpose(Basis);
        var s = t * r * Basis;
        s.M14 = s.M24 = s.M34 = s.M41 = s.M42 = s.M43 = 0f;
        s.M44 = 1f;
        return MathQ.Normalize(Quaternion.CreateFromRotationMatrix(s));
    }

    public XForm Transform(XForm x) => new(Point(x.Pos), Rotation(x.Rot));

    public Skeleton Skeleton(Skeleton source)
    {
        var defs = new List<BoneDefinition>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var b = source[i];
            defs.Add(new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : source[b.ParentIndex].Name, Transform(b.RestLocal)));
        }
        return Rig.Skeleton.Create(defs);
    }

    /// <summary>Clips re-expressed on the converted skeleton (same bone order by name).</summary>
    public List<Clip> Clips(IReadOnlyList<Clip> clips, Skeleton source, Skeleton converted)
    {
        var map = new int[source.Count];
        for (var i = 0; i < source.Count; i++)
            map[i] = converted.IndexOf(source[i].Name);
        var result = new List<Clip>(clips.Count);
        foreach (var clip in clips)
        {
            var frames = new List<XForm[]>(clip.FrameCount);
            foreach (var frame in clip.Frames)
            {
                var locals = new XForm[converted.Count];
                for (var i = 0; i < source.Count && i < frame.Length; i++)
                    if (map[i] >= 0)
                        locals[map[i]] = Transform(frame[i]);
                frames.Add(locals);
            }
            result.Add(new Clip(clip.Name, clip.Fps, clip.Looping, frames, clip.NativeFps));
        }
        return result;
    }
}
