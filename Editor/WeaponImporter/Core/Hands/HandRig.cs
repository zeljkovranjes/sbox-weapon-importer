#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Core.Hands;

using Vector3 = System.Numerics.Vector3;

public enum Side { Right, Left }

public enum FingerKind { Thumb, Index, Middle, Ring, Pinky }

/// <summary>One finger: its joint bones from the knuckle outward, with flex axes.</summary>
public sealed class FingerRig
{
    public required FingerKind Kind { get; init; }

    /// <summary>Joint bones, proximal first (metacarpals are not included).</summary>
    public required int[] Joints { get; init; }

    /// <summary>Segment lengths, one per joint (the last one is estimated to the fingertip).</summary>
    public required float[] Lengths { get; init; }

    /// <summary>Flexion axis of each joint in that joint's own rest-local frame.</summary>
    public required Vector3[] FlexAxes { get; init; }

    /// <summary>Spread (abduction) axis of the base joint, local frame.</summary>
    public required Vector3 SpreadAxis { get; init; }

    /// <summary>Flexion limits per joint (radians).</summary>
    public required float[] MaxFlex { get; init; }

    /// <summary>Finger radius used for contact tests.</summary>
    public required float Radius { get; init; }

    /// <summary>Local direction a joint points along (toward its child), rest frame.</summary>
    public required Vector3[] Along { get; init; }
}

/// <summary>
/// A character's hand and arm, read from bone names and the rest pose. Everything the grasp
/// solver needs is derived here once: palm frame, finger chains, flex axes, sizes.
/// </summary>
public sealed class HandRig
{
    public required Side Side { get; init; }
    public required Skeleton Skeleton { get; init; }
    public required int Hand { get; init; }
    public required int LowerArm { get; init; }
    public required int UpperArm { get; init; }
    public int Clavicle { get; init; } = -1;

    /// <summary>Twist/helper bones between shoulder and hand that just follow their parents.</summary>
    public IReadOnlyList<int> Helpers { get; init; } = Array.Empty<int>();

    public required IReadOnlyList<FingerRig> Fingers { get; init; }

    /// <summary>Metacarpal bones (kept at rest).</summary>
    public IReadOnlyList<int> Metacarpals { get; init; } = Array.Empty<int>();

    // ---- hand-local frame (relative to the hand bone's rest transform) ----
    public required Vector3 PalmCenter { get; init; }
    public required Vector3 PalmNormal { get; init; }   // out of the palm
    public required Vector3 FingerDirection { get; init; } // wrist -> knuckles
    public required Vector3 KnuckleLine { get; init; }  // index -> pinky side
    public required Vector3 KnuckleCenter { get; init; }
    public required float HandLength { get; init; }
    public required float PalmWidth { get; init; }
    public float PalmThickness => HandLength * 0.14f;

    public FingerRig? Finger(FingerKind kind) => Fingers.FirstOrDefault(f => f.Kind == kind);

    /// <summary>All bones this rig may rotate (arm chain and fingers).</summary>
    public IEnumerable<int> Bones
    {
        get
        {
            if (Clavicle >= 0) yield return Clavicle;
            yield return UpperArm;
            yield return LowerArm;
            yield return Hand;
            foreach (var h in Helpers) yield return h;
            foreach (var f in Fingers)
                foreach (var j in f.Joints)
                    yield return j;
        }
    }

    // ------------------------------------------------------------------ building

    private static string SideSuffix(Side side) => side == Side.Right ? "R" : "L";

