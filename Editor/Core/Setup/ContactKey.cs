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

/// <summary>A change of hand contact at a moment of an animation (normalized time 0..1).</summary>
public sealed class ContactKey
{
    public Side Hand { get; set; } = Side.Left;
    public float Time { get; set; }
    public ContactState State { get; set; } = ContactState.Released;
    /// <summary>Blend time into the new state, seconds.</summary>
    public float Blend { get; set; } = 0.15f;
    /// <summary>Optional adjustment of the hand target while locked (weapon space).</summary>
    public float[]? Offset { get; set; }
    /// <summary>Optional weapon bone the hand follows while locked (magazine during a reload).</summary>
    public string Follow { get; set; } = "";

    /// <summary>What a lock holds, for display: "grip", "magazine" or "" (the grip).</summary>
    public string Target { get; set; } = "";
}
