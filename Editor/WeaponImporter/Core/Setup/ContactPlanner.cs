#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;

namespace WeaponImporter.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Plans when the support hand holds what during an action, from the character's own animation
/// sampled over the action (the hand's wrist in weapon space). The source motion is kept: the
/// hand only locks onto a target while its animation already brings it close, so each
/// correction is as small as the animation allows, and it plays its own motion in between.
/// </summary>
public static class ContactPlanner
{
    /// <summary>The animated support wrist at a normalized action time, in weapon space.</summary>
    public readonly record struct Sample(float Time, XForm Wrist);

    /// <summary>How close to the weapon (its bounds, or the magazine without them) the wrist must come for a touch, inches.</summary>
    public const float TouchRadius = 8f;

    /// <summary>How much worse than a touch's best moment a neighbouring moment may be and still belong to it.</summary>
    public const float TouchBand = 2.5f;

    /// <summary>Inches of touch cost per inch/second of hand speed (a working hand slows down).</summary>
    public const float SpeedWeight = 0.05f;

    /// <summary>Inches of touch cost per inch of distance to this weapon's magazine.</summary>
    public const float MagazineWeight = 0.15f;

    /// <summary>The hand counts as having left the grip once its animation moved this far (inches).</summary>
    public const float LeaveDistance = 2.5f;

    /// <summary>After its farthest point the hand is coming home once it is this close to its final place.</summary>
    public const float HomeRadius = 6f;

    /// <summary>A second touch is kept only when its correction is at most this large (inches)...</summary>
    public const float SecondTouchMaxShift = 10f;

    /// <summary>...and not much larger than the main touch's (inches).</summary>
    public const float SecondTouchSlack = 4f;

    /// <summary>Speed (in/s) a corrected hand may be pulled toward its target; sets the blend length.</summary>
    public const float CorrectionSpeed = 30f;

    /// <summary>Blend length for a correction of <paramref name="shift"/> inches.</summary>
    public static float BlendFor(float shift) => Math.Clamp(0.12f + shift / CorrectionSpeed, TouchBlend, 0.6f);

    public const float ReleaseBlend = 0.18f;
    public const float TouchBlend = 0.16f;
    public const float ReturnBlend = 0.28f;

    public sealed record Plan(List<ContactKey> Keys, List<(float Start, float End)> Touches, float ClosestApproach, string Note);

