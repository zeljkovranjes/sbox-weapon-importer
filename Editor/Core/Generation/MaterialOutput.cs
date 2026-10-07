#nullable enable annotations

using System.Globalization;
using System.Numerics;
using System.Text;
using WeaponImporter.EditorTools.Core.Geometry;

namespace WeaponImporter.EditorTools.Core.Generation;

/// <summary>A generated material: vmat text plus the texture files the caller must produce.</summary>
public sealed class MaterialOutput
{
    /// <summary>Asset-relative path of the vmat (forward slashes), e.g. "weapons/ak74/materials/ak74m.vmat".</summary>
    public required string VmatPath { get; init; }

    public required string VmatText { get; init; }

    /// <summary>
    /// Texture files referenced by the vmat. <c>Channel == All</c>: copy / extract the source as
    /// is. Any other channel: decode the source image and write that single channel as a
    /// grayscale PNG (the editor side does this; Core has no image decoder).
    /// </summary>
    public required List<(string RelativePath, TextureRef Source, TextureChannel Channel)> Textures { get; init; }
}
