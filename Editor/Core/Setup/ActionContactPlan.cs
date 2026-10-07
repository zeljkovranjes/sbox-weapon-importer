#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Grip;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Ik;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>Result of planning the support hand over the actions.</summary>
public sealed class ActionContactPlan
{
    /// <summary>Support hand on the magazine (weapon space), when the weapon has one.</summary>
    public HandPose? Magazine { get; set; }
    public GripRegion? MagazineRegion { get; set; }
    public Dictionary<AnimationRole, ContactPlanner.Plan> Plans { get; } = new();
}
