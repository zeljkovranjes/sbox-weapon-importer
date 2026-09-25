#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.Core.Geometry;

using Vector4 = System.Numerics.Vector4;

/// <summary>A colour channel of an image; <see cref="All"/> means the whole image.</summary>
public enum TextureChannel { All, R, G, B, A }

/// <summary>
/// An image a material uses: either a file on disk (<see cref="FilePath"/>, absolute) or bytes
/// embedded in the source file (<see cref="Bytes"/>). <see cref="Name"/> is a stable,
/// file-system-safe identifier (no extension) derived from the source.
/// </summary>
public sealed record TextureRef
{
    /// <summary>Absolute path of the image, or null when the image is embedded.</summary>
    public string? FilePath { get; init; }

    /// <summary>Embedded image bytes (FBX Video Content, glTF bufferView / data URI).</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>File extension including the dot, lowercase (".png", ".jpg", ".tga").</summary>
    public string Extension { get; init; } = ".png";

    /// <summary>Stable name of the image (source file name without extension, or glTF image name).</summary>
    public string Name { get; init; } = "texture";

    /// <summary>
    /// glTF metallicRoughness packing: G = roughness, B = metalness. The same instance is then
    /// used for both <see cref="MaterialInfo.RoughnessTexture"/> and <see cref="MaterialInfo.MetalnessTexture"/>.
    /// </summary>
    public bool Packed { get; init; }

    /// <summary>Channel a scalar map reads (glTF occlusion reads R). All = the image as is.</summary>
    public TextureChannel Channel { get; init; } = TextureChannel.All;

    public bool IsEmbedded => Bytes is not null;

    public static TextureRef FromFile(string path) => new()
    {
        FilePath = path,
        Extension = ExtensionOf(path),
        Name = SafeName(FileNameOf(path)),
    };

    public static TextureRef FromBytes(byte[] bytes, string extension, string name) => new()
    {
        Bytes = bytes,
        Extension = NormalizeExtension(extension, bytes),
        Name = SafeName(name),
    };

    /// <summary>Reads the image bytes (file or embedded).</summary>
    public byte[] ReadBytes() => Bytes ?? File.ReadAllBytes(FilePath ?? throw new InvalidOperationException("Texture has no source."));

    /// <summary>File name part of a path written with either separator.</summary>
    public static string FileNameOf(string path)
    {
        var cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    public static string ExtensionOf(string path)
    {
        var file = FileNameOf(path);
        var dot = file.LastIndexOf('.');
        return dot >= 0 ? file[dot..].ToLowerInvariant() : "";
    }

    /// <summary>Lowercase [a-z0-9_] name without extension.</summary>
    public static string SafeName(string raw)
    {
        var file = FileNameOf(raw);
        var dot = file.LastIndexOf('.');
        if (dot > 0)
            file = file[..dot];
        var chars = file.ToLowerInvariant().Select(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '_').ToArray();
        var s = new string(chars);
        while (s.Contains("__"))
            s = s.Replace("__", "_");
        s = s.Trim('_');
        return s.Length == 0 ? "texture" : s;
    }

    private static string NormalizeExtension(string extension, byte[] bytes)
    {
        var e = (extension ?? "").Trim().ToLowerInvariant();
        e = e switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            _ => e,
        };
        if (e.Length > 0 && e[0] != '.')
            e = "." + e;
        if (e.Length > 1)
            return e;
        // Sniff magic numbers.
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        return ".png";
    }
}

/// <summary>
/// A render material read from the source (FBX Material / glTF material), engine agnostic.
/// Scalars follow glTF metallic-roughness semantics: when a texture is present the scalar
/// multiplies it.
/// </summary>
public sealed record MaterialInfo
{
    public string Name { get; init; } = "default";

    /// <summary>Linear-ish RGBA base colour factor (alpha = opacity).</summary>
    public Vector4 BaseColor { get; init; } = Vector4.One;

    public TextureRef? BaseColorTexture { get; init; }
    public TextureRef? NormalTexture { get; init; }
    public TextureRef? RoughnessTexture { get; init; }
    public TextureRef? MetalnessTexture { get; init; }
    public TextureRef? AmbientOcclusionTexture { get; init; }
    public TextureRef? EmissiveTexture { get; init; }

    /// <summary>Separate opacity image (FBX TransparentColor/Opacity map). Null = base colour alpha.</summary>
    public TextureRef? OpacityTexture { get; init; }

    /// <summary>Alpha-test threshold (glTF MASK); null = no alpha test.</summary>
    public float? AlphaCutoff { get; init; }

    /// <summary>Alpha-blended (glTF BLEND / FBX opacity below 1).</summary>
    public bool Translucent { get; init; }

    public bool DoubleSided { get; init; }

    public float Roughness { get; init; } = 1f;
    public float Metalness { get; init; }

    public static MaterialInfo Default { get; } = new();
}
