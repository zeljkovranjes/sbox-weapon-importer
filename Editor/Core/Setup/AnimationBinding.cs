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

public sealed class AnimationBinding
{
    public string Clip { get; set; } = "";

    /// <summary>More clips played in turn with <see cref="Clip"/> (left and right punches, a combo of slashes).</summary>
    public List<string> Variants { get; set; } = new();
    public float Confidence { get; set; }
    public bool Manual { get; set; }
}
