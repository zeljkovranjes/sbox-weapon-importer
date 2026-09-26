#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Grasp;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Ik;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Grip;

using Vector3 = System.Numerics.Vector3;

/// <summary>The character side of a grip solve.</summary>
public sealed record CharacterRig
{
    public required HandRig Right { get; init; }
    public HandRig? Left { get; init; }

    /// <summary>Bone the weapon follows in game (hold_R, else hand_R).</summary>
    public required int HoldBone { get; init; }

    /// <summary>Direction the character aims in model space (s&amp;box characters face +X).</summary>
    public Vector3 Forward { get; init; } = Vector3.UnitX;
    public Vector3 Up { get; init; } = Vector3.UnitZ;

    public static CharacterRig? From(Rig.Skeleton skeleton, bool pinkyFollowsRing = false)
    {
        var right = HandRig.Build(skeleton, Side.Right);
        if (right is null)
            return null;
        var left = HandRig.Build(skeleton, Side.Left);
        right.PinkyFollowsRing = pinkyFollowsRing;
        if (left is not null)
            left.PinkyFollowsRing = pinkyFollowsRing;
        var hold = skeleton.IndexOf("hold_R");
        return new CharacterRig { Right = right, Left = left, HoldBone = hold >= 0 ? hold : right.Hand };
    }
}

/// <summary>User choices that shape the solve.</summary>
public sealed record GripOptions
{
    public bool UseSupportHand { get; init; } = true;
    public bool IndexOnTrigger { get; init; } = true;

    /// <summary>0 keeps the weapon pointing exactly where the character aims; 1 keeps the animated wrist.</summary>
    public float WristPreference { get; init; } = 0.35f;

    /// <summary>User nudge of the weapon in the hand (applied after the solve), weapon space.</summary>
    public XForm WeaponOffset { get; init; } = XForm.Identity;

    /// <summary>Hand poses edited by the user (weapon space); they replace the generated ones.</summary>
    public HandPose? RightOverride { get; init; }
    public HandPose? LeftOverride { get; init; }

    public IGripGenerator? Backend { get; init; }

    /// <summary>
    /// Preview pass: no placement search and no fine grasp search, just the direct grasp on the
    /// given surfaces (milliseconds). The editor shows it at once and refines in the background.
    /// </summary>
    public bool Fast { get; init; }
}

/// <summary>Everything a baked grip needs, plus diagnostics.</summary>
public sealed class GripSolution
{
    /// <summary>Weapon (canonical space) relative to the hold bone.</summary>
    public required XForm WeaponInHold { get; init; }

    /// <summary>Firing hand in weapon space, with finger angles.</summary>
    public required HandPose Right { get; init; }
    public HandPose? Left { get; init; }

    public required GraspQuality RightQuality { get; init; }
    public GraspQuality? LeftQuality { get; init; }

    /// <summary>The grasp problems the hands were solved for (to re-score edited poses cheaply).</summary>
    public GraspRequest? RightRequest { get; init; }
    public GraspRequest? LeftRequest { get; init; }

    /// <summary>Rotation the firing wrist needs away from the animation, degrees.</summary>
    public float RightWristCorrection { get; init; }
    public float LeftWristCorrection { get; init; }

    /// <summary>Anatomical wrist bend after the solve (hand vs forearm, vs rest), degrees.</summary>
    public float RightWristBend { get; init; }
    public float LeftWristBend { get; init; }
    public ReachResult RightReach { get; init; }
    public ReachResult LeftReach { get; init; }

    /// <summary>Reference pose with the grip applied (preview / validation).</summary>
    public required CharacterPose Posed { get; init; }

    /// <summary>Which grip each hand came from ("Procedural", a library grip's name, "Edited").</summary>
    public string RightSource { get; init; } = "";
    public string LeftSource { get; init; } = "";

    public List<string> Notes { get; } = new();
}

/// <summary>
/// The grip pipeline: existing animation -> weapon placement -> wrist contact -> arm IK ->
/// wrist orientation -> finger fit -> penetration clean-up. Pure maths; the editor feeds it a
/// sampled character pose and the runtime component replays the baked result.
/// </summary>
public static class GripSolver
{
    /// <summary>Stage timings for diagnostics (tests hook it; null in the editor).</summary>
    public static Action<string>? Trace;

