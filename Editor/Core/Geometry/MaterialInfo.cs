#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.EditorTools.Core.Geometry;

using Vector4 = System.Numerics.Vector4;

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
