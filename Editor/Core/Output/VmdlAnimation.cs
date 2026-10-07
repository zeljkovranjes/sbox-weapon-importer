#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Output;

using Vector3 = System.Numerics.Vector3;

/// <summary>An animation entry of a generated model.</summary>
public sealed record VmdlAnimation
{
    public required string Name { get; init; }
    /// <summary>Project-relative source file (DMX/FBX).</summary>
    public required string File { get; init; }
    public int Take { get; init; }
    public bool Looping { get; init; }
    public int FrameCount { get; init; }
    public float Fps { get; init; } = 30f;
    public IReadOnlyList<(string Name, float Time)> Events { get; init; } = Array.Empty<(string, float)>();
}
