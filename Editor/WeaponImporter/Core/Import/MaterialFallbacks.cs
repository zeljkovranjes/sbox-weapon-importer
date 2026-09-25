#nullable enable annotations

using System.Globalization;
using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Import;

/// <summary>
/// Fills in materials the source file left blank: first from a matching Wavefront .mtl shipped
/// alongside the model (many packs keep colours and textures only there), then from material
/// names (wood, metal, polymer, brass...) so an uncoloured weapon still reads correctly.
/// </summary>
public static class MaterialFallbacks
{
    public static WeaponAsset Apply(WeaponAsset asset, string sourcePath)
    {
        var materials = asset.Mesh.Materials;
        if (materials.All(m => !IsBlank(m)))
            return asset;

        var mtl = FindMtl(sourcePath);
        var fromMtl = mtl is null ? new Dictionary<string, MaterialInfo>() : ReadMtl(mtl);
        var changed = false;
        var result = materials.Select(m =>
        {
            if (!IsBlank(m))
                return m;
            if (fromMtl.TryGetValue(m.Name, out var mm) && !IsBlank(mm))
            {
                changed = true;
                return m with { BaseColor = mm.BaseColor, BaseColorTexture = mm.BaseColorTexture ?? m.BaseColorTexture, NormalTexture = mm.NormalTexture ?? m.NormalTexture, Roughness = mm.Roughness };
            }
            var guess = ByName(m);
            if (guess != m)
                changed = true;
            return guess;
        }).ToList();
        if (!changed)
            return asset;

        var mesh = asset.Mesh;
        var newMesh = new TriMesh(mesh.Positions, mesh.Indices, mesh.VertexBone, mesh.TrianglePart, mesh.PartNames, mesh.CornerNormals, mesh.CornerUVs, mesh.TriangleMaterial, result);
        var copy = new WeaponAsset { Name = asset.Name, Kind = asset.Kind, SourcePath = asset.SourcePath, Skeleton = asset.Skeleton, Mesh = newMesh, Clips = asset.Clips, Attachments = asset.Attachments };
        copy.Notes.AddRange(asset.Notes);
        copy.Notes.Add(mtl is not null && fromMtl.Count > 0 ? $"Materials completed from {Path.GetFileName(mtl)}." : "The file has no material colours; colours were chosen from material names.");
        return copy;
    }

    /// <summary>No texture and a flat default grey/white colour (what exporters write when nothing was set).</summary>
    public static bool IsBlank(MaterialInfo m)
    {
        if (m.BaseColorTexture is not null)
            return false;
        var c = m.BaseColor;
        var grey = MathF.Abs(c.X - c.Y) < 0.02f && MathF.Abs(c.Y - c.Z) < 0.02f;
        return grey && (MathF.Abs(c.X - 0.8f) < 0.05f || MathF.Abs(c.X - 0.64f) < 0.05f || c.X > 0.97f || MathF.Abs(c.X - 0.5f) < 0.02f);
    }

