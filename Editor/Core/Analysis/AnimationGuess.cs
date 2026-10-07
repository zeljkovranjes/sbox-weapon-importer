#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Analysis;

/// <summary>A guess at an animation's role, with how sure the classifier is.</summary>
public sealed record AnimationGuess(string Animation, AnimationRole Role, float Confidence, string Reason)
{
    /// <summary>First-person (view model) or third-person (world / body) clip, when the name says so.</summary>
    public AnimationPerspective Perspective { get; init; }
}
