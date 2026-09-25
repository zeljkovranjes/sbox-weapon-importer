#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Grasp;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Geometry-driven grasp: orient the palm onto the measured surface, lay the knuckles along
/// the grip's axis, then close each finger joint until it meets the mesh. The hand's own
/// chirality decides which way fingers wrap, so left and right hands need no special cases.
/// </summary>
public sealed class ProceduralGripGenerator : IGripGenerator
{
    public string Name => "Procedural";
    public bool IsAvailable => true;

    private const float Step = 3f * MathF.PI / 180f;
    private const float ContactTolerance = 0.05f;

    public IEnumerable<GraspCandidate> Generate(GraspRequest request, CancellationToken cancel = default)
    {
        var steps = Math.Max(0, request.SearchSteps);
        var side = 2 * steps + 1;
        var candidates = new GraspCandidate[side * side];
        // Search a small neighbourhood: roll around the surface normal and slide along the axis.
        // Each candidate is independent and only reads the geometry, so they run in parallel
        // (this is always off the editor thread).
        Parallel.For(0, candidates.Length, new ParallelOptions { CancellationToken = cancel }, i =>
        {
            var roll = i / side - steps;
            var slide = i % side - steps;
            var pose = Solve(request, roll * 8f * MathF.PI / 180f, slide * 0.3f);
            var quality = GraspEvaluator.Evaluate(request, pose);
            candidates[i] = new GraspCandidate(pose, quality, $"{Name} roll {roll * 8}° slide {slide * 0.3f:0.0}");
        });
        return candidates.OrderByDescending(c => c.Quality.Score).ToList();
    }

    /// <summary>Places the palm and closes the fingers for one wrist placement.</summary>
    public static HandPose Solve(GraspRequest r, float roll = 0f, float slide = 0f)
    {
        var rig = r.Hand;
        var s = r.Surface;
        var wrist = PlaceWrist(r, roll, slide);
        var pose = HandPose.Open(rig, wrist);

        // Resolve palm penetration first (fingers are open, so only the palm matters).
        PushPalmOut(r, pose);

        foreach (var finger in rig.Fingers)
        {
            if (finger.Kind == FingerKind.Index && r.Trigger is { } trigger && !r.IndexOffTrigger)
            {
                PlaceOnTrigger(r, pose, finger, trigger);
                continue;
            }
            if (finger.Kind == FingerKind.Thumb)
            {
                PoseThumb(r, pose, finger);
                continue;
            }
            // Indexed trigger finger: lay it straight along the frame.
            var maxScale = finger.Kind == FingerKind.Index && r.Trigger is not null && r.IndexOffTrigger ? 0.25f : 1f;
            Close(r, pose, finger, new[] { 1.0f, 1.15f, 0.85f }, maxScale);
        }

        // Spread fingers slightly on thick grips so they don't stack.
        if (s.Radius > 0.9f)
            foreach (var finger in rig.Fingers.Where(f => f.Kind is FingerKind.Ring or FingerKind.Pinky))
                pose.Spread[finger.Kind] = -0.06f;

        ContactRefiner.Refine(r, pose);
        return pose;
    }

