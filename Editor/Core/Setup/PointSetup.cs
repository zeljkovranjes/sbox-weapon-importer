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

/// <summary>A point with a direction attached to a weapon bone (bone-local, model units).</summary>
public sealed class PointSetup
{
    public string Bone { get; set; } = "";
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = { 0, 0, 0, 1 };
    /// <summary>Same point in canonical space (for display).</summary>
    public float[] Canonical { get; set; } = new float[3];
    public float Confidence { get; set; }
    public bool Manual { get; set; }

    [JsonIgnore] public XForm Local => new(V.Of(Position), V.Q(Rotation));
}