    /// <summary>
    /// Finds a hand by the s&amp;box naming scheme (<c>hand_R</c>, <c>arm_lower_R</c>,
    /// <c>finger_index_0_R</c>...) with fallbacks for common rigs (Mixamo, UE, Rigify, Biped).
    /// Returns null when the skeleton has no recognisable arm.
    /// </summary>
    public static HandRig? Build(Skeleton skeleton, Side side)
    {
        var s = SideSuffix(side);
        int Find(params string[] names)
        {
            foreach (var n in names)
            {
                var i = skeleton.IndexOf(n);
                if (i >= 0)
                    return i;
            }
            // Case-insensitive fallback.
            foreach (var n in names)
                for (var b = 0; b < skeleton.Count; b++)
                    if (string.Equals(skeleton[b].Name, n, StringComparison.OrdinalIgnoreCase))
                        return b;
            return -1;
        }
        var lower = s.ToLowerInvariant();
        var full = side == Side.Right ? "Right" : "Left";
        var hand = Find($"hand_{s}", $"Hand_{s}", $"hand.{s}", $"hand_{lower}", $"{full}Hand", $"mixamorig:{full}Hand", $"hand_{lower}", $"DEF-hand.{s}", $"Bip01 {s} Hand");
        if (hand < 0)
            hand = FindHandByTokens(skeleton, side);
        if (hand < 0)
            return null;
        var lowerArm = skeleton[hand].ParentIndex;
        // Skip twist helpers between hand and forearm.
        while (lowerArm >= 0 && IsHelper(skeleton[lowerArm].Name))
            lowerArm = skeleton[lowerArm].ParentIndex;
        if (lowerArm < 0)
            return null;
        var upperArm = skeleton[lowerArm].ParentIndex;
        while (upperArm >= 0 && IsHelper(skeleton[upperArm].Name))
            upperArm = skeleton[upperArm].ParentIndex;
        if (upperArm < 0)
            return null;
        var clavicle = skeleton[upperArm].ParentIndex;

        var helpers = new List<int>();
        for (var b = 0; b < skeleton.Count; b++)
        {
            var p = skeleton[b].ParentIndex;
            if ((p == upperArm || p == lowerArm) && IsHelper(skeleton[b].Name))
                helpers.Add(b);
        }

        var fingers = new List<FingerRig>();
        var metas = new List<int>();
        var restWorld = skeleton.RestWorld;
        var handRest = restWorld[hand];

        List<int> Chain(string finger)
        {
            var chain = new List<int>();
            for (var k = 0; k < 4; k++)
            {
                // Deform bones before same-named controls (Rigify has both).
                var b = Find($"finger_{finger}_{k}_{s}", $"{finger}_{k}_{s}", $"{full}Hand{Cap(finger)}{k + 1}", $"mixamorig:{full}Hand{Cap(finger)}{k + 1}", $"{finger}_{(k + 1):00}_{lower}",
                    $"DEF-f_{finger}.{(k + 1):00}.{s}", $"DEF-{finger}.{(k + 1):00}.{s}", $"{finger}.{(k + 1):00}.{s}", $"f_{finger}.{(k + 1):00}.{s}");
                if (b < 0)
                    break;
                chain.Add(b);
            }
            return chain;
        }

        var jointsByKind = new Dictionary<FingerKind, List<int>>();
        foreach (var kind in Enum.GetValues<FingerKind>())
        {
            var name = kind.ToString().ToLowerInvariant();
            var chain = Chain(name);
            if (chain.Count == 0 && kind == FingerKind.Pinky)
                chain = Chain("little");
            // Keep only the bones that descend from the hand.
            chain = chain.Where(b => Descends(skeleton, b, hand)).Take(3).ToList();
            if (chain.Count > 0)
                jointsByKind[kind] = chain;
            var meta = Find($"finger_{name}_meta_{s}");
            if (meta >= 0)
                metas.Add(meta);
        }
        if (!jointsByKind.ContainsKey(FingerKind.Index) || !jointsByKind.ContainsKey(FingerKind.Middle))
            jointsByKind = FingersByShape(skeleton, hand) ?? jointsByKind;
        if (!jointsByKind.ContainsKey(FingerKind.Index) || !jointsByKind.ContainsKey(FingerKind.Middle))
            return null;

        Vector3 Local(int bone) => handRest.Inverse().TransformPoint(restWorld[bone].Pos);

        var indexBase = Local(jointsByKind[FingerKind.Index][0]);
        var middleBase = Local(jointsByKind[FingerKind.Middle][0]);
        var outer = jointsByKind.TryGetValue(FingerKind.Pinky, out var pinky) ? pinky : jointsByKind.GetValueOrDefault(FingerKind.Ring) ?? jointsByKind[FingerKind.Middle];
        var outerBase = Local(outer[0]);
        var knuckles = new[] { FingerKind.Index, FingerKind.Middle, FingerKind.Ring, FingerKind.Pinky }
            .Where(jointsByKind.ContainsKey).Select(k => Local(jointsByKind[k][0])).ToList();
        var knuckleCenter = knuckles.Aggregate(Vector3.Zero, (a, v) => a + v) / knuckles.Count;
        var wrist = Vector3.Zero;
        var fingerDir = SafeNormal(knuckleCenter - wrist, Vector3.UnitX);
        var knuckleLine = outerBase - indexBase;
        knuckleLine = SafeNormal(knuckleLine - fingerDir * Vector3.Dot(knuckleLine, fingerDir), MathQ.Perpendicular(fingerDir));

        // Palm normal: perpendicular to fingers and knuckles; the sign comes from anatomy.
        var normal = Vector3.Normalize(Vector3.Cross(fingerDir, knuckleLine));
        var vote = 0f;
        // Thumb sits on the palm side of the knuckle plane.
        if (jointsByKind.TryGetValue(FingerKind.Thumb, out var thumb))
        {
            var thumbTip = Local(thumb[^1]);
            vote += Vector3.Dot(thumbTip - wrist, normal) * 2f;
        }
        // Rest curl bends fingertips toward the palm.
        foreach (var kind in new[] { FingerKind.Index, FingerKind.Middle, FingerKind.Ring })
            if (jointsByKind.TryGetValue(kind, out var chain) && chain.Count >= 2)
                vote += Vector3.Dot(Local(chain[^1]) - Local(chain[0]), normal) - Vector3.Dot(Local(chain[1]) - Local(chain[0]), normal) * 0.5f;
        // Handedness fallback: for a right hand, fingers x knuckles(index->pinky) points out of
        // the back of the hand in a right-handed frame; mirrored for the left.
        if (MathF.Abs(vote) < 1e-3f)
            vote = side == Side.Right ? -1f : 1f;
        if (vote < 0f)
            normal = -normal;

        var middleChain = jointsByKind[FingerKind.Middle];
        var middleTip = Local(middleChain[^1]);
        var lastSeg = middleChain.Count >= 2 ? Vector3.Distance(Local(middleChain[^1]), Local(middleChain[^2])) : 1f;
        var handLength = Vector3.Distance(wrist, middleTip) + lastSeg * 0.8f;
        var palmWidth = MathF.Max(1e-3f, Vector3.Distance(indexBase, outerBase)) * 1.25f;
        var palmCenter = Vector3.Lerp(wrist, knuckleCenter, 0.55f) + normal * 0.0f;

        foreach (var (kind, chain) in jointsByKind)
        {
            var lengths = new float[chain.Count];
            var axes = new Vector3[chain.Count];
            var along = new Vector3[chain.Count];
            for (var j = 0; j < chain.Count; j++)
            {
                var head = Local(chain[j]);
                var tailWorld = j + 1 < chain.Count ? Local(chain[j + 1]) : head + (head - Local(j > 0 ? chain[j - 1] : hand)) * 0.8f;
                var dir = SafeNormal(tailWorld - head, fingerDir);
                lengths[j] = MathF.Max(0.05f, Vector3.Distance(head, tailWorld));
                // Flexion bends the segment toward the palm: axis = dir x normal (right-hand rule
                // turns dir toward -normal... pick the sign that moves the tip toward +normal).
                var axisHand = SafeNormal(Vector3.Cross(dir, normal), knuckleLine);
                if (kind == FingerKind.Thumb)
                {
                    // Thumb flexes across the palm toward the little finger.
                    axisHand = SafeNormal(Vector3.Cross(dir, SafeNormal(knuckleCenter + knuckleLine * palmWidth * 0.5f - head, normal)), axisHand);
                }
                // Check sign: rotating dir about the axis must move it toward the palm (or across it).
                var probeTarget = kind == FingerKind.Thumb ? SafeNormal(knuckleCenter + knuckleLine * palmWidth * 0.5f - head, normal) : normal;
                var rotated = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(axisHand, 0.3f));
                if (Vector3.Dot(rotated - dir, probeTarget) < 0f)
                    axisHand = -axisHand;
                // Into the joint's own rest frame.
                var jointRestInHand = XForm.Compose(handRest.Inverse(), restWorld[chain[j]]);
                axes[j] = Vector3.Normalize(Vector3.Transform(axisHand, Quaternion.Conjugate(jointRestInHand.Rot)));
                along[j] = Vector3.Normalize(Vector3.Transform(dir, Quaternion.Conjugate(jointRestInHand.Rot)));
            }
            var baseRest = XForm.Compose(handRest.Inverse(), restWorld[chain[0]]);
            var spreadHand = normal;
            var spreadLocal = Vector3.Normalize(Vector3.Transform(spreadHand, Quaternion.Conjugate(baseRest.Rot)));
            var maxFlex = kind == FingerKind.Thumb
                ? new[] { 0.9f, 1.0f, 1.3f }
                : new[] { 1.55f, 1.9f, 1.4f };
            fingers.Add(new FingerRig
            {
                Kind = kind,
                Joints = chain.ToArray(),
                Lengths = lengths,
                FlexAxes = axes,
                SpreadAxis = spreadLocal,
                MaxFlex = maxFlex.Take(chain.Count).ToArray(),
                Radius = handLength * (kind == FingerKind.Thumb ? 0.058f : 0.05f),
                Along = along,
            });
        }

