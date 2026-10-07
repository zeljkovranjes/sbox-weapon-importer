#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.EditorTools.Core.Geometry;

using Vector4 = System.Numerics.Vector4;

/// <summary>A colour channel of an image; <see cref="All"/> means the whole image.</summary>
public enum TextureChannel { All, R, G, B, A }
