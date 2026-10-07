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

public sealed class GripSetup
{
    public GripStyle Style { get; set; } = GripStyle.Wrap;
    public float[] Contact { get; set; } = new float[3];
    public float[] Normal { get; set; } = { 0, 0, -1 };
    public float[] Axis { get; set; } = { 1, 0, 0 };
    public float Confidence { get; set; }
    public string Reason { get; set; } = "";
    public bool Manual { get; set; }

    /// <summary>Hand pose edited by the user, replacing the generated fit.</summary>
    public HandPoseSetup? Pose { get; set; }

    /// <summary>
    /// Grip to use from the library ("" = automatic: every library grip and the procedural fit
    /// compete, the best fit wins; "Procedural" = only the procedural fit).
    /// </summary>
    public string Preset { get; set; } = "";

    public bool Enabled { get; set; } = true;
}
