#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;

namespace WeaponImporter.EditorTools.Core.Formats.Fbx;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Editor/Core/Assembly.cs)

/// <summary>Options for <see cref="FbxImporter.Import"/>.</summary>
public sealed class FbxImportOptions
{
    /// <summary>Fixed resampling rate for all clips, frames per second.</summary>
    public float SampleFps { get; init; } = 30f;

    /// <summary>
    /// When the static rest pose is degenerate (Mixamo-style zeroed bind translations) and no
    /// usable BindPose node exists, sample frame 0 of the first clip as the rest pose.
    /// </summary>
    public bool RestFromFrame0WhenBindDegenerate { get; init; } = true;
}
