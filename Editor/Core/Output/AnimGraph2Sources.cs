#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Output;

/// <summary>
/// The source files an AnimGraph2 graph plays from, as KV3 text (their compiler rejects JSON): a skeleton
/// (<c>.skel</c>) listing the bones of a model's source art, and a clip (<c>.anim</c>) per animation, compiled
/// against that skeleton.
/// </summary>
public static class AnimGraph2Sources
{
    /// <summary>A <c>.skel</c> with the bones of <paramref name="sourceFile"/> (an FBX, DMX or SMD).</summary>
    public static string Skeleton(string sourceFile)
        => Kv3.Serialize(new KvObject()
            .Add("m_sourceFilename", sourceFile)
            .Add("m_flGlobalScale", 1.0)
            .Add("m_additionalSourceFilenames", new KvArray())
            .Add("m_rootBoneName", ""), Kv3.GenericHeader);

    /// <summary>An <c>.anim</c> playing <paramref name="sourceFile"/>'s first take on <paramref name="skeleton"/> (a <c>.skel</c>).</summary>
    public static string Clip(string sourceFile, string skeleton)
        => Kv3.Serialize(new KvObject()
            .Add("_class", "CClipDocument")
            .Add("m_sourceFilename", sourceFile)
            .Add("m_animationSkeletonName", skeleton)
            .Add("m_nTake", 0)
            .Add("m_flImportScale", 1.0), Kv3.GenericHeader);
}
