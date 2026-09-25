#nullable enable annotations

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeaponImporter.Core.Output;

/// <summary>A component on a generated prefab object: type name plus property values.</summary>
public sealed record PrefabComponent(string Type, IReadOnlyDictionary<string, JsonNode?> Properties);

/// <summary>A GameObject in a generated prefab.</summary>
public sealed class PrefabObject
{
    public required string Name { get; init; }
    public string Tags { get; init; } = "";
    public System.Numerics.Vector3 Position { get; init; }
    public System.Numerics.Quaternion Rotation { get; init; } = System.Numerics.Quaternion.Identity;
    public float Scale { get; init; } = 1f;
    public List<PrefabComponent> Components { get; } = new();
    public List<PrefabObject> Children { get; } = new();
}

/// <summary>
/// Writes s&amp;box prefab JSON. GUIDs are derived from the prefab path and each object's role,
/// so re-baking keeps references stable.
/// </summary>
public static class PrefabBuilder
{
    public static string Build(string prefabPath, PrefabObject root)
    {
        var doc = new JsonObject
        {
            ["RootObject"] = Object(prefabPath, root, "root"),
            ["ShowInMenu"] = false,
            ["MenuPath"] = null,
            ["MenuIcon"] = null,
            ["DontBreakAsTemplate"] = false,
            ["ResourceVersion"] = 2,
            ["__references"] = new JsonArray(),
            ["__version"] = 2,
        };
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n");
    }

    private static JsonObject Object(string prefab, PrefabObject o, string role)
    {
        var components = new JsonArray();
        var index = 0;
        foreach (var c in o.Components)
        {
            var node = new JsonObject
            {
                ["__type"] = c.Type,
                ["__guid"] = Guid(prefab, $"{role}/component:{index++}:{c.Type}"),
            };
            foreach (var (k, v) in c.Properties)
                node[k] = v?.DeepClone();
            components.Add(node);
        }
        var children = new JsonArray();
        foreach (var child in o.Children)
            children.Add(Object(prefab, child, $"{role}/{child.Name}"));
        return new JsonObject
        {
            ["__guid"] = Guid(prefab, role),
            ["__version"] = 1,
            ["Flags"] = 0,
            ["Name"] = o.Name,
            ["Position"] = $"{F(o.Position.X)},{F(o.Position.Y)},{F(o.Position.Z)}",
            ["Rotation"] = $"{F(o.Rotation.X)},{F(o.Rotation.Y)},{F(o.Rotation.Z)},{F(o.Rotation.W)}",
            ["Scale"] = $"{F(o.Scale)},{F(o.Scale)},{F(o.Scale)}",
            ["Tags"] = o.Tags,
            ["Enabled"] = true,
            ["NetworkMode"] = 2,
            ["NetworkInterpolation"] = true,
            ["NetworkOrphaned"] = 0,
            ["OwnerTransfer"] = 1,
            ["Components"] = components,
            ["Children"] = children,
        };
    }

    private static string F(float v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Stable name-based GUID (SHA-256 of prefab path + role, formatted as a v5-style GUID).</summary>
    public static string Guid(string prefab, string role)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefab.ToLowerInvariant() + "|" + role));
        var bytes = hash.Take(16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new System.Guid(bytes).ToString("D");
    }

    /// <summary>The SkinnedModelRenderer entry for a weapon model.</summary>
    public static PrefabComponent ModelRenderer(string model, bool useAnimGraph = false) => new("Sandbox.SkinnedModelRenderer", new Dictionary<string, JsonNode?>
    {
        ["__enabled"] = true,
        ["BodyGroups"] = 18446744073709551615UL,
        ["CreateAttachments"] = true,
        ["CreateBoneObjects"] = false,
        ["Model"] = model,
        ["RenderType"] = "On",
        ["Tint"] = "1,1,1,1",
        ["UseAnimGraph"] = useAnimGraph,
        ["PlaybackRate"] = 1,
    });
}
