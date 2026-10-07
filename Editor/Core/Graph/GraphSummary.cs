#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;

namespace WeaponImporter.EditorTools.Core.Graph;

/// <summary>What the generated graph supports, for the review screen and docs.</summary>
public sealed record GraphSummary(IReadOnlyList<string> States, IReadOnlyList<string> Parameters, IReadOnlyList<string> Tags);
