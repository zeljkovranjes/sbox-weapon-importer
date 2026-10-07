#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Grip;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

/// <summary>One validation line: "✓ Skeleton" or "! Left wrist penetration detected".</summary>
public sealed record CheckResult
{
    public required string Name { get; init; }
    public required CheckSeverity Severity { get; init; }
    public string Message { get; init; } = "";

    /// <summary>A safe automatic fix, when one exists.</summary>
    public Action? AutoFix { get; init; }
    public string AutoFixLabel { get; init; } = "Auto Fix";

    public bool NeedsAttention => Severity >= CheckSeverity.Warning;
}