    /// <summary>Wrist transform that puts the palm skin onto the contact with knuckles along the grip axis.</summary>
    public static XForm PlaceWrist(GraspRequest r, float roll, float slide)
    {
        var rig = r.Hand;
        var s = r.Surface;
        var palmWorld = -s.Normal;
        var axis = s.Axis;
        // Knuckle line runs index -> pinky, so it points away from where the index should go.
        var knuckles = Vector3.Dot(axis, r.IndexToward) > 0f ? -axis : axis;
        knuckles = Vector3.Normalize(knuckles - palmWorld * Vector3.Dot(knuckles, palmWorld));
        if (MathF.Abs(roll) > 1e-5f)
            knuckles = Vector3.Transform(knuckles, Quaternion.CreateFromAxisAngle(palmWorld, roll));

        // Rotation taking the hand-local (palm normal, knuckle line) onto the world pair.
        var localFrame = MathQ.FromAxes(rig.PalmNormal, rig.KnuckleLine);
        var worldFrame = MathQ.FromAxes(palmWorld, knuckles);
        var rot = MathQ.Normalize(worldFrame * Quaternion.Conjugate(localFrame));

        // Which part of the palm touches depends on the grip.
        var along = r.Style switch
        {
            GripStyle.Wrap or GripStyle.Handle => 0.62f,
            GripStyle.Pump => 0.45f,
            GripStyle.Cradle => 0.3f,
            GripStyle.Overlay => 0.35f,
            _ => 0.5f,
        };
        var palmPoint = Vector3.Lerp(rig.PalmCenter, rig.KnuckleCenter, along) + rig.PalmNormal * (rig.PalmThickness * 0.5f);
        var contact = s.Contact + axis * slide + s.Normal * (0.03f + r.PalmOffset);
        var pos = contact - Vector3.Transform(palmPoint, rot);
        return new XForm(pos, rot);
    }

    private static void PushPalmOut(GraspRequest r, HandPose pose)
    {
        for (var iter = 0; iter < 6; iter++)
        {
            var shape = HandShape.Of(r.Hand, pose);
            var worst = 0f;
            var push = Vector3.Zero;
            foreach (var p in shape.PalmPoints)
            {
                if (r.Weapon.Closest(p, 3f) is not { } c)
                    continue;
                var depth = c.Inside ? c.Distance : -c.Distance;
                if (depth > worst)
                {
                    worst = depth;
                    push = c.Normal;
                }
            }
            if (worst <= 0.01f)
                return;
            // Move straight away from the surface we are inside.
            var dir = Vector3.Dot(push, r.Surface.Normal) > 0.2f ? push : r.Surface.Normal;
            pose.Wrist.Pos += dir * (worst + 0.02f);
        }
    }

    /// <summary>
    /// Closes a finger: all joints together at the given ratios until the first contact, then
    /// the remaining distal joints individually, so the finger wraps the surface.
    /// </summary>
    public static void Close(GraspRequest r, HandPose pose, FingerRig finger, float[] ratios, float maxScale = 1f)
    {
        var flex = pose.Flex[finger.Kind];
        var n = finger.Joints.Length;
        var limits = finger.MaxFlex.Select(m => m * maxScale).ToArray();

        // Phase A: close together.
        var firstContact = -1;
        for (var iter = 0; iter < 80 && firstContact < 0; iter++)
        {
            var moved = false;
            var before = flex.ToArray();
            for (var j = 0; j < n; j++)
            {
                var next = MathF.Min(limits[j], flex[j] + Step * ratios[Math.Min(j, ratios.Length - 1)]);
                if (next > flex[j]) moved = true;
                flex[j] = next;
            }
            if (!moved)
                break;
            var (touching, penetrating, joint) = FingerContact(r, pose, finger, 0);
            if (penetrating)
            {
                Array.Copy(before, flex, n);
                firstContact = Math.Max(0, joint);
            }
            else if (touching)
            {
                firstContact = Math.Max(0, joint);
            }
        }
        if (firstContact < 0)
            return; // reached the limits without touching: floating, reported by the evaluator

        // Phase B: wrap the joints after the one that touched.
        for (var j = firstContact + 1; j < n; j++)
        {
            for (var iter = 0; iter < 60; iter++)
            {
                if (flex[j] >= limits[j])
                    break;
                var prev = flex[j];
                flex[j] = MathF.Min(limits[j], flex[j] + Step);
                var (touching, penetrating, _) = FingerContact(r, pose, finger, j);
                if (penetrating)
                {
                    flex[j] = prev;
                    break;
                }
                if (touching)
                    break;
            }
        }
    }

