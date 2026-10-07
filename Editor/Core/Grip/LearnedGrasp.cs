#nullable enable annotations

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;
using WeaponImporter.EditorTools.Core.Grasp;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>One hand from a learned grasp generator: 21 keypoints in canonical weapon space (inches).</summary>
public sealed record LearnedGrasp(Side Side, Vector3[] Joints);