        return new HandRig
        {
            Side = side,
            Skeleton = skeleton,
            Hand = hand,
            LowerArm = lowerArm,
            UpperArm = upperArm,
            Clavicle = clavicle,
            Helpers = helpers,
            Fingers = fingers.OrderBy(f => f.Kind).ToList(),
            Metacarpals = metas,
            PalmCenter = palmCenter,
            PalmNormal = normal,
            FingerDirection = fingerDir,
            KnuckleLine = knuckleLine,
            KnuckleCenter = knuckleCenter,
            HandLength = handLength,
            PalmWidth = palmWidth,
        };
    }

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------------ rigs without known names

    /// <summary>Control/mechanism bones that are never the deforming hand or finger.</summary>
    private static readonly HashSet<string> ControlTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ik", "ctrl", "cntrl", "control", "mch", "org", "drv", "master", "tweak", "vis", "pole", "target", "end", "helper", "twist", "hold", "attach", "widget", "parent", "socket", "nub",
    };

    /// <summary>Lowercase name tokens: "IK_Hand_Cntrl_L_015" → ik, hand, cntrl, l, 015.</summary>
    public static List<string> Tokens(string name)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
                tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }
            // Camel case and letter/digit boundaries split words.
            if (current.Length > 0 && ((char.IsUpper(c) && char.IsLower(current[^1])) || char.IsDigit(c) != char.IsDigit(current[^1])))
                Flush();
            current.Append(c);
        }
        Flush();
        return tokens;
    }

    private static bool IsSide(List<string> tokens, Side side)
        => side == Side.Right ? tokens.Any(t => t is "r" or "right" or "rt") : tokens.Any(t => t is "l" or "left" or "lt");

    /// <summary>
    /// The deforming hand of a side on rigs with other conventions: a bone named hand/wrist with a
    /// side marker, not a control; deform ("DEF") bones and bones with many descendants win.
    /// </summary>
    private static int FindHandByTokens(Skeleton skeleton, Side side)
    {
        var best = -1;
        var bestScore = 0;
        for (var b = 0; b < skeleton.Count; b++)
        {
            var tokens = Tokens(skeleton[b].Name);
            if (!tokens.Any(t => t is "hand" or "wrist") || !IsSide(tokens, side) || tokens.Any(ControlTokens.Contains))
                continue;
            var descendants = 0;
            for (var c = 0; c < skeleton.Count; c++)
                if (Descends(skeleton, c, b))
                    descendants++;
            if (descendants < 6)
                continue;
            var score = descendants + (tokens.Contains("def") ? 1000 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = b;
            }
        }
        return best;
    }

    /// <summary>
    /// Finger chains of a hand told apart by shape when their names say nothing ("Bone_R.004"):
    /// each chain is followed from the hand (through palm bones) up to three joints; the thumb is
    /// the chain whose direction differs most from the others, and the fingers are ordered by
    /// their distance from the thumb (index nearest, pinky farthest).
    /// </summary>
    private static Dictionary<FingerKind, List<int>>? FingersByShape(Skeleton skeleton, int hand)
    {
        bool Usable(int b)
        {
            var tokens = Tokens(skeleton[b].Name);
            return !tokens.Any(t => t is "end" or "nub" or "tip" or "mch" or "org" or "ik" or "ctrl" or "cntrl" or "drv" or "master" or "tweak" or "vis" or "helper" or "twist");
        }
        List<int> Children(int b) => Enumerable.Range(0, skeleton.Count).Where(c => skeleton[c].ParentIndex == b && Usable(c)).ToList();

        var roots = new List<int>();
        void Collect(int b, int depth)
        {
            foreach (var c in Children(b))
            {
                var kids = Children(c);
                // A palm/metacarpal bone that fans out: its children are the fingers.
                if (kids.Count >= 2 && depth == 0)
                    Collect(c, depth + 1);
                else if (kids.Count >= 1)
                    roots.Add(c);
            }
        }
        Collect(hand, 0);
        if (roots.Count < 3)
            return null;

        var raw = roots.Select(r =>
        {
            var chain = new List<int> { r };
            while (chain.Count < 5 && Children(chain[^1]) is { Count: > 0 } kids)
                chain.Add(kids[0]);
            return chain;
        }).Where(c => c.Count >= 2).ToList();
        if (raw.Count < 3)
            return null;
        // Four joints: the first is the metacarpal inside the palm, not a finger joint.
        var chains = raw.Select(c => (c.Count >= 4 ? c.Skip(1) : c).Take(3).ToList()).ToList();

        var rest = skeleton.RestWorld;
        Vector3 Dir(List<int> c) => SafeNormal(rest[c[^1]].Pos - rest[c[0]].Pos, Vector3.UnitX);
        // The thumb has one bone fewer than the fingers on most rigs; otherwise its base sits
        // nearest the wrist and it points away from the others.
        var counts = raw.Select(c => c.Count).ToList();
        var shortest = counts.Min();
        List<int> thumb;
        if (counts.Count(c => c == shortest) == 1 && counts.Count(c => c > shortest) >= 3)
            thumb = chains[counts.IndexOf(shortest)];
        else
            thumb = chains.OrderBy(c => Vector3.Distance(rest[c[0]].Pos, rest[hand].Pos) - 0.3f * chains.Where(o => o != c).Sum(o => 1f - Vector3.Dot(Dir(c), Dir(o)))).First();
        // Order along the knuckle line: from the knuckle nearest the thumb to the farthest one.
        var others = chains.Where(c => c != thumb).ToList();
        var near = others.OrderBy(c => Vector3.Distance(rest[c[0]].Pos, rest[thumb[0]].Pos)).First();
        var far = others.OrderByDescending(c => Vector3.Distance(rest[c[0]].Pos, rest[thumb[0]].Pos)).First();
        var line = rest[far[0]].Pos - rest[near[0]].Pos;
        var fingers = others.OrderBy(c => Vector3.Dot(rest[c[0]].Pos - rest[near[0]].Pos, line)).ToList();
        var result = new Dictionary<FingerKind, List<int>> { [FingerKind.Thumb] = thumb };
        var kinds = new[] { FingerKind.Index, FingerKind.Middle, FingerKind.Ring, FingerKind.Pinky };
        for (var i = 0; i < fingers.Count && i < kinds.Length; i++)
            result[kinds[i]] = fingers[i];
        return result;
    }

    private static bool IsHelper(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("twist") || n.Contains("helper") || n.Contains("roll") || n.Contains("ikrule") || n.Contains("_ik") || n.Contains("ik_") || n.Contains("hold") || n.Contains("attach") || n.Contains("target");
    }

    private static bool Descends(Skeleton skeleton, int bone, int ancestor)
    {
        for (var b = skeleton[bone].ParentIndex; b >= 0; b = skeleton[b].ParentIndex)
            if (b == ancestor)
                return true;
        return false;
    }

    private static Vector3 SafeNormal(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : fallback;

    /// <summary>Palm frame in hand-local space: X = fingers, Z = palm normal, Y completes it.</summary>
    public Quaternion PalmFrame => MathQ.FromAxes(FingerDirection, Vector3.Cross(PalmNormal, FingerDirection));
}
