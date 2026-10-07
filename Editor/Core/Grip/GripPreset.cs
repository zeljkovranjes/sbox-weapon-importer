#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Setup;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A hand grip captured from an animation (any rig) in a form that transfers to another hand
/// and another weapon: finger angles, and the palm's place and orientation relative to the grip
/// surface it holds, normalised by hand length. Anatomical axes (fingers, palm) are used, never
/// bone axes, so rigs with different bone orientations agree.
/// </summary>
public sealed class GripPreset
{
    public string Name { get; set; } = "";

    /// <summary>Where it came from: "this weapon's first-person animation", a pack file, "built-in".</summary>
    public string Source { get; set; } = "";

    public Side Side { get; set; }
    public GripStyle Style { get; set; }

    /// <summary>Palm centre in the grip surface frame, divided by the hand length.</summary>
    public float[] Palm { get; set; } = new float[3];

    /// <summary>Anatomical hand frame (x = fingers, y = out of the palm) in the grip surface frame.</summary>
    public float[] Orientation { get; set; } = { 0, 0, 0, 1 };

    /// <summary>
    /// Each finger's segment directions (x,y,z per joint, proximal first) in the anatomical hand
    /// frame. Applied by fitting the target hand's joint angles to them.
    /// </summary>
    public Dictionary<FingerKind, float[]> Directions { get; set; } = new();

    /// <summary>
    /// Preference added to the fit score when grips compete. A grip captured from this very
    /// weapon's own first-person animation gets <see cref="GripExtractor.OwnPriority"/>: it was
    /// authored for exactly this geometry, so it wins unless it fits clearly worse.
    /// </summary>
    [JsonIgnore] public float Priority { get; set; }

    /// <summary>Weapon type it was captured on (ranking prefers the same kind).</summary>
    public string WeaponType { get; set; } = "";
}