    private static string? FindMtl(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return null;
        var dir = Path.GetDirectoryName(sourcePath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var candidates = new List<string>
        {
            Path.Combine(dir, stem + ".mtl"),
            Path.Combine(dir, "..", "OBJ", stem + ".mtl"),
            Path.Combine(dir, "..", "obj", stem + ".mtl"),
            Path.Combine(dir, "OBJ", stem + ".mtl"),
        };
        var parent = Path.GetDirectoryName(dir);
        if (parent is not null && Directory.Exists(parent))
            foreach (var sibling in Directory.EnumerateDirectories(parent))
                candidates.Add(Path.Combine(sibling, stem + ".mtl"));
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, MaterialInfo> ReadMtl(string path)
    {
        var result = new Dictionary<string, MaterialInfo>(StringComparer.OrdinalIgnoreCase);
        MaterialInfo? current = null;
        string? name = null;
        var dir = Path.GetDirectoryName(path) ?? "";
        void Flush()
        {
            if (name is not null && current is not null)
                result[name] = current;
        }
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            float F(int i) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            switch (parts[0].ToLowerInvariant())
            {
                case "newmtl":
                    Flush();
                    name = line[6..].Trim();
                    current = MaterialInfo.Default with { Name = name };
                    break;
                case "kd" when current is not null:
                    current = current with { BaseColor = new Vector4(F(1), F(2), F(3), current.BaseColor.W) };
                    break;
                case "ns" when current is not null:
                    // Blinn-Phong exponent -> roughness.
                    current = current with { Roughness = Math.Clamp(MathF.Sqrt(2f / (F(1) + 2f)), 0.05f, 1f) };
                    break;
                case "map_kd" when current is not null:
                    var tex = Path.Combine(dir, parts[^1]);
                    if (File.Exists(tex))
                        current = current with { BaseColorTexture = TextureRef.FromFile(tex) };
                    break;
                case "map_bump" or "bump" or "norm" when current is not null:
                    var n = Path.Combine(dir, parts[^1]);
                    if (File.Exists(n))
                        current = current with { NormalTexture = TextureRef.FromFile(n) };
                    break;
            }
        }
        Flush();
        return result;
    }

    private static readonly (string[] Words, Vector4 Color, float Roughness, float Metalness)[] Palette =
    {
        (new[] { "wood", "wooden", "stock", "handguard", "furniture", "walnut" }, new Vector4(0.42f, 0.26f, 0.14f, 1f), 0.7f, 0f),
        (new[] { "gold", "brass", "bullet", "bullets", "shell", "casing", "round", "rounds", "cartridge" }, new Vector4(0.78f, 0.6f, 0.28f, 1f), 0.35f, 1f),
        (new[] { "copper" }, new Vector4(0.72f, 0.45f, 0.3f, 1f), 0.35f, 1f),
        (new[] { "darkermetal", "darkmetal", "black", "blackmetal", "gunmetal", "blued", "muzzle", "barrel", "suppressor", "silencer" }, new Vector4(0.1f, 0.1f, 0.11f, 1f), 0.45f, 0.8f),
        (new[] { "metal", "steel", "iron", "chrome", "silver", "slide", "receiver", "frame", "bolt" }, new Vector4(0.32f, 0.33f, 0.35f, 1f), 0.4f, 0.9f),
        (new[] { "magazine", "mag", "clip", "drum" }, new Vector4(0.16f, 0.16f, 0.17f, 1f), 0.55f, 0.4f),
        (new[] { "grip", "polymer", "plastic", "rubber", "handle" }, new Vector4(0.08f, 0.08f, 0.09f, 1f), 0.8f, 0f),
        (new[] { "glass", "lens", "scopeglass" }, new Vector4(0.05f, 0.08f, 0.12f, 1f), 0.05f, 0f),
        (new[] { "leather", "strap", "sling" }, new Vector4(0.3f, 0.18f, 0.1f, 1f), 0.8f, 0f),
        (new[] { "blade", "edge" }, new Vector4(0.62f, 0.63f, 0.66f, 1f), 0.25f, 1f),
    };

    /// <summary>A plausible colour for a named but uncoloured material.</summary>
    public static MaterialInfo ByName(MaterialInfo m)
    {
        var tokens = NameTokens.Split(m.Name);
        var joined = NameTokens.Joined(tokens);
        foreach (var (words, color, roughness, metalness) in Palette)
            if (words.Any(w => tokens.Contains(w) || joined.Contains(w, StringComparison.Ordinal)))
                return m with { BaseColor = color, Roughness = roughness, Metalness = metalness };
        // Unknown names: a neutral dark gunmetal reads better than primer grey.
        return m with { BaseColor = new Vector4(0.22f, 0.22f, 0.23f, 1f), Roughness = 0.5f, Metalness = 0.5f };
    }
}
