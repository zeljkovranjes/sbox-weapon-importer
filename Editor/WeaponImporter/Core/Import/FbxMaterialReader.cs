#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Formats.Fbx;
using WeaponImporter.Core.Geometry;

namespace WeaponImporter.Core.Import;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// FBX materials and textures: Material objects connected to mesh Models (OO), Texture objects
/// connected to material properties (OP), and Video objects (OO to the texture) that may embed
/// the image bytes. Texture files are resolved on disk next to the FBX when the stored paths
/// are stale (the usual case for files exported on someone else's machine).
/// </summary>
internal sealed class FbxMaterialReader
{
    private readonly FbxScene _scene;
    private readonly string _folder;
    private readonly Dictionary<long, List<long>> _ooChildren = new();
    private readonly Dictionary<long, List<(long Child, string Property)>> _opChildren = new();
    private readonly Dictionary<long, int> _materialIndex = new();
    private readonly Dictionary<long, TextureRef?> _textures = new();
    private int _defaultIndex = -1;

    public List<MaterialInfo> Materials { get; } = new();

    public List<string> Notes { get; } = new();

    public FbxMaterialReader(FbxNode tree, FbxScene scene, string sourcePath)
    {
        _scene = scene;
        _folder = string.IsNullOrEmpty(sourcePath) ? "" : Path.GetDirectoryName(sourcePath) ?? "";
        foreach (var c in tree.Child("Connections")?.ChildrenNamed("C") ?? Enumerable.Empty<FbxNode>())
        {
            if (c.Properties.Count < 3 || c.Properties[0] is not string kind)
                continue;
            long child, parent;
            try
            {
                child = c.Prop<long>(1);
                parent = c.Prop<long>(2);
            }
            catch (FormatException)
            {
                continue;
            }
            if (kind == "OO")
            {
                if (!_ooChildren.TryGetValue(parent, out var list))
                    _ooChildren[parent] = list = new List<long>();
                list.Add(child);
            }
            else if (kind == "OP" && c.Properties.Count >= 4 && c.Properties[3] is string prop)
            {
                if (!_opChildren.TryGetValue(parent, out var list))
                    _opChildren[parent] = list = new List<(long, string)>();
                list.Add((child, prop));
            }
        }
    }

    /// <summary>Global material indices of the materials connected to a mesh model, in connection order.</summary>
    public int[] MaterialsOf(FbxObject model)
    {
        if (!_ooChildren.TryGetValue(model.Id, out var children))
            return Array.Empty<int>();
        var result = new List<int>();
        foreach (var id in children)
            if (_scene.ObjectsById.TryGetValue(id, out var obj) && obj.NodeType == "Material")
                result.Add(MaterialIndex(obj));
        return result.ToArray();
    }

    /// <summary>Index of a shared fallback material for meshes without one.</summary>
    public int DefaultMaterial()
    {
        if (_defaultIndex < 0)
        {
            _defaultIndex = Materials.Count;
            Materials.Add(MaterialInfo.Default);
        }
        return _defaultIndex;
    }

    private int MaterialIndex(FbxObject material)
    {
        if (_materialIndex.TryGetValue(material.Id, out var index))
            return index;
        index = Materials.Count;
        Materials.Add(BuildMaterial(material));
        _materialIndex[material.Id] = index;
        return index;
    }

    private MaterialInfo BuildMaterial(FbxObject m)
    {
        var diffuse = _scene.GetVector3(m, "DiffuseColor", _scene.GetVector3(m, "Diffuse", new Vector3(0.8f)));
        float alpha;
        if (_scene.FindProperty(m, "Opacity") is { Values.Count: >= 1 } opacity)
            alpha = (float)opacity.GetDouble();
        else
        {
            var factor = (float)_scene.GetDouble(m, "TransparencyFactor", 0);
            var color = _scene.GetVector3(m, "TransparentColor", Vector3.Zero);
            alpha = 1f - factor * (color.X + color.Y + color.Z) / 3f;
        }
        alpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0f, 1f) : 1f;

