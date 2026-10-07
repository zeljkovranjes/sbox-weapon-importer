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

public enum CharacterAnimationSource
{
    /// <summary>The character's own animgraph action (holdtype + trigger parameter).</summary>
    Graph,
    /// <summary>A specific sequence from a model (the weapon's retargeted clips or any project model).</summary>
    Sequence,
    /// <summary>Nothing plays for this role.</summary>
    None,
}
