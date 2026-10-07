#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Formats.Dmx;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Generation;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>A written model DMX plus the faceSet material names it uses.</summary>
public sealed class ModelDmxResult
{
    public required string Text { get; init; }

    /// <summary>Sanitised, unique faceSet material name and the source material, in file order.</summary>
    public required IReadOnlyList<(string DmxMaterial, MaterialInfo Material)> Materials { get; init; }
}
