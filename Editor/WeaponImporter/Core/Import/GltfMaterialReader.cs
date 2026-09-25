#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using WeaponImporter.Core.Formats.Gltf;
using WeaponImporter.Core.Geometry;

namespace WeaponImporter.Core.Import;

using Vector4 = System.Numerics.Vector4;

/// <summary>
/// glTF materials (core metallic-roughness model) and their images. Image sources: a relative
/// file uri (resolved next to the glTF), a base64 data: uri, or a bufferView (GLB embedded).
/// </summary>
internal sealed class GltfMaterialReader
{
    private readonly JsonElement _root;
    private readonly GltfDocument _document;
    private readonly string _folder;
    private readonly Func<string, byte[]>? _external;
    private readonly Dictionary<int, TextureRef?> _images = new();
    private readonly Dictionary<int, int> _materialIndex = new();
    private int _defaultIndex = -1;

    public List<MaterialInfo> Materials { get; } = new();

    public List<string> Notes { get; } = new();

    public GltfMaterialReader(GltfDocument document, string sourcePath, Func<string, byte[]>? external)
    {
        _document = document;
        _root = document.Root;
        _folder = string.IsNullOrEmpty(sourcePath) ? "" : Path.GetDirectoryName(sourcePath) ?? "";
        _external = external;
    }

    /// <summary>Index into <see cref="Materials"/> for a glTF material index (-1 = default material).</summary>
    public int MaterialIndex(int gltfMaterial)
    {
        if (!_root.TryGetProperty("materials", out var materials) || materials.ValueKind != JsonValueKind.Array
            || gltfMaterial < 0 || gltfMaterial >= materials.GetArrayLength())
        {
            if (_defaultIndex < 0)
            {
                _defaultIndex = Materials.Count;
                Materials.Add(MaterialInfo.Default);
            }
            return _defaultIndex;
        }
        if (_materialIndex.TryGetValue(gltfMaterial, out var index))
            return index;
        index = Materials.Count;
        Materials.Add(Build(materials[gltfMaterial], gltfMaterial));
        _materialIndex[gltfMaterial] = index;
        return index;
    }

    private MaterialInfo Build(JsonElement m, int index)
    {
        var name = m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s ? s : $"material_{index}";
        var baseColor = Vector4.One;
        float metallic = 1f, roughness = 1f;
        TextureRef? baseTex = null, mrTex = null;
        if (m.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.ValueKind == JsonValueKind.Object)
        {
            if (pbr.TryGetProperty("baseColorFactor", out var bcf) && bcf.ValueKind == JsonValueKind.Array && bcf.GetArrayLength() >= 4)
                baseColor = new Vector4(bcf[0].GetSingle(), bcf[1].GetSingle(), bcf[2].GetSingle(), bcf[3].GetSingle());
            if (pbr.TryGetProperty("metallicFactor", out var mf) && mf.ValueKind == JsonValueKind.Number)
                metallic = mf.GetSingle();
            if (pbr.TryGetProperty("roughnessFactor", out var rf) && rf.ValueKind == JsonValueKind.Number)
                roughness = rf.GetSingle();
            baseTex = TextureOf(pbr, "baseColorTexture");
            mrTex = TextureOf(pbr, "metallicRoughnessTexture");
            if (mrTex is not null)
                mrTex = mrTex with { Packed = true };
        }
        var occlusion = TextureOf(m, "occlusionTexture");
        if (occlusion is not null)
            occlusion = occlusion with { Channel = TextureChannel.R };

        var alphaMode = m.TryGetProperty("alphaMode", out var am) && am.ValueKind == JsonValueKind.String ? am.GetString() : "OPAQUE";
        float? cutoff = null;
        if (alphaMode == "MASK")
            cutoff = m.TryGetProperty("alphaCutoff", out var ac) && ac.ValueKind == JsonValueKind.Number ? ac.GetSingle() : 0.5f;

        return new MaterialInfo
        {
            Name = name,
            BaseColor = baseColor,
            BaseColorTexture = baseTex,
            NormalTexture = TextureOf(m, "normalTexture"),
            RoughnessTexture = mrTex,
            MetalnessTexture = mrTex,
            AmbientOcclusionTexture = occlusion,
            EmissiveTexture = TextureOf(m, "emissiveTexture"),
            AlphaCutoff = cutoff,
            Translucent = alphaMode == "BLEND",
            DoubleSided = m.TryGetProperty("doubleSided", out var ds) && ds.ValueKind == JsonValueKind.True,
            Roughness = Math.Clamp(roughness, 0f, 1f),
            Metalness = Math.Clamp(metallic, 0f, 1f),
        };
    }

