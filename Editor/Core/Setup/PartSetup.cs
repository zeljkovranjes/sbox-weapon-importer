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

public sealed class PartSetup
{
    public PartKind Kind { get; set; }
    public string Bone { get; set; } = "";
    public List<string> Meshes { get; set; } = new();
    public float Confidence { get; set; }
    public bool Manual { get; set; }
    public float[] Center { get; set; } = new float[3];
}
