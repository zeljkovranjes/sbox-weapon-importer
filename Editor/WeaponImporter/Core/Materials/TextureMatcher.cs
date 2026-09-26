#nullable enable annotations

using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Materials;

public enum TextureSlot { BaseColor, Normal, Roughness, Metalness, AmbientOcclusion, Emissive, Opacity }

/// <summary>A texture file matched to a material slot.</summary>
public sealed record TextureMatch
{
    public required int Material { get; init; }
    public required string MaterialName { get; init; }
    public required TextureSlot Slot { get; init; }
    public required string Path { get; init; }

    /// <summary>0..1: how sure the match is (exact names ~1, fuzzy name overlap lower).</summary>
    public float Confidence { get; init; }
    public string Reason { get; init; } = "";

    /// <summary>Already linked by the file (shown, never changed).</summary>
    public bool Linked { get; init; }

    /// <summary>Read from one channel of a packed map (ORM: R = AO, G = roughness, B = metalness).</summary>
    public TextureChannel Channel { get; init; } = TextureChannel.All;

    public string Key => $"{MaterialName}|{Slot}";

    public static string Label(TextureSlot slot) => slot switch
    {
        TextureSlot.BaseColor => "Base Color",
        TextureSlot.AmbientOcclusion => "AO",
        _ => slot.ToString(),
    };
}