    private TextureRef? TextureOf(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("index", out var ti) || ti.ValueKind != JsonValueKind.Number)
            return null;
        if (!_root.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Array)
            return null;
        var t = ti.GetInt32();
        if (t < 0 || t >= textures.GetArrayLength())
            return null;
        var texture = textures[t];
        var source = -1;
        if (texture.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Number)
            source = src.GetInt32();
        else if (texture.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Object)
            foreach (var e in ext.EnumerateObject())
                if (e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("source", out var es) && es.ValueKind == JsonValueKind.Number)
                {
                    source = es.GetInt32();
                    break;
                }
        return source >= 0 ? Image(source) : null;
    }

    private TextureRef? Image(int index)
    {
        if (_images.TryGetValue(index, out var cached))
            return cached;
        TextureRef? result = null;
        try
        {
            result = LoadImage(index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            Notes.Add($"glTF image {index} could not be read: {e.Message}");
        }
        if (result is null)
            Notes.Add($"glTF image {index} was not found.");
        _images[index] = result;
        return result;
    }

    private TextureRef? LoadImage(int index)
    {
        if (!_root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array || index >= images.GetArrayLength())
            return null;
        var image = images[index];
        var mime = image.TryGetProperty("mimeType", out var mt) && mt.ValueKind == JsonValueKind.String ? mt.GetString() ?? "" : "";
        var imageName = image.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "";
        if (image.TryGetProperty("uri", out var uriProp) && uriProp.ValueKind == JsonValueKind.String && uriProp.GetString() is { Length: > 0 } uri)
        {
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = uri.IndexOf(',');
                if (comma < 0)
                    return null;
                var header = uri[5..comma];
                var semi = header.IndexOf(';');
                var dataMime = semi >= 0 ? header[..semi] : header;
                var bytes = Convert.FromBase64String(uri[(comma + 1)..]);
                return TextureRef.FromBytes(bytes, mime.Length > 0 ? mime : dataMime, imageName.Length > 0 ? imageName : $"image_{index}");
            }
            var relative = Uri.UnescapeDataString(uri);
            if (_folder.Length > 0)
            {
                var path = Path.GetFullPath(Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(path))
                    return TextureRef.FromFile(path);
            }
            if (_external is not null)
            {
                var bytes = _external(uri);
                return TextureRef.FromBytes(bytes, TextureRef.ExtensionOf(relative), TextureRef.FileNameOf(relative));
            }
            return null;
        }
        if (image.TryGetProperty("bufferView", out var bvProp) && bvProp.ValueKind == JsonValueKind.Number
            && _root.TryGetProperty("bufferViews", out var views) && views.ValueKind == JsonValueKind.Array)
        {
            var view = views[bvProp.GetInt32()];
            var buffer = _document.Buffers[view.GetProperty("buffer").GetInt32()];
            var offset = view.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
            var length = view.GetProperty("byteLength").GetInt32();
            if (offset < 0 || length < 0 || (long)offset + length > buffer.Length)
                throw new FormatException($"image {index} reads past the end of its buffer.");
            var bytes = new byte[length];
            Array.Copy(buffer, offset, bytes, 0, length);
            return TextureRef.FromBytes(bytes, mime, imageName.Length > 0 ? imageName : $"image_{index}");
        }
        return null;
    }
}