        TextureRef? baseTex = null, normalTex = null, roughTex = null, metalTex = null, emissiveTex = null, opacityTex = null, aoTex = null;
        if (_opChildren.TryGetValue(m.Id, out var links))
        {
            foreach (var (child, property) in links)
            {
                if (!_scene.ObjectsById.TryGetValue(child, out var texture) || texture.NodeType != "Texture")
                    continue;
                var tex = Texture(texture);
                if (tex is null)
                    continue;
                switch (Slot(property))
                {
                    case "base": baseTex ??= tex; break;
                    case "normal": normalTex ??= tex; break;
                    case "rough": roughTex ??= tex; break;
                    case "metal": metalTex ??= tex; break;
                    case "emissive": emissiveTex ??= tex; break;
                    case "opacity": opacityTex ??= tex; break;
                    case "ao": aoTex ??= tex; break;
                }
            }
        }
        // Exporters often drop the texture links; look for "<material>_<slot>.<ext>" images.
        if (baseTex is null || normalTex is null || roughTex is null || metalTex is null || aoTex is null || emissiveTex is null)
        {
            var byName = TexturesByName(m.Name);
            var found = byName.Count > 0 && ((baseTex is null && byName.ContainsKey("base")) || (normalTex is null && byName.ContainsKey("normal")));
            baseTex ??= byName.GetValueOrDefault("base");
            normalTex ??= byName.GetValueOrDefault("normal");
            roughTex ??= byName.GetValueOrDefault("rough");
            metalTex ??= byName.GetValueOrDefault("metal");
            aoTex ??= byName.GetValueOrDefault("ao");
            emissiveTex ??= byName.GetValueOrDefault("emissive");
            if (found)
                Notes.Add($"Material '{m.Name}': textures matched by file name.");
        }

        // An opacity map that is the base colour image means "use its alpha".
        if (opacityTex is not null && baseTex is not null && SameImage(opacityTex, baseTex))
            opacityTex = null;
        var hasOpacityMap = opacityTex is not null || (links?.Any(l => Slot(l.Property) == "opacity" && _scene.ObjectsById.ContainsKey(l.Child)) ?? false);

        // Blender writes Shininess = ((1 - roughness) * 10)^2 and ReflectionFactor = metallic.
        var shininess = (float)_scene.GetDouble(m, "Shininess", _scene.GetDouble(m, "ShininessExponent", 20));
        var roughness = Math.Clamp(1f - MathF.Sqrt(MathF.Max(0f, shininess)) / 10f, 0f, 1f);
        // Own value only: the Phong template default (1) is reflectivity, not metalness.
        var metalness = m.Properties.TryGetValue("ReflectionFactor", out var rf) && rf.Values.Count > 0 ? Math.Clamp((float)rf.GetDouble(), 0f, 1f) : 0f;