    /// <summary>Touching / penetrating state of the finger's segments from joint <paramref name="from"/> on.</summary>
    public static (bool Touching, bool Penetrating, int Joint) FingerContact(GraspRequest r, HandPose pose, FingerRig finger, int from)
    {
        var shape = FingerSegments(r.Hand, pose, finger);
        var touching = false;
        var touchJoint = -1;
        foreach (var seg in shape)
        {
            if (seg.Joint < from)
                continue;
            for (var k = 1; k <= 4; k++)
            {
                var p = seg.Point(k / 4f);
                if (r.Weapon.Closest(p, seg.Radius * 3f + 0.2f) is not { } c)
                    continue;
                var gap = c.Inside ? -c.Distance : c.Distance;
                if (gap < seg.Radius - 0.06f)
                    return (true, true, seg.Joint);
                if (gap < seg.Radius + ContactTolerance && !touching)
                {
                    touching = true;
                    touchJoint = seg.Joint;
                }
            }
            if (r.OtherHand is { } other && OverlapsOther(seg, other) > 0.05f)
                return (true, true, seg.Joint);
        }
        return (touching, false, touchJoint);
    }

    /// <summary>Segments of one finger only (cheaper than the whole hand).</summary>
    public static List<HandShape.Segment> FingerSegments(HandRig rig, HandPose pose, FingerRig finger)
    {
        var skeleton = rig.Skeleton;
        var chain = new List<int>();
        for (var b = skeleton[finger.Joints[0]].ParentIndex; b >= 0 && b != rig.Hand; b = skeleton[b].ParentIndex)
            chain.Add(b);
        var world = pose.Wrist;
        for (var i = chain.Count - 1; i >= 0; i--)
            world = XForm.Compose(world, skeleton[chain[i]].RestLocal);
        var result = new List<HandShape.Segment>(finger.Joints.Length);
        for (var j = 0; j < finger.Joints.Length; j++)
        {
            var local = skeleton[finger.Joints[j]].RestLocal;
            local.Rot = pose.LocalRotation(rig, finger, j);
            world = XForm.Compose(world, local);
            result.Add(new HandShape.Segment(finger.Kind, j, world.Pos, world.TransformPoint(finger.Along[j] * finger.Lengths[j]), HandShape.SegmentRadius(finger, j)));
        }
        return result;
    }

    public static float OverlapsOther(HandShape.Segment seg, HandShape other)
    {
        var worst = 0f;
        foreach (var o in other.Segments)
        {
            var d = SegmentDistance(seg.A, seg.B, o.A, o.B);
            worst = MathF.Max(worst, seg.Radius + o.Radius - d);
        }
        return worst;
    }

