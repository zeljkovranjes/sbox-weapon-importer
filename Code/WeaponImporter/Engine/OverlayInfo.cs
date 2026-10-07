using System.Text.Json;

namespace WeaponImporter.Engine;

/// <summary>A replacement character animation for one action.</summary>
public sealed class OverlayInfo
{
    public string Role;
    /// <summary>Model that owns the sequence ("" = the character's own model). Must share the character's skeleton.</summary>
    public string Model;
    public string Sequence;
    /// <summary>Fade in/out time in seconds.</summary>
    public float Blend = 0.2f;
    /// <summary>More sequences of the same model played in turn with <see cref="Sequence"/> (attack combos).</summary>
    public List<string> Variants = new();
}