        return new MaterialInfo
        {
            Name = m.Name.Length > 0 ? m.Name : $"material_{m.Id}",
            // FBX semantics: a connected diffuse texture replaces the colour (Blender still
            // writes its viewport colour), so the tint is white then.
            BaseColor = baseTex is not null ? new Vector4(1f, 1f, 1f, alpha) : new Vector4(Clamp01(diffuse.X), Clamp01(diffuse.Y), Clamp01(diffuse.Z), alpha),
            BaseColorTexture = baseTex,
            NormalTexture = normalTex,
            RoughnessTexture = roughTex,
            MetalnessTexture = metalTex,
            AmbientOcclusionTexture = aoTex,
            EmissiveTexture = emissiveTex,
            OpacityTexture = opacityTex,
            AlphaCutoff = hasOpacityMap ? 0.5f : null,
            Translucent = !hasOpacityMap && alpha < 0.999f,
            Roughness = roughTex is not null ? 1f : roughness,
            Metalness = metalTex is not null ? 1f : metalness,
        };
    }

    private static float Clamp01(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 1f;

    private static bool SameImage(TextureRef a, TextureRef b)
        => ReferenceEquals(a, b) || (a.FilePath is not null && string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Which material slot an FBX material property feeds.</summary>
    internal static string Slot(string property)
    {
        var p = property.ToLowerInvariant();
        if (p.Contains("normal") || p.Contains("bump"))
            return "normal";
        if (p.Contains("rough") || p.Contains("shininess"))
            return "rough";
        if (p.Contains("metal") || p.Contains("reflectionfactor"))
            return "metal";
        if (p.Contains("emissive") || p.Contains("emission") || p.Contains("selfillum") || p.Contains("incandescence"))
            return "emissive";
        if (p.Contains("transparen") || p.Contains("opacity"))
            return "opacity";
        if (p.Contains("occlusion") || p.EndsWith("_ao", StringComparison.Ordinal) || p.Contains("ao_map"))
            return "ao";
        if (p.Contains("diffuse") || p.Contains("basecolor") || p.Contains("base_color") || p.Contains("base color") || p.Contains("color_map") || p == "maya|basecolor")
            return "base";
        return "";
    }

    private TextureRef? Texture(FbxObject texture)
    {
        if (_textures.TryGetValue(texture.Id, out var cached))
            return cached;
        var result = ResolveTexture(texture);
        _textures[texture.Id] = result;
        if (result is null)
            Notes.Add($"Texture '{texture.Name}' ({StringChild(texture.Node, "FileName") ?? StringChild(texture.Node, "RelativeFilename") ?? "no file"}) was not found.");
        return result;
    }

    private TextureRef? ResolveTexture(FbxObject texture)
    {
        var raws = new List<string>();
        void AddRaw(string? s)
        {
            if (!string.IsNullOrWhiteSpace(s) && !raws.Contains(s))
                raws.Add(s);
        }
        AddRaw(StringChild(texture.Node, "FileName"));
        AddRaw(StringChild(texture.Node, "RelativeFilename"));
        FbxObject? video = null;
        if (_ooChildren.TryGetValue(texture.Id, out var kids))
            foreach (var id in kids)
                if (_scene.ObjectsById.TryGetValue(id, out var obj) && obj.NodeType == "Video")
                {
                    video = obj;
                    break;
                }
        if (video is not null)
        {
            AddRaw(StringChild(video.Node, "FileName"));
            AddRaw(StringChild(video.Node, "RelativeFilename"));
        }

        // 1. Stored absolute paths, then relative to the FBX.
        foreach (var raw in raws)
            if (IsRooted(raw) && File.Exists(raw))
                return TextureRef.FromFile(raw);
        if (_folder.Length > 0)
            foreach (var raw in raws)
                if (!IsRooted(raw))
                {
                    var p = Path.Combine(_folder, raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(p))
                        return TextureRef.FromFile(Path.GetFullPath(p));
                }

        // 2. Bytes embedded in the Video node.
        if (video is not null && EmbeddedContent(video) is { Length: > 0 } bytes)
        {
            var nameSource = raws.FirstOrDefault() ?? video.Name;
            return TextureRef.FromBytes(bytes, TextureRef.ExtensionOf(nameSource), nameSource);
        }

        // 3. The file name next to the FBX, in a textures folder, or up to two folders up.
        if (_folder.Length > 0)
        {
            var names = raws.Select(TextureRef.FileNameOf).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dir = _folder;
            for (var level = 0; level <= 2 && !string.IsNullOrEmpty(dir); level++)
            {
                foreach (var name in names)
                    foreach (var sub in new[] { "", "textures", "Textures" })
                    {
                        var p = sub.Length == 0 ? Path.Combine(dir, name) : Path.Combine(dir, sub, name);
                        if (File.Exists(p))
                            return TextureRef.FromFile(Path.GetFullPath(p));
                    }
                dir = Path.GetDirectoryName(dir) ?? "";
            }
        }
        return null;
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".psd", ".exr" };

    private static readonly (string Token, string Slot)[] SlotTokens =
    {
        ("base_color", "base"), ("basecolor", "base"), ("albedo", "base"), ("diffuse", "base"), ("color", "base"), ("col", "base"), ("diff", "base"),
        ("normal", "normal"), ("normal_opengl", "normal"), ("normal_directx", "normal"), ("normal_dx", "normal"), ("normal_gl", "normal"), ("nrm", "normal"), ("norm", "normal"),
        ("roughness", "rough"), ("rough", "rough"),
        ("metallic", "metal"), ("metalness", "metal"), ("metal", "metal"),
        ("ao", "ao"), ("mixed_ao", "ao"), ("ambient_occlusion", "ao"), ("ambientocclusion", "ao"), ("occlusion", "ao"),
        ("emission", "emissive"), ("emissive", "emissive"),
    };

    private List<string>? _folderImages;

    /// <summary>Images named "&lt;material&gt;_&lt;slot&gt;[.udim].ext" in the texture search folders.</summary>
    private Dictionary<string, TextureRef> TexturesByName(string materialName)
    {
        var result = new Dictionary<string, TextureRef>();
        var key = Normalize(materialName);
        // Blender duplicates: "Hair.001" matches "Hair_BaseColor".
        var dup = key.LastIndexOf('_');
        if (dup > 0 && key.Length - dup == 4 && key[(dup + 1)..].All(char.IsDigit))
            key = key[..dup];
        if (key.Length == 0 || _folder.Length == 0)
            return result;
        _folderImages ??= ListFolderImages();
        foreach (var file in _folderImages)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            // Strip a UDIM tile (".1001").
            var dot = stem.LastIndexOf('.');
            if (dot > 0 && stem[(dot + 1)..].All(char.IsDigit))
                stem = stem[..dot];
            var n = Normalize(stem);
            if (!n.StartsWith(key + "_", StringComparison.Ordinal))
                continue;
            var rest = n[(key.Length + 1)..];
            foreach (var (token, slot) in SlotTokens)
                if (rest == token && !result.ContainsKey(slot))
                    result[slot] = TextureRef.FromFile(file);
        }
        return result;
    }

    private List<string> ListFolderImages()
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = _folder;
        for (var level = 0; level <= 2 && !string.IsNullOrEmpty(dir); level++)
        {
            foreach (var sub in new[] { "", "textures", "Textures" })
            {
                var d = sub.Length == 0 ? dir : Path.Combine(dir, sub);
                if (!Directory.Exists(d) || !seen.Add(Path.GetFullPath(d)))
                    continue;
                try
                {
                    foreach (var f in Directory.GetFiles(d).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        if (ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            files.Add(Path.GetFullPath(f));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return files;
    }

    private static string Normalize(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var r = new string(chars);
        while (r.Contains("__"))
            r = r.Replace("__", "_");
        return r.Trim('_');
    }

    private static bool IsRooted(string path)
        => path.Length >= 2 && (path[1] == ':' || path[0] == '/' || path[0] == '\\');

    private static string? StringChild(FbxNode node, string name)
    {
        var child = node.Child(name);
        if (child is null || child.Properties.Count == 0 || child.Properties[0] is not string s)
            return null;
        return s;
    }

    private static byte[]? EmbeddedContent(FbxObject video)
    {
        var content = video.Node.Child("Content");
        if (content is null || content.Properties.Count == 0)
            return null;
        switch (content.Properties[0])
        {
            case byte[] b:
                return b;
            case string s when s.Length > 0:
                // ASCII FBX stores the blob base64 encoded, possibly split over several strings.
                try
                {
                    var joined = string.Concat(content.Properties.OfType<string>()).Replace(",", "").Trim();
                    return Convert.FromBase64String(joined);
                }
                catch (FormatException)
                {
                    return null;
                }
            default:
                return null;
        }
    }
}