    /// <summary>Closest distance between two segments.</summary>
    public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var rr = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, rr);
        float s, t;
        if (a <= 1e-9f && e <= 1e-9f)
            return Vector3.Distance(p1, p2);
        if (a <= 1e-9f)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = Vector3.Dot(d1, rr);
            if (e <= 1e-9f)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denom = a * e - b * b;
                s = denom > 1e-9f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
    }

    private static void PoseThumb(GraspRequest r, HandPose pose, FingerRig thumb)
    {
        var (opposition, ratios, scale) = r.Style switch
        {
            // Oppose, then close around the far side.
            GripStyle.Wrap or GripStyle.Handle or GripStyle.Pump => (0.55f, new[] { 0.7f, 1f, 1f }, 1f),
            // Along the side of the handguard.
            GripStyle.Cradle => (0.25f, new[] { 0.5f, 0.8f, 0.8f }, 0.7f),
            // Thumbs-forward pistol grip: thumb points along the frame.
            _ => (0.15f, new[] { 0.3f, 0.5f, 0.5f }, 0.5f),
        };

        // The open thumb must start clear of the weapon: back off opposition, then swing the
        // thumb away from the palm, until it no longer intersects. The strongest clear wrap wins.
        var flex = pose.Flex[thumb.Kind];
        Array.Clear(flex);
        var placed = false;
        var bestDepth = float.MaxValue;
        (float O, float S, float E) best = (opposition, 0f, 0f);
        foreach (var extend in new[] { 0f, -0.25f, -0.5f, -0.75f })
        {
            foreach (var spread in new[] { 0f, 0.2f, 0.4f, -0.2f })
            {
                for (var o = opposition; o >= -0.45f; o -= 0.1f)
                {
                    pose.ThumbOpposition = o;
                    pose.Spread[thumb.Kind] = spread;
                    flex[0] = extend;
                    var depth = ThumbDepth(r, pose, thumb);
                    // Prefer less change from the natural pose when clearing is equal.
                    var cost = depth + (opposition - o) * 0.02f + MathF.Abs(spread) * 0.03f - extend * 0.05f;
                    if (cost < bestDepth)
                    {
                        bestDepth = cost;
                        best = (o, spread, extend);
                    }
                    if (depth <= 0f)
                    {
                        placed = true;
                        break;
                    }
                }
                if (placed)
                    break;
            }
            if (placed)
                break;
        }
        pose.ThumbOpposition = best.O;
        pose.Spread[thumb.Kind] = best.S;
        flex[0] = best.E;
        // Close from the clear start; the base joint keeps its extension as the floor.
        var floor = best.E;
        Close(r, pose, thumb, ratios, scale);
        flex[0] = MathF.Max(flex[0], floor);
    }

    /// <summary>How deep the thumb's segments sit inside the weapon (0 when clear).</summary>
    private static float ThumbDepth(GraspRequest r, HandPose pose, FingerRig thumb)
    {
        var worst = 0f;
        foreach (var seg in FingerSegments(r.Hand, pose, thumb))
            for (var k = 1; k <= 4; k++)
                if (r.Weapon.Closest(seg.Point(k / 4f), seg.Radius + 0.5f) is { } c)
                {
                    var gap = c.Inside ? -c.Distance : c.Distance;
                    worst = MathF.Max(worst, seg.Radius - 0.06f - gap);
                }
        return worst;
    }

    /// <summary>Bends the index finger so the pad of its last segment rests on the trigger.</summary>
    private static void PlaceOnTrigger(GraspRequest r, HandPose pose, FingerRig index, Vector3 trigger)
    {
        var flex = pose.Flex[index.Kind];
        var best = flex.ToArray();
        var bestError = float.MaxValue;
        // Coarse search over the three joints, then local refinement.
        for (var a = 0f; a <= index.MaxFlex[0]; a += 0.12f)
            for (var b = 0f; b <= index.MaxFlex[Math.Min(1, index.MaxFlex.Length - 1)]; b += 0.12f)
            {
                flex[0] = a;
                if (flex.Length > 1) flex[1] = b;
                if (flex.Length > 2) flex[2] = b * 0.7f;
                var err = TriggerError(r, pose, index, trigger);
                if (err < bestError)
                {
                    bestError = err;
                    best = flex.ToArray();
                }
            }
        Array.Copy(best, flex, flex.Length);
        for (var iter = 0; iter < 40; iter++)
        {
            var improved = false;
            for (var j = 0; j < flex.Length; j++)
                foreach (var d in new[] { 0.03f, -0.03f })
                {
                    var old = flex[j];
                    flex[j] = Math.Clamp(old + d, 0f, index.MaxFlex[j]);
                    var err = TriggerError(r, pose, index, trigger);
                    if (err + 1e-4f < bestError)
                    {
                        bestError = err;
                        improved = true;
                    }
                    else
                    {
                        flex[j] = old;
                    }
                }
            if (!improved)
                break;
        }
    }

    private static float TriggerError(GraspRequest r, HandPose pose, FingerRig index, Vector3 trigger)
    {
        var segs = FingerSegments(r.Hand, pose, index);
        // Pad of the distal segment: two thirds along it, on the palm side.
        var last = segs[^1];
        var pad = last.Point(0.6f);
        var err = Vector3.Distance(pad, trigger);
        // Penalise passing through the frame or trigger guard.
        foreach (var seg in segs)
            for (var k = 1; k <= 3; k++)
                if (r.Weapon.Closest(seg.Point(k / 3f), seg.Radius + 0.3f) is { } c)
                {
                    var gap = c.Inside ? -c.Distance : c.Distance;
                    if (gap < seg.Radius * 0.5f)
                        err += (seg.Radius * 0.5f - gap) * 4f;
                }
        return err;
    }
}
