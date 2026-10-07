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

public sealed class IkSettings
{
    /// <summary>0 = aim exactly along the character's forward, 1 = keep the animated wrist.</summary>
    public float WristPreference { get; set; } = 0.35f;
    /// <summary>Weapon nudge in the hand (canonical space).</summary>
    public float[] WeaponOffsetPosition { get; set; } = new float[3];
    public float[] WeaponOffsetRotation { get; set; } = { 0, 0, 0, 1 };
    /// <summary>Temporal smoothing of hand corrections at runtime (0 = none).</summary>
    public float Smoothing { get; set; } = 0.35f;
    /// <summary>How far past arm length the support hand may be pulled before it lets go.</summary>
    public float ReachSlack { get; set; } = 1.5f;

    [JsonIgnore] public XForm WeaponOffset => new(V.Of(WeaponOffsetPosition), V.Q(WeaponOffsetRotation));
}
