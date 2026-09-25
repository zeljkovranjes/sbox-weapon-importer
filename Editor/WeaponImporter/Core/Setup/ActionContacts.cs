#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Ik;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>A character action sampled over its normalized time (see CharacterPoser.SampleTrack).</summary>
public sealed record ActionTrack(AnimationRole Role, float Seconds, IReadOnlyList<(float Time, CharacterPose Pose)> Samples);

/// <summary>Result of planning the support hand over the actions.</summary>
public sealed class ActionContactPlan
{
    /// <summary>Support hand on the magazine (weapon space), when the weapon has one.</summary>
    public HandPose? Magazine { get; set; }
    public GripRegion? MagazineRegion { get; set; }
    public Dictionary<AnimationRole, ContactPlanner.Plan> Plans { get; } = new();
}

/// <summary>
/// Plans the support hand over the character's actions once the grip is solved: where the
/// animated hand goes relative to the weapon (it follows the hold bone), where it should touch
/// this weapon instead (the magazine during reloads), and the contact keys that get it there with
/// the smallest change to the animation. Pure maths; the samples come from the editor.
/// </summary>
public static class ActionContacts
{
    public static ActionContactPlan Plan(WeaponAnalysis weapon, WeaponSetup setup, CharacterRig character, GripSolution grip, IReadOnlyList<ActionTrack> tracks, GripOptions? options = null, CancellationToken cancel = default)
    {
        var plan = new ActionContactPlan();
        if (grip.Left is null || character.Left is not { } leftRig || !setup.UseSupportHand)
            return plan;
        var region = GripRegions.Magazine(weapon);
        plan.MagazineRegion = region;

        // Animated support wrist in weapon space, per sample.
        List<ContactPlanner.Sample> Samples(ActionTrack track) => track.Samples
            .Select(s => new ContactPlanner.Sample(s.Time, XForm.ToLocal(GripSolver.WeaponWorld(s.Pose, character, grip.WeaponInHold), s.Pose.World[leftRig.Hand])))
            .ToList();

        var reloads = tracks.Where(t => ActionTiming.ReloadRoles.Contains(t.Role)).ToList();
        if (!setup.ReloadTouchesMagazine)
        {
            // Default: the character reloads as animated; the hand only lets go and comes back.
            foreach (var track in reloads)
                plan.Plans[track.Role] = ContactPlanner.PlanRelease(Samples(track), track.Seconds);
            return plan;
        }
        if (region?.Surface is not null && reloads.Count > 0)
        {
            // Fit the magazine grab closest to where the animation brings the hand.
            var first = Samples(reloads[0]);
            var nearest = first.OrderBy(s => Vector3.Distance(s.Wrist.Pos, region.Surface.Contact)).First();
            plan.Magazine = GripSolver.SolveOnRegion(weapon, region, character, grip, nearest.Wrist, options, cancel);
        }

        foreach (var track in reloads)
        {
            cancel.ThrowIfCancellationRequested();
            if (plan.Magazine is null)
                break;
            plan.Plans[track.Role] = ContactPlanner.PlanReload(Samples(track), grip.Left.Wrist, plan.Magazine.Wrist, track.Seconds, weapon.WeaponBounds);
        }
        return plan;
    }

    /// <summary>Writes the planned tracks into the setup, leaving tracks the user edited alone.</summary>
    public static void Apply(WeaponSetup setup, ActionContactPlan plan)
    {
        foreach (var (role, p) in plan.Plans)
        {
            if (setup.Contacts.TryGetValue(role, out var existing) && existing.Keys.Count > 0 && !existing.Generated)
                continue;
            setup.Contacts[role] = new ContactTrack { Keys = p.Keys, Generated = true, Note = p.Note };
        }
    }
}
