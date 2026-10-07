#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Formats.Dmx;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;
using WeaponImporter.EditorTools.Core.Setup;

namespace WeaponImporter.EditorTools.Core.Generation;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

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
