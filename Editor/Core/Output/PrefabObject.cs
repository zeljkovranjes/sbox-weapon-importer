#nullable enable annotations

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeaponImporter.EditorTools.Core.Output;

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
