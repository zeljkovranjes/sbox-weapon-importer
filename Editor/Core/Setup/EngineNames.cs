#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Grip;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

/// <summary>Names as the engine sees them (ModelDoc sanitises sequence and bone names).</summary>
public static class EngineNames
{
    public static string Sanitize(string name)
    {
        var raw = name;
        var sep = raw.LastIndexOf('|');
        if (sep >= 0 && sep < raw.Length - 1)
            raw = raw[(sep + 1)..];
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "anim" : s;
    }

    /// <summary>Sequence name the importer gives a source clip in the generated model.</summary>
    public static string Sequence(string clip) => Sanitize(clip).ToLowerInvariant();

    public static string Bone(string bone)
    {
        var hash = bone.IndexOf('#');
        if (hash >= 0)
            bone = bone[..hash];
        return new string(bone.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
    }
}
