#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace WeaponImporter.EditorTools.Core.Formats.Gltf;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Editor/Core/Assembly.cs)

/// <summary>One glTF animation with its decoded rotation/translation channels.</summary>
internal sealed class GltfAnimation
{
    public string? Name;
    public List<GltfChannel> Channels { get; } = new();
}
