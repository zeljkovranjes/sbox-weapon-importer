#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Formats.Dmx;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Core.Generation;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Axis system and units a DMX is written in. Data inside the importer is s&amp;box model space
/// (inches, +X forward, +Z up).
/// <list type="bullet">
/// <item><see cref="SourceZUpInches"/> (default): data written as-is, declared Z-up
/// (DmeAxisSystem upAxis 3, forwardParity 1, coordSys 0). Verified in the editor: ModelDoc
/// compiles it unchanged.</item>
/// <item>Y-up: ModelDoc rotates a Y-up DMX by quaternion (0.5, 0.5, 0.5, 0.5), i.e. file
/// (x, y, z) becomes engine (z, x, y). The writer applies the inverse, writing engine (x, y, z)
/// as (y, z, x), so the compiled result matches the Z-up path. Units are never converted by
/// the engine, so <see cref="UnitScale"/> changes the compiled size.</item>
/// </list>
/// Only root bones are re-oriented (bone-local frames of children are untouched), matching how
/// ModelDoc applies the axis conversion.
/// </summary>
public sealed record DmxSpace(bool UpAxisY, float UnitScale)
{
    /// <summary>Z-up, inches, data as-is. The default for model and animation DMX.</summary>
    public static DmxSpace SourceZUpInches { get; } = new(false, 1f);

    /// <summary>Y-up (engine-rotated on import), inches.</summary>
    public static DmxSpace YUpInches { get; } = new(true, 1f);

    /// <summary>
    /// Write texture V with a bottom-left origin and declare <c>flipVCoordinates 1</c>
    /// (the Blender Source Tools convention). False writes TriMesh's top-left V with
    /// <c>flipVCoordinates 0</c>.
    /// </summary>
    public bool UvBottomLeft { get; init; } = true;

    /// <summary>Inverse of the engine's Y-up import rotation.</summary>
    private static readonly Quaternion ToYUp = Quaternion.Conjugate(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f));

    public Vector3 Point(Vector3 p) => Direction(p) * UnitScale;

    public Vector3 Direction(Vector3 v) => UpAxisY ? new Vector3(v.Y, v.Z, v.X) : v;

    public Quaternion RootRotation(Quaternion q) => UpAxisY ? MathQ.Normalize(ToYUp * q) : q;

    /// <summary>A bone local transform in file space (roots are re-oriented, all translations scaled).</summary>
    public XForm Local(XForm local, bool root)
        => root ? new XForm(Point(local.Pos), RootRotation(local.Rot)) : new XForm(local.Pos * UnitScale, local.Rot);

    public Vector2 Uv(Vector2 topLeft) => UvBottomLeft ? new Vector2(topLeft.X, 1f - topLeft.Y) : topLeft;

    internal void WriteAxisSystem(Kv2Writer w)
    {
        w.Attr("upAxis", "string", UpAxisY ? "Y" : "Z");
        w.BeginInline("axisSystem", "DmeAxisSystem");
        w.Attr("upAxis", "int", UpAxisY ? "2" : "3");
        w.Attr("forwardParity", "int", UpAxisY ? "2" : "1");
        w.Attr("coordSys", "int", "0");
        w.EndInline();
    }
}

/// <summary>Engine-safe names shared by the model and animation writers.</summary>
public static class DmxNames
{
    /// <summary>
    /// DMX joint name per skeleton bone: <see cref="EngineNames.Bone"/> (strips '#suffix',
    /// [A-Za-z0-9_]) made unique with _2, _3... in skeleton order. Both writers use this, so
    /// model joints and animation channels always agree.
    /// </summary>
    public static string[] Bones(Skeleton skeleton)
    {
        var result = new string[skeleton.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < skeleton.Count; i++)
        {
            var baseName = EngineNames.Bone(skeleton[i].Name);
            if (baseName.Trim('_').Length == 0)
                baseName = "bone";
            result[i] = Unique(baseName, used);
        }
        return result;
    }

    /// <summary>Material name for a faceSet: lowercase [a-z0-9_], no dots, never empty.</summary>
    public static string Material(string name)
    {
        var chars = (name ?? "").ToLowerInvariant().Select(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '_').ToArray();
        var s = new string(chars);
        while (s.Contains("__"))
            s = s.Replace("__", "_");
        s = s.Trim('_');
        if (s.Length == 0)
            s = "material";
        if (char.IsDigit(s[0]))
            s = "m_" + s;
        return s;
    }

    public static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = $"{name}_{n}";
        return candidate;
    }
}