/// <summary>
/// "Attach Textures": finds image files beside a weapon that belong to its materials but were
/// never linked (or are linked to paths on another machine), matches them to material slots by
/// name, and reports each match with a confidence. Matches at or above
/// <see cref="AutoAttach"/> are attached automatically; weaker ones are proposals the user
/// confirms. Linked textures are never replaced.
/// Ported from the original importer's TextureResolver (subject tokens, channel suffix,
/// ubiquitous-token rule), extended with confidences, packed maps and single-material weapons.
/// </summary>
public static class TextureMatcher
{
    public const float AutoAttach = 0.6f;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".psd", ".webp", ".dds" };

    private static readonly string[] TextureFolders = { "textures", "texture", "tex", "maps", "images", "materials", "mats", "texturas" };

    private static readonly (TextureSlot Slot, string[] Words)[] SlotWords =
    {
        (TextureSlot.BaseColor, new[] { "albedo", "basecolor", "diffuse", "diff", "dif", "color", "colour", "col", "alb", "bc", "d", "c" }),
        (TextureSlot.Normal, new[] { "normal", "normals", "nrm", "nor", "norm", "nm", "n", "normalmap", "nrml" }),
        (TextureSlot.Roughness, new[] { "roughness", "rough", "rgh", "r" }),
        (TextureSlot.Metalness, new[] { "metalness", "metallic", "metal", "mtl", "m" }),
        (TextureSlot.AmbientOcclusion, new[] { "ao", "occlusion", "ambientocclusion" }),
        (TextureSlot.Emissive, new[] { "emissive", "emission", "glow", "illum", "selfillum", "e" }),
        (TextureSlot.Opacity, new[] { "opacity", "alpha", "transparency", "mask" }),
    };

    /// <summary>Words that describe a texture's format rather than what it belongs to.</summary>
    private static readonly HashSet<string> FormatWords = new(StringComparer.Ordinal)
    {
        "opengl", "open", "gl", "ogl", "directx", "direct", "dx", "x", "map", "tex", "texture", "base", "mixed", "baked", "srgb",
        "1k", "2k", "4k", "8k", "t", "tx", "mat", "mi", "orm", "arm", "gloss", "glossiness", "spec", "specular", "height", "disp", "displacement", "bump",
    };

    /// <summary>Folders searched for images: the model's, its parent, and texture folders in both.</summary>
    public static List<string> SearchFolders(string modelPath)
    {
        var folders = new List<string>();
        void Add(string? folder, bool withTextureFolders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;
            var full = System.IO.Path.GetFullPath(folder);
            if (!folders.Contains(full, StringComparer.OrdinalIgnoreCase))
                folders.Add(full);
            if (!withTextureFolders)
                return;
            try
            {
                foreach (var sub in Directory.GetDirectories(full))
                    if (TextureFolders.Contains(System.IO.Path.GetFileName(sub).ToLowerInvariant()))
                        Add(sub, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(modelPath));
        Add(dir, true);
        Add(dir is null ? null : System.IO.Path.GetDirectoryName(dir), true);
        return folders;
    }

    public static List<string> FindImages(IEnumerable<string> folders)
    {
        var images = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                var recursive = TextureFolders.Contains(System.IO.Path.GetFileName(folder).ToLowerInvariant());
                foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = recursive, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
                    if (ImageExtensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()) && !images.Contains(file, StringComparer.OrdinalIgnoreCase))
                        images.Add(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder never fails an import.
            }
        }
        images.Sort(StringComparer.OrdinalIgnoreCase);
        return images;
    }

    /// <summary>Every slot of every material: linked ones (as found) and matches for the empty ones.</summary>
    public static List<TextureMatch> Match(WeaponAsset asset, string modelPath, IReadOnlyList<string>? images = null)
    {
        images ??= FindImages(SearchFolders(modelPath));
        var materials = asset.Mesh.Materials;
        var modelSubject = SubjectTokens(System.IO.Path.GetFileNameWithoutExtension(modelPath));
        var placeSubject = PlaceTokens(modelPath);
        // Words shared by most of this weapon's textures (its own name) are judged among the
        // images that relate to it, not every image in the neighbouring folders.
        var known = materials.SelectMany(m => SubjectTokens(m.Name)).Concat(modelSubject).ToHashSet(StringComparer.Ordinal);
        var related = images.Where(i => SubjectTokens(Stem(i)).Any(known.Contains)).ToList();
        var ubiquitous = UbiquitousTokens(related.Count >= 3 ? related : images);
        // A word that tells this weapon's materials apart ("glove" vs "shirt") is never its shared name.
        var perMaterial = materials.Select(m => MaterialTokens(m.Name, placeSubject).ToHashSet(StringComparer.Ordinal)).ToList();
        ubiquitous.RemoveWhere(t => perMaterial.Any(s => s.Contains(t)) && !perMaterial.All(s => s.Contains(t)));
        var result = new List<TextureMatch>();
        for (var m = 0; m < materials.Count; m++)
        {
            var material = materials[m];
            foreach (var slot in Enum.GetValues<TextureSlot>())
            {
                if (Get(material, slot) is { } linked)
                {
                    result.Add(new TextureMatch { Material = m, MaterialName = material.Name, Slot = slot, Path = linked.FilePath ?? $"(embedded) {linked.Name}", Confidence = 1f, Reason = "linked in the file", Linked = true });
                    continue;
                }
            }
            foreach (var match in MatchByName(m, material.Name, images, ubiquitous, materials.Count, modelSubject, placeSubject, perMaterial))
                if (Get(material, match.Slot) is null)
                    result.Add(match);
        }
        return result;
    }

    /// <summary>The asset with the given (non-linked) matches attached; linked slots are left alone.</summary>
    public static WeaponAsset Apply(WeaponAsset asset, IEnumerable<TextureMatch> matches)
    {
        var list = matches.Where(x => !x.Linked && File.Exists(x.Path)).ToList();
        if (list.Count == 0)
            return asset;
        var materials = asset.Mesh.Materials.ToList();
        foreach (var match in list)
        {
            if (match.Material < 0 || match.Material >= materials.Count || Get(materials[match.Material], match.Slot) is not null)
                continue;
            var tex = TextureRef.FromFile(match.Path) with { Channel = match.Channel };
            materials[match.Material] = Set(materials[match.Material], match.Slot, tex);
        }
        var mesh = asset.Mesh;
        var newMesh = new TriMesh(mesh.Positions, mesh.Indices, mesh.VertexBone, mesh.TrianglePart, mesh.PartNames, mesh.CornerNormals, mesh.CornerUVs, mesh.TriangleMaterial, materials);
        var copy = new WeaponAsset { Name = asset.Name, Kind = asset.Kind, SourcePath = asset.SourcePath, Skeleton = asset.Skeleton, Mesh = newMesh, Clips = asset.Clips, Attachments = asset.Attachments };
        copy.Notes.AddRange(asset.Notes);
        copy.Notes.Add($"Attached {list.Count} texture{(list.Count == 1 ? "" : "s")} found beside the model.");
        return copy;
    }

    public static TextureRef? Get(MaterialInfo m, TextureSlot slot) => slot switch
    {
        TextureSlot.BaseColor => m.BaseColorTexture,
        TextureSlot.Normal => m.NormalTexture,
        TextureSlot.Roughness => m.RoughnessTexture,
        TextureSlot.Metalness => m.MetalnessTexture,
        TextureSlot.AmbientOcclusion => m.AmbientOcclusionTexture,
        TextureSlot.Emissive => m.EmissiveTexture,
        TextureSlot.Opacity => m.OpacityTexture,
        _ => null,
    };

    private static MaterialInfo Set(MaterialInfo m, TextureSlot slot, TextureRef t) => slot switch
    {
        // A colour texture shows as is: drop the placeholder tint.
        TextureSlot.BaseColor => m with { BaseColorTexture = t, BaseColor = new System.Numerics.Vector4(1f, 1f, 1f, m.BaseColor.W) },
        TextureSlot.Normal => m with { NormalTexture = t },
        TextureSlot.Roughness => m with { RoughnessTexture = t },
        TextureSlot.Metalness => m with { MetalnessTexture = t },
        TextureSlot.AmbientOcclusion => m with { AmbientOcclusionTexture = t },
        TextureSlot.Emissive => m with { EmissiveTexture = t },
        TextureSlot.Opacity => m with { OpacityTexture = t },
        _ => m,
    };

    // ------------------------------------------------------------------ names

    private static string Stem(string path)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        // UDIM tile (".1001") and Blender duplicates (".001").
        var dot = stem.LastIndexOf('.');
        if (dot > 0 && stem[(dot + 1)..].All(char.IsDigit))
            stem = stem[..dot];
        return stem;
    }

    private static List<string> Words(string name)
    {
        var tokens = NameTokens.Split(name);
        var words = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            var w = tokens[i];
            // "4k" splits into "4" + "k": keep it one (format) word.
            if (w.All(char.IsDigit) && i + 1 < tokens.Length && tokens[i + 1] == "k")
            {
                words.Add(w + "k");
                i++;
                continue;
            }
            if (w.Length == 4 && w.All(char.IsDigit))
                continue;
            words.Add(Singular(w));
        }
        return words;
    }

    /// <summary>"gloves" and "glove" are the same word ("M_Gloves" / "T_Glove_normal").</summary>
    private static string Singular(string w) => w.Length > 3 && w[^1] == 's' && w[^2] != 's' && !char.IsDigit(w[^2]) ? w[..^1] : w;

    /// <summary>
    /// What a material is about. A generic one ("Weapon", "M_Gun") is named after the weapon
    /// itself: its textures carry the weapon's name, which the file and its folders ("spas-12/") hold.
    /// </summary>
    private static List<string> MaterialTokens(string materialName, List<string> placeSubject)
    {
        var tokens = SubjectTokens(materialName);
        return tokens.All(GenericMaterialWords.Contains) ? placeSubject : tokens;
    }

    /// <summary>Material names that say nothing about which part of the model they cover.</summary>
    private static readonly HashSet<string> GenericMaterialWords = new(StringComparer.Ordinal)
    {
        "m", "mi", "mat", "mtl", "material", "weapon", "gun", "main", "body", "default", "standard", "lambert", "phong", "blinn", "surface", "mesh", "model", "base",
    };

    /// <summary>Folders that say nothing about what the model is.</summary>
    private static readonly HashSet<string> GenericFolderWords = new(StringComparer.Ordinal)
    {
        "fbx", "gltf", "glb", "obj", "source", "model", "mesh", "export", "file", "asset", "content", "download", "weapon", "gun", "art", "3d", "blend", "blender", "ue5", "ue4", "unity", "rig",
    };

    /// <summary>What the model's file and its four nearest folders are named after.</summary>
    private static List<string> PlaceTokens(string modelPath)
    {
        var tokens = SubjectTokens(System.IO.Path.GetFileNameWithoutExtension(modelPath)).ToList();
        var dir = System.IO.Path.GetDirectoryName(modelPath);
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++, dir = System.IO.Path.GetDirectoryName(dir))
            tokens.AddRange(SubjectTokens(System.IO.Path.GetFileName(dir)));
        return tokens.Where(t => !GenericFolderWords.Contains(t) && !GenericMaterialWords.Contains(t) && !t.All(char.IsDigit)).Distinct().ToList();
    }

    /// <summary>Name words without channel/format words: what the texture or material is about.</summary>
    private static List<string> SubjectTokens(string name)
    {
        var words = Words(name);
        // Drop the trailing channel words ("rifle_base_color" -> rifle).
        while (words.Count > 0 && (FormatWords.Contains(words[^1]) || SlotOfWord(words[^1]) is not null))
            words.RemoveAt(words.Count - 1);
        return words.Where(w => !FormatWords.Contains(w)).ToList();
    }

    private static TextureSlot? SlotOfWord(string word)
    {
        foreach (var (slot, words) in SlotWords)
            if (words.Contains(word))
                return slot;
        return null;
    }

    /// <summary>
    /// The slots an image fills, from its last channel word ("rifle_base_color", "Gun_Normal_OpenGL",
    /// "gun_orm" = packed AO/roughness/metalness). Null: the name doesn't say.
    /// </summary>
    private static List<(TextureSlot Slot, TextureChannel Channel)>? SlotsOf(string stem)
    {
        var words = Words(stem);
        for (var i = words.Count - 1; i >= 0; i--)
        {
            var w = words[i];
            if (w is "opengl" or "open" or "gl" or "ogl" or "directx" or "direct" or "x" or "dx" or "srgb" or "1k" or "2k" or "4k" or "8k")
                continue;
            if (w is "orm" or "arm")
                return new() { (TextureSlot.AmbientOcclusion, TextureChannel.R), (TextureSlot.Roughness, TextureChannel.G), (TextureSlot.Metalness, TextureChannel.B) };
            if (i > 0 && words[i - 1] == "base" && w is "color" or "colour")
                return new() { (TextureSlot.BaseColor, TextureChannel.All) };
            if (i > 0 && words[i - 1] == "ambient" && w == "occlusion")
                return new() { (TextureSlot.AmbientOcclusion, TextureChannel.All) };
            // Single letters only count as a suffix after a separator ("gun_n", not "gun").
            if (w.Length == 1 && i == 0)
                return null;
            return SlotOfWord(w) is { } slot ? new() { (slot, TextureChannel.All) } : null;
        }
        return null;
    }

    private static HashSet<string> UbiquitousTokens(IReadOnlyList<string> images)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var image in images)
            foreach (var token in SubjectTokens(Stem(image)).Distinct())
                counts[token] = counts.GetValueOrDefault(token) + 1;
        return counts.Where(kv => kv.Value >= 3 && kv.Value * 2 >= images.Count).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<TextureMatch> MatchByName(int materialIndex, string materialName, IReadOnlyList<string> images, HashSet<string> ubiquitous, int materialCount, List<string> modelSubject, List<string> placeSubject, List<HashSet<string>> perMaterial)
    {
        var materialTokens = MaterialTokens(materialName, placeSubject);
        var best = new Dictionary<TextureSlot, TextureMatch>();
        foreach (var image in images)
        {
            var stem = Stem(image);
            var slots = SlotsOf(stem);
            var imageTokens = SubjectTokens(stem);
            float confidence;
            string reason;
            if (materialTokens.Count > 0 && imageTokens.Count > 0 && string.Concat(imageTokens) == string.Concat(materialTokens))
            {
                confidence = slots is null ? 0.7f : 0.95f;
                reason = "named after the material";
            }
            else if (materialCount == 1 && (imageTokens.Count == 0 || string.Concat(imageTokens) == string.Concat(modelSubject) || imageTokens.All(ubiquitous.Contains)))
            {
                // One material: textures named after the model (or unnamed) are its textures.
                if (slots is null)
                    continue;
                confidence = imageTokens.Count == 0 ? 0.65f : 0.8f;
                reason = "the weapon's only material";
            }
            else
            {
                if (materialTokens.Count == 0 || imageTokens.Count == 0)
                    continue;
                var shared = imageTokens.Where(materialTokens.Contains).Distinct().ToList();
                var distinctive = shared.Count(t => !ubiquitous.Contains(t));
                if (distinctive == 0)
                    continue;
                // Words every image shares (the asset's own name) say nothing about which part it is.
                var imageOwn = imageTokens.Where(t => !ubiquitous.Contains(t)).Distinct().ToList();
                var materialOwn = materialTokens.Where(t => !ubiquitous.Contains(t)).Distinct().ToList();
                var coverage = imageOwn.Count == 0 ? 0f : distinctive / (float)imageOwn.Count;
                var recall = materialOwn.Count == 0 ? 0f : distinctive / (float)materialOwn.Count;
                if (coverage < 0.5f)
                    continue;
                confidence = Math.Clamp(0.25f + 0.45f * coverage * recall + 0.1f * Math.Min(distinctive, 2), 0f, 0.9f);
                if (slots is null)
                    confidence *= 0.7f;
                reason = $"name shares \"{string.Join(" ", shared)}\"";
                // Everything the image is named after belongs to this material alone ("T_Glove_normal"
                // and only "M_Gloves_Black" says glove): as sure as a full name match.
                var own = shared.Where(t => !ubiquitous.Contains(t)).ToList();
                if (slots is not null && coverage >= 0.999f && own.All(t => perMaterial.Where((s, i) => i != materialIndex).All(s => !s.Contains(t))))
                {
                    confidence = MathF.Max(confidence, 0.65f);
                    reason = $"only this material is named \"{string.Join(" ", own)}\"";
                }
            }
            foreach (var (slot, channel) in slots ?? new() { (TextureSlot.BaseColor, TextureChannel.All) })
            {
                var match = new TextureMatch { Material = materialIndex, MaterialName = materialName, Slot = slot, Path = image, Confidence = confidence, Reason = reason, Channel = channel };
                if (!best.TryGetValue(slot, out var current) || confidence > current.Confidence + 1e-4f
                    || (MathF.Abs(confidence - current.Confidence) <= 1e-4f && Rank(image) < Rank(current.Path)))
                    best[slot] = match;
            }
        }
        return best.Values;
    }

    /// <summary>Between equally good images prefer engine-friendly formats and OpenGL normals.</summary>
    private static int Rank(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var rank = ext switch { ".png" => 0, ".tga" => 1, ".jpg" => 2, ".jpeg" => 3, _ => 4 };
        if (path.Contains("directx", StringComparison.OrdinalIgnoreCase) || path.Contains("_dx", StringComparison.OrdinalIgnoreCase))
            rank += 10;
        return rank;
    }
}
