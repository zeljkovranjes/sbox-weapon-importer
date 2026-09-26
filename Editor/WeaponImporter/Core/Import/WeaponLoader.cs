#nullable enable annotations

using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Import;

/// <summary>An import failure with a message written for the person importing.</summary>
public sealed class WeaponImportException : Exception
{
    public WeaponImportException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Loads FBX / glTF / GLB sources into a <see cref="WeaponAsset"/>.</summary>
public static class WeaponLoader
{
    public static readonly string[] SourceExtensions = { ".fbx", ".glb", ".gltf" };

    /// <summary>All formats the importer accepts, including engine models loaded in the editor.</summary>
    public static readonly string[] AllExtensions = { ".fbx", ".glb", ".gltf", ".vmdl" };

    public static bool IsSource(string path) => SourceExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsSupported(string path) => AllExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>A lowercase identifier made from the file name, safe for asset paths.</summary>
    private static readonly string[] GenericStems = { "scene", "model", "untitled", "export", "mesh", "object", "default", "main", "weapon", "gun", "asset", "file", "source" };

    public static string SafeName(string path)
    {
        var raw = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        // "scene.gltf" in "fps_ak-74m_animations/" is better named after its folder.
        if (GenericStems.Contains(raw) && Path.GetFileName(Path.GetDirectoryName(path) ?? "") is { Length: > 0 } folder)
            raw = folder.ToLowerInvariant();
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var name = new string(chars).Trim('_');
        while (name.Contains("__"))
            name = name.Replace("__", "_");
        if (name.Length == 0)
            name = "weapon";
        if (char.IsDigit(name[0]))
            name = "w_" + name;
        return name.Length > 48 ? name[..48] : name;
    }

    /// <param name="withAnimationFiles">Also add the clips of animation-only files beside it (see <see cref="AnimationFiles"/>).</param>
    public static WeaponAsset Load(string path, bool withAnimationFiles = true)
    {
        if (!File.Exists(path))
            throw new WeaponImportException($"File not found: {path}");
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            throw new WeaponImportException($"Could not read {Path.GetFileName(path)}: {e.Message}", e);
        }
        var dir = Path.GetDirectoryName(path) ?? "";
        var asset = Load(data, path, uri => File.ReadAllBytes(Path.Combine(dir, Uri.UnescapeDataString(uri))));
        return withAnimationFiles ? AnimationFiles.Merge(asset, path) : asset;
    }

    public static WeaponAsset Load(byte[] data, string path, Func<string, byte[]>? externalBuffers = null)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = SafeName(path);
        if (data.Length == 0)
            throw new WeaponImportException($"{Path.GetFileName(path)} is empty.");
        try
        {
            var asset = ext switch
            {
                ".fbx" => FbxWeaponReader.Read(data, name, path),
                ".glb" or ".gltf" => GltfWeaponReader.Read(data, name, path, externalBuffers),
                _ => throw new WeaponImportException($"{ext} files are not supported. Use FBX, GLB, glTF or a VMDL."),
            };
            return MaterialFallbacks.Apply(asset, path);
        }
        catch (WeaponImportException)
        {
            throw;
        }
        catch (Exception e) when (e is FormatException or EndOfStreamException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException or System.Text.Json.JsonException or NotSupportedException or ArithmeticException)
        {
            throw new WeaponImportException($"{Path.GetFileName(path)} could not be read: {e.Message}", e);
        }
    }
}