    /// <summary>
    /// Reload plan: grip → release as the animation leaves → lock on the magazine while the
    /// animated hand is at it (up to two touches: remove, insert) → back on the grip when the
    /// animation returns. Without usable samples the hand simply lets go and comes back.
    /// </summary>
    public static Plan PlanReload(IReadOnlyList<Sample> samples, XForm grip, XForm magazine, float seconds, Geometry.Bounds? weapon = null, float fallbackRelease = 0.18f, float fallbackReturn = 0.86f)
    {
        seconds = MathF.Max(0.2f, seconds);
        float N(float s) => s / seconds; // seconds -> normalized
        var offset = OffsetFrom(grip, magazine);

        if (samples.Count < 4)
            return Fallback("no samples of the character's reload", float.NaN);

        // Where the animation works the magazine: the hand has left its hold, is close to the
        // weapon and slows down (grabbing, pulling, inserting). The character's animation was made
        // for its own weapon, so these moments are found on the weapon as a whole and then
        // retargeted onto this weapon's magazine. Near-magazine moments are preferred.
        var dt = seconds / MathF.Max(1, samples.Count - 1);
        var d = new float[samples.Count];
        // The farthest point of the hand's trip splits leaving from coming home.
        var far = Enumerable.Range(0, samples.Count).MaxBy(i => Vector3.Distance(samples[i].Wrist.Pos, samples[0].Wrist.Pos));
        for (var i = 0; i < samples.Count; i++)
        {
            var p = samples[i].Wrist.Pos;
            var away = i <= far
                ? Vector3.Distance(p, samples[0].Wrist.Pos) >= LeaveDistance
                : Vector3.Distance(p, samples[^1].Wrist.Pos) >= HomeRadius;
            var toMag = Vector3.Distance(p, magazine.Pos);
            var toWeapon = weapon is { } b ? MathF.Sqrt(b.DistanceSquared(p)) : toMag;
            var speed = Vector3.Distance(samples[Math.Min(i + 1, samples.Count - 1)].Wrist.Pos, samples[Math.Max(i - 1, 0)].Wrist.Pos) / (2f * dt);
            d[i] = away && toWeapon <= TouchRadius ? toWeapon + speed * SpeedWeight + toMag * MagazineWeight : float.MaxValue;
        }
        var closest = d.Min();
        if (closest == float.MaxValue)
            return Fallback("the reload never brings the hand to the weapon", float.NaN);

        // Touch windows: around each local minimum below the radius, while within the band.
        var touches = new List<(int A, int B)>();
        var used = new bool[samples.Count];
        foreach (var i in Enumerable.Range(0, samples.Count).OrderBy(i => d[i]))
        {
            if (used[i] || d[i] == float.MaxValue || touches.Count >= 2)
                continue;
            var limit = d[i] + TouchBand;
            int a = i, b = i;
            while (a > 0 && !used[a - 1] && d[a - 1] <= limit) a--;
            while (b < samples.Count - 1 && !used[b + 1] && d[b + 1] <= limit) b++;
            for (var k = a; k <= b; k++) used[k] = true;
            // Keep the windows apart so the hand is released in between.
            for (var k = Math.Max(0, a - 2); k <= Math.Min(samples.Count - 1, b + 2); k++) used[k] = true;
            touches.Add((a, b));
        }
        // How far each touch moves the hand off its animation (the correction it needs).
        float Shift((int A, int B) w) => Enumerable.Range(w.A, w.B - w.A + 1).Min(i => Vector3.Distance(samples[i].Wrist.Pos, magazine.Pos));
        // The smallest correction is the touch that matters; another one only when it asks for a
        // similar, modest move (a big second pull reads as the arm being yanked around).
        var primary = touches.OrderBy(Shift).First();
        var primaryShift = Shift(primary);
        touches = touches.Where(w => w == primary || Shift(w) <= MathF.Min(SecondTouchMaxShift, primaryShift + SecondTouchSlack)).ToList();
        touches.Sort((x, y) => x.A.CompareTo(y.A));

        var start = samples[0].Wrist.Pos;
        var end = samples[^1].Wrist.Pos;
        var firstTouch = samples[touches[0].A].Time;
        var lastTouch = samples[touches[^1].B].Time;
        // Leaves the grip: first moment the animation moved away from where it started.
        var leave = samples.FirstOrDefault(s => s.Time < firstTouch && Vector3.Distance(s.Wrist.Pos, start) > LeaveDistance).Time;
        if (leave <= 0f)
            leave = MathF.Max(0.02f, firstTouch - N(ReleaseBlend + TouchBlend) - 0.05f);
        // Back at the grip: first moment after the last touch the animation is home again.
        var back = samples.FirstOrDefault(s => s.Time > lastTouch && Vector3.Distance(s.Wrist.Pos, end) < LeaveDistance).Time;
        if (back <= 0f)
            back = MathF.Min(0.95f, MathF.Max(lastTouch + 0.05f, fallbackReturn));

        var keys = new List<ContactKey>();
        var cursor = 0f;
        void Add(float t, ContactState state, float blend, float[]? off, string target)
        {
            t = Math.Clamp(MathF.Max(t, cursor), 0f, 0.98f);
            keys.Add(new ContactKey { Hand = Side.Left, Time = t, State = state, Blend = blend, Offset = off, Target = target });
            // The next key starts only once this blend is over (a lock never starts from a half release).
            cursor = t + N(blend) + 0.005f;
        }

        Add(leave - N(ReleaseBlend) * 0.5f, ContactState.Released, ReleaseBlend, null, "");
        var touchList = new List<(float, float)>();
        foreach (var window in touches)
        {
            var ta = samples[window.A].Time;
            var tb = samples[window.B].Time;
            // The farther the hand is taken off its animation, the longer it eases in and out,
            // so it never moves faster than a hand would.
            var blend = BlendFor(Shift(window));
            Add(ta - N(blend), ContactState.Locked, blend, offset, "magazine");
            var lockedAt = keys[^1].Time + N(blend);
            Add(MathF.Max(tb, lockedAt), ContactState.Released, blend, null, "");
            touchList.Add((lockedAt, keys[^1].Time));
        }
        Add(back - N(ReturnBlend), ContactState.Locked, ReturnBlend, null, "grip");
        // How far the hand is moved onto this weapon's magazine at each touch (the correction size).
        var shift = touches.Max(t => Enumerable.Range(t.A, t.B - t.A + 1).Min(i => Vector3.Distance(samples[i].Wrist.Pos, magazine.Pos)));
        return new Plan(keys, touchList, shift, $"{touches.Count} magazine touch{(touches.Count == 1 ? "" : "es")}, hand moved up to {shift:0.0} in onto this magazine");

        Plan Fallback(string why, float c)
        {
            var k = new List<ContactKey>
            {
                new() { Hand = Side.Left, Time = fallbackRelease, State = ContactState.Released, Blend = 0.3f },
                new() { Hand = Side.Left, Time = fallbackReturn, State = ContactState.Locked, Blend = 0.3f, Target = "grip" },
            };
            return new Plan(k, new List<(float, float)>(), c, why);
        }
    }

