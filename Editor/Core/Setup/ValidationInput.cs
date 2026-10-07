#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Grip;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

/// <summary>Everything validation can look at; engine-side facts are optional.</summary>
public sealed record ValidationInput
{
    public required WeaponSetup Setup { get; init; }
    public WeaponAnalysis? Analysis { get; init; }
    public GripSolution? Grip { get; init; }

    /// <summary>Compile errors captured by the editor (file -> messages).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? CompileErrors { get; init; }

    /// <summary>Asset references the editor could not resolve.</summary>
    public IReadOnlyList<string> MissingReferences { get; init; } = Array.Empty<string>();

    /// <summary>Clip names present in the compiled model (null when not compiled yet).</summary>
    public IReadOnlyCollection<string>? CompiledSequences { get; init; }

    /// <summary>Problems found by replaying the grip over the character's actions (role, normalized time, message).</summary>
    public IReadOnlyList<(AnimationRole Role, float Time, string Message)> ActionIssues { get; init; } = Array.Empty<(AnimationRole, float, string)>();

    /// <summary>Called by auto fixes that change the setup, so the editor can re-solve.</summary>
    public Action? Changed { get; init; }
}