    public static GripSolution Solve(WeaponAnalysis weapon, GripCandidate primary, GripCandidate? support, CharacterRig character, CharacterPose reference, GripOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new GripOptions();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var backend = options.Backend ?? GripGenerators.All.First(b => b.IsAvailable);
        var bvh = weapon.WeaponBvh;
        var world = reference.World;

        // 1+2. Firing hand and weapon placement, searched together: palm positions around the
        //      grip are tried and each is judged by grasp quality AND the wrist bend the arm needs.
        HandPose right;
        GraspRequest rightRequest;
        var rightSource = options.RightOverride is null ? "Procedural" : "Edited";
        var leftSource = options.LeftOverride is null ? "Procedural" : "Edited";
        XForm weaponWorld, weaponInHold;
        if (options.RightOverride is { } fixedRight)
        {
            rightRequest = BuildRequest(weapon, primary, character.Right, bvh, options, firing: true, other: null, preferred: null);
            right = fixedRight.Clone();
            (weaponWorld, weaponInHold) = PlaceWeapon(weapon, character, world, right, options);
        }
        else
        {
            (GraspRequest Request, HandPose Pose, XForm World, XForm InHold)? best = null;
            var bestScore = float.MinValue;
            var steps = (options.Fast ? new[] { new SearchStep(0, primary, primary) } : Search(bvh, primary)).ToList();
            // Palm angles are independent: each stage is evaluated in parallel (background thread),
            // stage 1 only around stage 0's winner.
            (float Score, GraspRequest Request, HandPose Pose, XForm World, XForm InHold) Evaluate(SearchStep variant)
            {
                var request = BuildRequest(weapon, variant.Grip, character.Right, bvh, options, firing: true, other: null, preferred: null);
                var pose = Quick(backend, request, cancel);
                var (w, inHold) = PlaceWeapon(weapon, character, world, pose, options);
                var trial = reference.Clone();
                ArmIk.Solve(trial, character.Right, XForm.Compose(w, pose.Wrist));
                var bend = WristBend(trial, character.Right);
                return (GraspEvaluator.Evaluate(request, pose).Score - BendPenalty(bend), request, pose, w, inHold);
            }
            var stage0 = steps.Where(s => s.Stage == 0).ToList();
            var results0 = new (float Score, GraspRequest Request, HandPose Pose, XForm World, XForm InHold)[stage0.Count];
            Parallel.For(0, stage0.Count, new ParallelOptions { CancellationToken = cancel }, i => results0[i] = Evaluate(stage0[i]));
            var win = Enumerable.Range(0, stage0.Count).MaxBy(i => results0[i].Score);
            bestScore = results0[win].Score;
            best = (results0[win].Request, results0[win].Pose, results0[win].World, results0[win].InHold);
            var bestGrip = stage0[win].Grip;
            var stage1 = steps.Where(s => s.Stage == 1 && ReferenceEquals(s.From, bestGrip)).ToList();
            var results1 = new (float Score, GraspRequest Request, HandPose Pose, XForm World, XForm InHold)[stage1.Count];
            Parallel.For(0, stage1.Count, new ParallelOptions { CancellationToken = cancel }, i => results1[i] = Evaluate(stage1[i]));
            foreach (var r in results1)
                if (r.Score > bestScore)
                {
                    bestScore = r.Score;
                    best = (r.Request, r.Pose, r.World, r.InHold);
                }
            Trace?.Invoke($"right search {clock.ElapsedMilliseconds} ms");
            clock.Restart();
            // Fine search around the winning palm angle (library grips compete here), judged
            // again with the wrist bend each candidate's weapon placement needs.
            rightRequest = best!.Value.Request;
            if (options.Fast)
            {
                right = best.Value.Pose;
                rightSource = "Procedural";
            }
            else
            {
                // Library grips are captured relative to the grip's own surface, so they are
                // also judged there, not only on the palm angle the procedural search picked.
                var baseRequest = BuildRequest(weapon, primary, character.Right, bvh, options, firing: true, other: null, preferred: null);
                var (chosen, chosenRequest) = Choose(backend, new[] { rightRequest, baseRequest }, pose =>
                {
                    var (w, _) = PlaceWeapon(weapon, character, world, pose, options);
                    var trial = reference.Clone();
                    ArmIk.Solve(trial, character.Right, XForm.Compose(w, pose.Wrist));
                    return BendPenalty(WristBend(trial, character.Right));
                }, cancel);
                right = chosen.Pose;
                rightSource = chosen.Source;
                rightRequest = chosenRequest;
            }
            (weaponWorld, weaponInHold) = PlaceWeapon(weapon, character, world, right, options);
        }

        Trace?.Invoke($"right choose {clock.ElapsedMilliseconds} ms");
        clock.Restart();
        // 3. Support hand, avoiding the firing hand, also judged by its wrist.
        HandPose? left = null;
        GraspQuality? leftQuality = null;
        GraspRequest? leftRequest = null;
        if (support is not null && options.UseSupportHand && character.Left is { } leftRig)
        {
            var rightShape = HandShape.Of(character.Right, right);
            var preferred = XForm.ToLocal(weaponWorld, world[leftRig.Hand]);
            if (options.LeftOverride is { } fixedLeft)
            {
                left = fixedLeft.Clone();
                leftRequest = BuildRequest(weapon, support, leftRig, bvh, options, firing: false, other: rightShape, preferred: preferred);
                leftQuality = GraspEvaluator.Evaluate(leftRequest, left);
            }
            else
            {
                var bestScore = float.MinValue;
                GraspRequest? bestLeftRequest = null;
                GripCandidate bestSupport = support;
                foreach (var variant in options.Fast ? new[] { new SearchStep(0, support, support) } : Search(bvh, support))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (variant.Stage == 1 && !ReferenceEquals(variant.From, bestSupport))
                        continue;
                    var request = BuildRequest(weapon, variant.Grip, leftRig, bvh, options, firing: false, other: rightShape, preferred: preferred);
                    var pose = Quick(backend, request, cancel);
                    var quality = GraspEvaluator.Evaluate(request, pose);
                    var trial = reference.Clone();
                    var reach = ArmIk.Solve(trial, leftRig, XForm.Compose(weaponWorld, pose.Wrist));
                    var bend = WristBend(trial, leftRig);
                    var score = quality.Score - BendPenalty(bend) - reach.Shortfall * 2f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        left = pose;
                        leftQuality = quality;
                        bestLeftRequest = request;
                        if (variant.Stage == 0)
                            bestSupport = variant.Grip;
                    }
                }
                if (bestLeftRequest is not null && !options.Fast)
                {
                    var baseLeft = BuildRequest(weapon, support, leftRig, bvh, options, firing: false, other: rightShape, preferred: preferred);
                    var (chosen, chosenRequest) = Choose(backend, new[] { bestLeftRequest, baseLeft }, pose =>
                    {
                        var trial = reference.Clone();
                        var reach = ArmIk.Solve(trial, leftRig, XForm.Compose(weaponWorld, pose.Wrist));
                        return BendPenalty(WristBend(trial, leftRig)) + reach.Shortfall * 2f;
                    }, cancel);
                    left = chosen.Pose;
                    leftSource = chosen.Source;
                    leftQuality = GraspEvaluator.Evaluate(chosenRequest, left);
                    leftRequest = chosenRequest;
                }
                else if (bestLeftRequest is not null)
                {
                    leftRequest = bestLeftRequest;
                }
            }
        }

        Trace?.Invoke($"left {clock.ElapsedMilliseconds} ms");
        // 4. Apply to the reference pose: arm IK + fingers.
        var posed = reference.Clone();
        var (rightReach, leftReach, rightCorr, leftCorr) = Apply(posed, character, weaponInHold, right, left, 1f, 1f);
        var rightBend = WristBend(posed, character.Right);
        var leftBend = left is not null && character.Left is not null ? WristBend(posed, character.Left) : 0f;

        var solution = new GripSolution
        {
            WeaponInHold = weaponInHold,
            Right = right,
            Left = left,
            RightQuality = GraspEvaluator.Evaluate(rightRequest, right),
            LeftQuality = leftQuality,
            RightRequest = rightRequest,
            LeftRequest = leftRequest,
            RightReach = rightReach,
            LeftReach = leftReach,
            RightWristCorrection = rightCorr,
            LeftWristCorrection = leftCorr,
            RightWristBend = rightBend,
            LeftWristBend = leftBend,
            Posed = posed,
            RightSource = rightSource,
            LeftSource = left is null ? "" : leftSource,
        };
        if (!leftReach.Reached && left is not null)
            solution.Notes.Add($"Left hand falls {leftReach.Shortfall:0.0} in short of the support grip.");
        if (rightBend > MaxWristBend)
            solution.Notes.Add($"Right wrist bends {rightBend:0}° against the forearm.");
        return solution;
    }

    /// <summary>
    /// The generator's best few candidates, re-ranked with what their placement costs the arm
    /// (wrist bend, reach). Returns the winner with the name of the grip it came from.
    /// </summary>
    private static (GraspCandidate Candidate, GraspRequest Request) Choose(IGripGenerator backend, IEnumerable<GraspRequest> requests, Func<HandPose, float> penalty, CancellationToken cancel)
    {
        (GraspCandidate, GraspRequest)? best = null;
        var bestScore = float.MinValue;
        var first = true;
        foreach (var request in requests)
        {
            // Later requests (the grip's own surface) only add library grips; the procedural
            // neighbourhood was already searched on the first.
            var list = (first || backend is not ChoiceGripGenerator choice ? backend.Generate(request, cancel) : choice.GenerateLibrary(request, cancel)).Take(16).ToList();
            if (list.Count == 0 && first)
                list.Add(Best(backend, request, cancel));
            first = false;
            foreach (var c in list)
            {
                cancel.ThrowIfCancellationRequested();
                var score = c.Rank - penalty(c.Pose);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (c, request);
                }
            }
        }
        return best!.Value;
    }

    /// <summary>
    /// Fast re-fit after a fine-tune or finger edit: keeps the solved hand placements (optionally
    /// swapping in edited poses), re-places the weapon with the new options and re-runs the arm
    /// IK. No search, so it is cheap enough to run on every slider move.
    /// </summary>
    public static GripSolution Refit(WeaponAnalysis weapon, GripSolution previous, CharacterRig character, CharacterPose reference, GripOptions options, HandPose? right = null, HandPose? left = null)
    {
        var r = right ?? previous.Right;
        var l = left ?? previous.Left;
        var (_, weaponInHold) = PlaceWeapon(weapon, character, reference.World, r, options);
        var posed = reference.Clone();
        var (rightReach, leftReach, rightCorr, leftCorr) = Apply(posed, character, weaponInHold, r, l, 1f, 1f);
        var rightQuality = right is not null && previous.RightRequest is { } rr ? GraspEvaluator.Evaluate(rr, r) : previous.RightQuality;
        var leftQuality = left is not null && previous.LeftRequest is { } lr ? GraspEvaluator.Evaluate(lr, left) : previous.LeftQuality;
        var rightBend = WristBend(posed, character.Right);
        var leftBend = l is not null && character.Left is not null ? WristBend(posed, character.Left) : 0f;
        var solution = new GripSolution
        {
            WeaponInHold = weaponInHold,
            Right = r,
            Left = l,
            RightQuality = rightQuality,
            LeftQuality = leftQuality,
            RightRequest = previous.RightRequest,
            LeftRequest = previous.LeftRequest,
            RightReach = rightReach,
            LeftReach = leftReach,
            RightWristCorrection = rightCorr,
            LeftWristCorrection = leftCorr,
            RightWristBend = rightBend,
            LeftWristBend = leftBend,
            Posed = posed,
            RightSource = right is null ? previous.RightSource : "Edited",
            LeftSource = left is null ? previous.LeftSource : "Edited",
        };
        if (!leftReach.Reached && l is not null)
            solution.Notes.Add($"Left hand falls {leftReach.Shortfall:0.0} in short of the support grip.");
        if (rightBend > MaxWristBend)
            solution.Notes.Add($"Right wrist bends {rightBend:0}° against the forearm.");
        return solution;
    }

    /// <summary>
    /// Replays a solved grip on any animation frame: the weapon follows the hold bone, both
    /// hands reach their weapon-space targets (weighted), fingers take the grip pose.
    /// </summary>
    public static (ReachResult Right, ReachResult Left, float RightCorrection, float LeftCorrection) Apply(CharacterPose pose, CharacterRig character, XForm weaponInHold, HandPose right, HandPose? left, float rightWeight, float leftWeight)
    {
        var weaponWorld = XForm.Compose(pose.World[character.HoldBone], weaponInHold);
        var rightTarget = XForm.Compose(weaponWorld, right.Wrist);
        var rightCorr = MathQ.AngleBetween(pose.World[character.Right.Hand].Rot, rightTarget.Rot) * 180f / MathF.PI;

        // The hold bone usually hangs off the right hand; solving the right arm moves it, so
        // the weapon placement is taken before the solve (weapon stays where the animation put it).
        var rightReach = ArmIk.Solve(pose, character.Right, rightTarget, rightWeight);
        ArmIk.ApplyFingers(pose, character.Right, right, rightWeight);

        var leftReach = new ReachResult(true, 0f, 0f);
        var leftCorr = 0f;
        if (left is not null && character.Left is { } leftRig && leftWeight > 1e-4f)
        {
            var leftTarget = XForm.Compose(weaponWorld, left.Wrist);
            leftCorr = MathQ.AngleBetween(pose.World[leftRig.Hand].Rot, leftTarget.Rot) * 180f / MathF.PI;
            leftReach = ArmIk.Solve(pose, leftRig, leftTarget, leftWeight);
            ArmIk.ApplyFingers(pose, leftRig, left, leftWeight);
        }
        return (rightReach, leftReach, rightCorr, leftCorr);
    }

    /// <summary>
    /// Barrel along the aim (firearms; only limited roll from the animation), grip in the
    /// animated palm.
    /// </summary>
    private static (XForm World, XForm InHold) PlaceWeapon(WeaponAnalysis weapon, CharacterRig character, XForm[] world, HandPose right, GripOptions options)
    {
        var handAnim = world[character.Right.Hand];
        var aim = MathQ.FromAxes(character.Forward, Vector3.Cross(character.Up, character.Forward));
        var implied = MathQ.Normalize(handAnim.Rot * Quaternion.Conjugate(right.Wrist.Rot));
        Quaternion weaponRot;
        if (WeaponTypes.IsFirearm(weapon.Type.Type))
        {
            var relative = MathQ.Normalize(Quaternion.Conjugate(aim) * implied);
            MathQ.SwingTwist(relative, Vector3.UnitX, out _, out var twist);
            var roll = MathQ.Slerp(Quaternion.Identity, twist, Math.Clamp(options.WristPreference, 0f, 1f));
            var angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(roll.W), 0f, 1f));
            if (angle > 20f * MathF.PI / 180f)
                roll = MathQ.Slerp(Quaternion.Identity, roll, 20f * MathF.PI / 180f / angle);
            weaponRot = MathQ.Normalize(aim * roll);
        }
        else
        {
            // Melee: no aim line to keep. The weapon follows the hand exactly, so the animated
            // wrist (and every swing) is left untouched.
            weaponRot = implied;
        }
        var weaponPos = handAnim.Pos - Vector3.Transform(right.Wrist.Pos, weaponRot);
        var weaponWorld = XForm.Compose(new XForm(weaponPos, weaponRot), options.WeaponOffset);
        return (weaponWorld, XForm.ToLocal(world[character.HoldBone], weaponWorld));
    }

    /// <summary>Mild below 60°, steep beyond (approaching the anatomical limit).</summary>
    private static float BendPenalty(float bend)
        => MathF.Max(0f, bend - 35f) * 0.06f + MathF.Max(0f, bend - 60f) * 0.2f;

    private readonly record struct SearchStep(int Stage, GripCandidate Grip, GripCandidate From);

    /// <summary>
    /// Coordinate search over hand placements: stage 0 moves the palm around the grip axis,
    /// stage 1 (applied to stage 0's winner, which the caller tracks) rolls the knuckle line
    /// within the surface, so diagonal fists on stocks and bars are reachable. Manual grips
    /// and overlay grips are kept as they are.
    /// </summary>
    private static IEnumerable<SearchStep> Search(MeshBvh bvh, GripCandidate grip)
    {
        if (grip.Manual)
        {
            // A picked spot keeps its place along the grip, but the palm may still turn around
            // it and roll, so the wrist stays natural (the weapon is placed from the grasp).
            var around = Variants(bvh, grip, new[] { 0f, -20f, 20f, -40f, 40f }).ToList();
            foreach (var v in around)
                yield return new SearchStep(0, v, grip);
            foreach (var origin in around)
                foreach (var roll in new[] { -15f, 15f, -30f, 30f })
                {
                    var s = origin.Surface;
                    var axis = Vector3.Transform(s.Axis, Quaternion.CreateFromAxisAngle(s.Normal, roll * MathF.PI / 180f));
                    yield return new SearchStep(1, origin with { Surface = s with { Axis = Vector3.Normalize(axis) } }, origin);
                }
            yield break;
        }
        // Overlay palms sit over the firing hand, so their spot only moves a little.
        var stage0 = Variants(bvh, grip, grip.Style == GripStyle.Overlay ? new[] { 0f, -15f, 15f, -30f, 30f, -45f, 45f } : new[] { 0f, -25f, 25f, -50f, 50f, -75f, 75f }).ToList();
        // Handguards and pumps: the hand may also slide along the barrel (reach decides).
        if (grip.Style is GripStyle.Cradle or GripStyle.Pump)
            foreach (var back in new[] { 1.5f, 3f, 4.5f, 6f, 8f })
                if (Slide(bvh, grip, -back) is { } slid)
                    stage0.Add(slid);
        foreach (var v in stage0)
            yield return new SearchStep(0, v, grip);
        // The caller skips stage-1 steps whose origin didn't win stage 0.
        foreach (var origin in stage0)
            foreach (var roll in new[] { -15f, 15f, -30f, 30f, -45f, 45f, -60f, 60f })
            {
                var s = origin.Surface;
                var axis = Vector3.Transform(s.Axis, Quaternion.CreateFromAxisAngle(s.Normal, roll * MathF.PI / 180f));
                yield return new SearchStep(1, origin with { Surface = s with { Axis = Vector3.Normalize(axis) } }, origin);
            }
    }

    /// <summary>The grip moved along the weapon's length by <paramref name="dx"/> inches, re-measured on the surface.</summary>
    private static GripCandidate? Slide(MeshBvh bvh, GripCandidate grip, float dx)
    {
        var s = grip.Surface;
        var centre = s.Center + Vector3.UnitX * dx;
        var probed = GripFinder.ProbeFromInside(bvh, centre, s.Normal, s.Axis);
        if (probed is null || Vector3.Dot(probed.Normal, s.Normal) < 0.5f || Vector3.Distance(probed.Contact, s.Contact + Vector3.UnitX * dx) > 2.5f)
            return null;
        var axis = s.Axis - probed.Normal * Vector3.Dot(s.Axis, probed.Normal);
        return grip with { Surface = axis.LengthSquared() > 1e-4f ? probed with { Axis = Vector3.Normalize(axis) } : probed };
    }

    /// <summary>The grip with its palm contact moved around the grip axis by each angle (re-measured on the mesh).</summary>
    private static IEnumerable<GripCandidate> Variants(MeshBvh bvh, GripCandidate grip, float[] degrees)
    {
        var s = grip.Surface;
        foreach (var d in degrees)
        {
            if (MathF.Abs(d) < 1e-3f)
            {
                yield return grip;
                continue;
            }
            var n = Vector3.Transform(s.Normal, Quaternion.CreateFromAxisAngle(s.Axis, d * MathF.PI / 180f));
            var probed = GripFinder.ProbeFromInside(bvh, s.Center, n, s.Axis);
            if (probed is null || Vector3.Dot(probed.Normal, n) <= 0.3f)
                continue;
            // Keep the grip's own axis (measured from its whole cross-section); a patch on the
            // flat side of a grip gives an unreliable local axis.
            var axis = s.Axis - probed.Normal * Vector3.Dot(s.Axis, probed.Normal);
            yield return grip with { Surface = axis.LengthSquared() > 1e-4f ? probed with { Axis = Vector3.Normalize(axis) } : probed };
        }
    }

    /// <summary>
    /// Beyond this the wrist looks broken (combined flexion/deviation, degrees). Human wrists
    /// flex to about 70-80° and extend to about 70°; the search already penalises bends
    /// steeply from 60° so solutions stay well inside the range.
    /// </summary>
    public const float MaxWristBend = 70f;

    /// <summary>
    /// How far the hand is bent relative to the forearm, compared with the rest pose: the
    /// anatomical wrist angle the solve produced (what a viewer judges), not the change from
    /// the animation. Twist about the forearm is excluded (the forearm twist bones carry it).
    /// </summary>
    public static float WristBend(CharacterPose pose, HandRig arm)
    {
        var w = pose.World;
        var rest = arm.Skeleton.RestWorld;
        var rel = MathQ.Normalize(Quaternion.Conjugate(w[arm.LowerArm].Rot) * w[arm.Hand].Rot);
        var restRel = MathQ.Normalize(Quaternion.Conjugate(rest[arm.LowerArm].Rot) * rest[arm.Hand].Rot);
        var delta = MathQ.Normalize(rel * Quaternion.Conjugate(restRel));
        // Forearm axis in the lower-arm frame: toward the hand at rest.
        var forearm = Vector3.Transform(rest[arm.Hand].Pos - rest[arm.LowerArm].Pos, Quaternion.Conjugate(rest[arm.LowerArm].Rot));
        if (forearm.LengthSquared() < 1e-8f)
            return MathQ.AngleBetween(Quaternion.Identity, delta) * 180f / MathF.PI;
        MathQ.SwingTwist(delta, Vector3.Normalize(forearm), out var swing, out _);
        return 2f * MathF.Acos(Math.Clamp(MathF.Abs(swing.W), 0f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>
    /// The support hand fitted on a region other than its grip (the magazine during a reload),
    /// closest to the wrist the animation already has there. Weapon space; null without a
    /// measured surface or a left hand.
    /// </summary>
    public static HandPose? SolveOnRegion(WeaponAnalysis weapon, GripRegion region, CharacterRig character, GripSolution grip, XForm? preferred, GripOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new GripOptions();
        if (region.Surface is not { } surface || character.Left is not { } leftRig)
            return null;
        var backend = options.Backend ?? GripGenerators.All.First(b => b.IsAvailable);
        // Reached from below (a magazine well under a grip, a loading port): palm up under it.
        var style = surface.Normal.Z < -0.6f ? GripStyle.Cradle : GripStyle.Wrap;
        var candidate = new GripCandidate { Surface = surface, Style = style, Confidence = region.Confidence, Reason = region.Reason };
        var rightShape = HandShape.Of(character.Right, grip.Right);
        var request = BuildRequest(weapon, candidate, leftRig, weapon.WeaponBvh, options, firing: false, other: rightShape, preferred: preferred);
        return options.Fast ? ProceduralGripGenerator.Solve(request) : Best(backend, request, cancel).Pose;
    }

    /// <summary>
    /// How well a solved grip fits its pose (higher is better): grasp quality of both hands
    /// minus the wrist bends and the support hand's missing reach. Used to compare the same
    /// weapon on different hold animations.
    /// </summary>
    public static float FitScore(GripSolution s)
        => s.RightQuality.Score - BendPenalty(s.RightWristBend)
           + (s.LeftQuality?.Score ?? 0f) - (s.Left is null ? 0f : BendPenalty(s.LeftWristBend) + s.LeftReach.Shortfall * 2f);

    /// <summary>Hold animations (citizen holdtype) worth trying for a weapon type, its default first.</summary>
    public static int[] CandidateHolds(WeaponType type) => type switch
    {
        WeaponType.Pistol or WeaponType.Revolver => new[] { 1 },
        WeaponType.Smg => new[] { 2, 1 },
        WeaponType.Rifle or WeaponType.Sniper => new[] { 2, 3 },
        WeaponType.Shotgun => new[] { 3, 2 },
        WeaponType.Launcher => new[] { 7, 2 },
        WeaponType.Melee => new[] { 6, 4 },
        _ => new[] { 2, 1, 3, 4 },
    };

    public static string HoldLabel(int hold) => hold switch
    {
        1 => "Pistol",
        2 => "Rifle",
        3 => "Shotgun",
        4 => "Item",
        5 => "Fists",
        6 => "Melee",
        7 => "Launcher",
        _ => $"Hold {hold}",
    };

    /// <summary>
    /// Slides a hand's grasp to another point of the weapon, continuously: the wrist keeps its
    /// place relative to the local surface frame and the fingers close again. Null when there is
    /// no surface there (off the weapon).
    /// </summary>
    /// <summary>Farthest a sliding grasp moves in one update (inches).</summary>
    public const float MaxSlideStep = 0.2f;

    public static (HandPose Pose, GraspRequest Request)? Slide(MeshBvh bvh, GraspRequest request, HandPose pose, Vector3 point, Vector3 normal)
    {
        // Stay on the same side of the part: look for the surface along the current grip normal
        // (the nearest point can hop across a thin handle); fall back to the nearest point.
        var n = Vector3.Normalize(request.Surface.Normal);
        var contact = bvh.Raycast(point + n * 1.5f, -n, 3f) is { } hit ? hit.Point : (Vector3?)null;
        if (contact is null)
        {
            var nearest = SurfaceProbe.FromPoint(bvh, point + normal * 0.05f, request.Surface.Axis);
            if (nearest is null)
                return null;
            contact = nearest.Contact;
        }
        // The hand moves at most MaxSlideStep per update toward the spot (steps in the surface,
        // such as a trigger guard, never make it jump); callers repeat until it arrives.
        var delta = contact.Value - request.Surface.Contact;
        if (delta.Length() > MaxSlideStep)
            contact = request.Surface.Contact + Vector3.Normalize(delta) * MaxSlideStep;
        var probed = new { Contact = contact.Value };
        // Faceted meshes give neighbouring spots very different normals, so the hand is not
        // re-oriented to each probe: it slides with the contact point, keeping its orientation
        // and the grip frame, and the refiner re-seats palm and fingers on the new spot.
        var surface = request.Surface with { Contact = probed.Contact, Center = request.Surface.Center + (probed.Contact - request.Surface.Contact) };
        var moved = pose.Clone();
        moved.Wrist = new XForm(pose.Wrist.Pos + (surface.Contact - request.Surface.Contact), pose.Wrist.Rot);
        var next = request with { Surface = surface };
        var slid = moved.Wrist;
        ContactRefiner.Refine(next, moved, 2);
        // Re-seating the palm is also gradual: it converges over the following updates.
        var seat = moved.Wrist.Pos - slid.Pos;
        if (seat.Length() > MaxSlideStep)
            moved.Wrist.Pos = slid.Pos + Vector3.Normalize(seat) * MaxSlideStep;
        var turn = MathQ.AngleBetween(slid.Rot, moved.Wrist.Rot);
        const float maxTurn = 3f * MathF.PI / 180f;
        if (turn > maxTurn)
            moved.Wrist.Rot = MathQ.Slerp(slid.Rot, moved.Wrist.Rot, maxTurn / turn);
        return (moved, next);
    }

    /// <summary>World transform of the weapon for a pose (canonical weapon space -> character).</summary>
    public static XForm WeaponWorld(CharacterPose pose, CharacterRig character, XForm weaponInHold)
        => XForm.Compose(pose.World[character.HoldBone], weaponInHold);

    public static GraspRequest BuildRequest(WeaponAnalysis weapon, GripCandidate grip, HandRig hand, MeshBvh bvh, GripOptions options, bool firing, HandShape? other, XForm? preferred)
    {
        var s = grip.Surface;
        var indexToward = grip.Style switch
        {
            GripStyle.Cradle or GripStyle.Pump => Vector3.UnitX,
            GripStyle.Handle => s.Axis,
            _ => Vector3.Normalize(Vector3.UnitZ + Vector3.UnitX * 0.3f),
        };

        Vector3? trigger = null;
        if (firing && WeaponTypes.IsFirearm(weapon.Type.Type) && grip.Style == GripStyle.Wrap && weapon.Part(PartKind.Trigger) is { } t)
        {
            // Aim the finger pad at the rear face of the trigger, a finger radius behind it.
            var index = hand.Finger(FingerKind.Index);
            var radius = index?.Radius ?? 0.3f;
            var rear = bvh.Raycast(t.Center - Vector3.UnitX * 3f, Vector3.UnitX, 3.2f);
            var face = rear is { } h && Vector3.Distance(h.Point, t.Center) < 1.5f ? h.Point : t.Center;
            trigger = face - Vector3.UnitX * radius;
        }

        return new GraspRequest
        {
            Hand = hand,
            Weapon = bvh,
            Surface = s,
            Style = grip.Style,
            IndexToward = indexToward,
            Trigger = trigger,
            IndexOffTrigger = !options.IndexOnTrigger,
            OtherHand = other,
            PreferredWrist = preferred,
            PalmOffset = grip.Style == GripStyle.Overlay ? (other is null ? 0.9f : OverlayGap(other, s)) : 0f,
        };
    }

    /// <summary>How far the support palm must sit off the grip to clear the firing hand's fingers.</summary>
    private static float OverlayGap(HandShape other, GripSurface s)
    {
        var gap = 0.6f;
        foreach (var seg in other.Segments)
        {
            var d = Vector3.Dot(seg.B - s.Contact, s.Normal) + seg.Radius;
            gap = MathF.Max(gap, d);
        }
        return MathF.Min(gap, 2.5f);
    }

    /// <summary>One candidate without the roll/slide neighbourhood search (for comparing palm angles quickly).</summary>
    /// <summary>
    /// One candidate without the roll/slide neighbourhood search (for comparing palm angles
    /// quickly). The procedural fit stands in for composites; presets compete in <see cref="Best"/>.
    /// </summary>
    private static HandPose Quick(IGripGenerator backend, GraspRequest request, CancellationToken cancel)
        => backend is ProceduralGripGenerator or CompositeGripGenerator or ChoiceGripGenerator ? ProceduralGripGenerator.Solve(request) : Best(backend, request, cancel).Pose;

    private static GraspCandidate Best(IGripGenerator backend, GraspRequest request, CancellationToken cancel)
    {
        var best = backend.Generate(request, cancel).FirstOrDefault();
        if (best is null && backend is not ProceduralGripGenerator)
            best = new ProceduralGripGenerator().Generate(request, cancel).FirstOrDefault();
        if (best is not null)
            return best;
        var pose = ProceduralGripGenerator.Solve(request);
        return new GraspCandidate(pose, GraspEvaluator.Evaluate(request, pose), "Procedural");
    }
}
