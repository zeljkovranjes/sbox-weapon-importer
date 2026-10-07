#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;

namespace WeaponImporter.EditorTools.Core.Graph;

/// <summary>One weapon clip the graph can play: its role, compiled sequence name, looping and length.</summary>
public sealed record GraphClip(AnimationRole Role, string Sequence, bool Loop, float Seconds)
{
    /// <summary>More sequences for the role, played in turn with <see cref="Sequence"/> (picked by <c>attack_variant</c>).</summary>
    public IReadOnlyList<string> Variants { get; init; } = Array.Empty<string>();
}
