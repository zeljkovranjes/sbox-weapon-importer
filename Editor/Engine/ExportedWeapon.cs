using System.Text.Json;
using System.Text.Json.Nodes;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Generation;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Output;
using WeaponImporter.EditorTools.Core.Setup;
using WeaponImporter.EditorTools.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.EditorTools.Engine;

/// <summary>Files and references produced by the geometry/material export step.</summary>
public sealed class ExportedWeapon
{
    public ExportRig Rig { get; set; }
    public string MeshFile { get; set; } = "";
    /// <summary>Files the export already wrote to disk (materials, textures).</summary>
    public List<string> WrittenFiles { get; } = new();
    public List<(string Path, string Text)> TextFiles { get; } = new();
    public List<(string Path, byte[] Bytes)> BinaryFiles { get; } = new();
    public List<(string From, string To)> MaterialRemaps { get; } = new();
    public List<ExportedClip> Clips { get; } = new();
    public List<string> Notes { get; } = new();

    /// <summary>First-person viewmodel (null when not exported).</summary>
    public FirstPersonRig FirstPerson { get; set; }
    public string FirstPersonMeshFile { get; set; } = "";
    public List<(string From, string To)> FirstPersonMaterialRemaps { get; } = new();
    public List<ExportedClip> FirstPersonClips { get; } = new();
}
