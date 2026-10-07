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

/// <summary>What a bake produced.</summary>
public sealed class BakeResult
{
    public bool Success { get; set; }
    public string ModelPath { get; set; } = "";
    public string PrefabPath { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public string CorrectedModelPath { get; set; } = "";
    /// <summary>The first-person viewmodel (empty when the file has no first-person arms).</summary>
    public string FirstPersonModelPath { get; set; } = "";
    /// <summary>The weapon's AnimGraph (empty when there is no idle animation to build it on).</summary>
    public string GraphPath { get; set; } = "";
    public string SetupPath { get; set; } = "";
    public List<string> Files { get; } = new();
    public Dictionary<string, IReadOnlyList<string>> Errors { get; } = new();
    public List<string> Sequences { get; } = new();
    public List<string> Notes { get; } = new();
}