    /// <summary>
    /// Reload without magazine contact: the support hand lets go when the character's animation
    /// takes it off the weapon and comes back when the animation returns it, so the reload plays
    /// exactly as animated in between. Timing comes from the animation, not fixed fractions.
    /// </summary>
    public static Plan PlanRelease(IReadOnlyList<Sample> samples, float seconds, float fallbackRelease = 0.18f, float fallbackReturn = 0.86f)
    {
        seconds = MathF.Max(0.2f, seconds);
        float N(float s) => s / seconds;
        var release = fallbackRelease;
        var back = fallbackReturn;
        if (samples.Count >= 4)
        {
            var start = samples[0].Wrist.Pos;
            var end = samples[^1].Wrist.Pos;
            var far = Enumerable.Range(0, samples.Count).MaxBy(i => Vector3.Distance(samples[i].Wrist.Pos, start));
            if (Vector3.Distance(samples[far].Wrist.Pos, start) > LeaveDistance * 2f)
            {
                var leave = samples.FirstOrDefault(s => Vector3.Distance(s.Wrist.Pos, start) > LeaveDistance);
                release = MathF.Max(0f, leave.Time - N(ReleaseBlend) * 0.5f);
                var home = samples.Skip(far).FirstOrDefault(s => Vector3.Distance(s.Wrist.Pos, end) < LeaveDistance);
                back = home.Time > 0f ? MathF.Max(release + N(ReleaseBlend) + 0.01f, home.Time - N(ReturnBlend)) : fallbackReturn;
            }
        }
        var keys = new List<ContactKey>
        {
            new() { Hand = Side.Left, Time = release, State = ContactState.Released, Blend = ReleaseBlend },
            new() { Hand = Side.Left, Time = Math.Clamp(back, 0f, 0.98f), State = ContactState.Locked, Blend = ReturnBlend, Target = "grip" },
        };
        return new Plan(keys, new List<(float, float)>(), 0f, "the hand follows the character's reload");
    }

    /// <summary>
    /// The contact key offset that turns the grip target into <paramref name="target"/>
    /// (position added, rotation pre-multiplied, as WeaponHold applies it).
    /// </summary>
    public static float[] OffsetFrom(XForm grip, XForm target)
    {
        var rot = MathQ.Normalize(target.Rot * Quaternion.Conjugate(grip.Rot));
        var pos = target.Pos - grip.Pos;
        return new[] { pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, rot.W };
    }

    /// <summary>Applies an offset like the runtime does (for checks and previews).</summary>
    public static XForm ApplyOffset(XForm grip, float[]? offset)
    {
        if (offset is not { Length: 7 })
            return grip;
        var rot = MathQ.Normalize(new Quaternion(offset[3], offset[4], offset[5], offset[6]));
        return new XForm(grip.Pos + new Vector3(offset[0], offset[1], offset[2]), MathQ.Normalize(rot * grip.Rot));
    }
}
