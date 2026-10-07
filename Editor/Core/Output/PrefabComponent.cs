#nullable enable annotations

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeaponImporter.EditorTools.Core.Output;

/// <summary>A component on a generated prefab object: type name plus property values.</summary>
public sealed record PrefabComponent(string Type, IReadOnlyDictionary<string, JsonNode?> Properties);
