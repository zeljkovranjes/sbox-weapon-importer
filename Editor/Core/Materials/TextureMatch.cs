#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Materials;

/// <summary>A texture file matched to a material slot.</summary>
public sealed record TextureMatch
{
    public required int Material { get; init; }
    public required string MaterialName { get; init; }
    public required TextureSlot Slot { get; init; }
    public required string Path { get; init; }

    /// <summary>0..1: how sure the match is (exact names ~1, fuzzy name overlap lower).</summary>
    public float Confidence { get; init; }
    public string Reason { get; init; } = "";

    /// <summary>Already linked by the file (shown, never changed).</summary>
    public bool Linked { get; init; }

    /// <summary>Read from one channel of a packed map (ORM: R = AO, G = roughness, B = metalness).</summary>
    public TextureChannel Channel { get; init; } = TextureChannel.All;

    public string Key => $"{MaterialName}|{Slot}";

    public static string Label(TextureSlot slot) => slot switch
    {
        TextureSlot.BaseColor => "Base Color",
        TextureSlot.AmbientOcclusion => "AO",
        _ => slot.ToString(),
    };
}
