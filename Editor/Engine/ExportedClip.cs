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

public sealed record ExportedClip( string Clip, string Sequence, string File, int FrameCount, float Fps );
