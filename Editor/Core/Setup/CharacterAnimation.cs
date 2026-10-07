#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

using Vector3 = System.Numerics.Vector3;

public sealed class CharacterAnimation
{
    public CharacterAnimationSource Source { get; set; } = CharacterAnimationSource.Graph;
    public string Model { get; set; } = "";
    public string Sequence { get; set; } = "";
    /// <summary>More sequences of the same model played in turn (attack combos).</summary>
    public List<string> Variants { get; set; } = new();
    public bool Manual { get; set; }

    public string Describe() => Source switch
    {
        CharacterAnimationSource.Graph => "Character animgraph",
        CharacterAnimationSource.Sequence => string.IsNullOrEmpty(Sequence) ? "(no sequence)" : Sequence,
        _ => "None",
    };
}
