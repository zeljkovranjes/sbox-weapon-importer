namespace WeaponImporter;

/// <summary>
/// Result of an analytic two-bone solve. Both rotations are deltas expressed in the space the
/// input joints were given in (character model space in practice): <see cref="UpperDelta"/>
/// rotates the whole arm about the shoulder, <see cref="LowerDelta"/> is applied afterwards and
/// rotates the forearm (and everything below it) about the corrected elbow.
/// </summary>
public struct ArmSolution
{
    /// <summary>Rotation about the shoulder applied to the upper arm and all its descendants.</summary>
    public Rotation UpperDelta;

    /// <summary>Rotation about the corrected elbow applied after <see cref="UpperDelta"/>.</summary>
    public Rotation LowerDelta;

    /// <summary>Corrected elbow position.</summary>
    public Vector3 Elbow;

    /// <summary>Corrected wrist position (equals the target unless it was out of reach).</summary>
    public Vector3 Wrist;

    /// <summary>Distance from the shoulder to the requested target.</summary>
    public float TargetDistance;

    /// <summary>Full reach of the chain (upper length + lower length).</summary>
    public float Reach;

    /// <summary>How far the wrist stays short of the target (0 when reached).</summary>
    public float Shortfall;
}

/// <summary>
/// Analytic two-bone IK that keeps the animation's own elbow bend: the elbow bends the way the
/// animated arm already bends, so the correction stays small and never flips mid-action.
/// </summary>
public static class ArmSolver
{
    /// <summary>Fraction of full reach the chain is allowed to extend to (avoids a locked elbow).</summary>
    public const float MaxExtension = 0.999f;

    /// <summary>
    /// Solves shoulder / elbow / wrist so the wrist reaches <paramref name="target"/>.
    /// All positions share one space; the returned deltas are in that same space.
    /// </summary>
    public static ArmSolution Solve( Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 target )
        => Solve( shoulder, elbow, wrist, target, null, 0f );

    /// <summary>
    /// As <see cref="Solve(Vector3, Vector3, Vector3, Vector3)"/>, with the elbow leaning toward
    /// <paramref name="elbowHint"/> (a point, same space) by <paramref name="hintWeight"/> (0..1):
    /// a baked elbow direction that keeps the bend steady when the animation's own elbow is
    /// ambiguous (a nearly straight arm).
    /// </summary>
    public static ArmSolution Solve( Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 target, Vector3? elbowHint, float hintWeight )
    {
        var result = new ArmSolution
        {
            UpperDelta = Rotation.Identity,
            LowerDelta = Rotation.Identity,
            Elbow = elbow,
            Wrist = wrist,
        };

        var upper = elbow - shoulder;
        var lower = wrist - elbow;
        float a = upper.Length;
        float b = lower.Length;
        result.Reach = a + b;

        var toTarget = target - shoulder;
        float distance = toTarget.Length;
        result.TargetDistance = distance;

        if ( a < 1e-4f || b < 1e-4f || distance < 1e-4f )
        {
            result.Shortfall = MathF.Max( 0f, distance - result.Reach );
            return result;
        }

        var dir = toTarget / distance;

        float maxReach = MaxExtension * (a + b);
        float minReach = MathF.Max( MathF.Abs( a - b ) * 1.001f + 1e-3f, 1e-3f );
        float d = Math.Clamp( distance, minReach, maxReach );
        result.Shortfall = MathF.Max( 0f, distance - maxReach );

        var pole = BendDirection( upper, lower, dir );
        if ( elbowHint is { } hint && hintWeight > 0f )
        {
            var toHint = hint - shoulder;
            toHint -= dir * Vector3.Dot( toHint, dir );
            // A hint across the animated bend would swing the elbow through the arm: ignore it.
            if ( toHint.LengthSquared > 1e-6f && Vector3.Dot( toHint.Normal, pole ) > -0.3f )
            {
                var blended = pole * (1f - hintWeight) + toHint.Normal * hintWeight;
                blended -= dir * Vector3.Dot( blended, dir );
                if ( blended.LengthSquared > 1e-6f )
                    pole = blended.Normal;
            }
        }

        // Law of cosines for the shoulder angle.
        float cosA = Math.Clamp( (a * a + d * d - b * b) / (2f * a * d), -1f, 1f );
        float sinA = MathF.Sqrt( MathF.Max( 0f, 1f - cosA * cosA ) );

        var newElbow = shoulder + dir * (a * cosA) + pole * (a * sinA);
        var reached = shoulder + dir * d;

        var q1 = FromTo( upper, newElbow - shoulder );
        var movedLower = q1 * lower;
        var q2 = FromTo( movedLower, reached - newElbow );

        result.UpperDelta = q1;
        result.LowerDelta = q2;
        result.Elbow = newElbow;
        result.Wrist = newElbow + q2 * movedLower;
        return result;
    }

    /// <summary>
    /// Unit direction, perpendicular to <paramref name="dir"/>, towards which the elbow bends.
    /// Taken from the animated elbow; falls back to the animated bend plane normal when the
    /// animated elbow lies on the shoulder-target line.
    /// </summary>
    public static Vector3 BendDirection( Vector3 upper, Vector3 lower, Vector3 dir )
    {
        // The animation's own bend: the elbow's offset from the shoulder->animated-wrist line.
        // Measured against the animated wrist (not the target) so it never flips when the target
        // swings past the elbow during an action.
        var reach = upper + lower;
        var animPole = reach.LengthSquared > 1e-8f ? upper - reach.Normal * Vector3.Dot( upper, reach.Normal ) : Vector3.Zero;
        var armLength = upper.Length + lower.Length;

        // Nearly straight arms have no reliable bend; ease toward a relaxed down-and-back elbow.
        var relaxed = Vector3.Down * 0.8f + Vector3.Backward * 0.2f;
        float straightness = armLength > 1e-4f ? Math.Clamp( animPole.Length / (armLength * 0.08f), 0f, 1f ) : 0f;
        var pole = (animPole.LengthSquared > 1e-10f ? animPole.Normal : relaxed) * straightness + relaxed.Normal * (1f - straightness);

        // Perpendicular to the reach direction.
        pole -= dir * Vector3.Dot( pole, dir );
        if ( pole.LengthSquared > 1e-8f )
            return pole.Normal;

        var fallback = Vector3.Down - dir * Vector3.Dot( Vector3.Down, dir );
        if ( fallback.LengthSquared < 1e-6f )
            fallback = Vector3.Forward - dir * Vector3.Dot( Vector3.Forward, dir );
        return fallback.Normal;
    }

    /// <summary>Shortest-arc rotation taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static Rotation FromTo( Vector3 from, Vector3 to )
    {
        if ( from.LengthSquared < 1e-12f || to.LengthSquared < 1e-12f )
            return Rotation.Identity;

        var f = from.Normal;
        var t = to.Normal;
        float dot = Vector3.Dot( f, t );

        if ( dot < -0.999999f )
        {
            var axis = Vector3.Cross( f, Vector3.Up );
            if ( axis.LengthSquared < 1e-6f )
                axis = Vector3.Cross( f, Vector3.Forward );
            axis = axis.Normal;
            return new Rotation( axis.x, axis.y, axis.z, 0f );
        }

        // Half-way quaternion: q = (cross, 1 + dot), normalised.
        var c = Vector3.Cross( f, t );
        float w = 1f + dot;
        float len = MathF.Sqrt( c.x * c.x + c.y * c.y + c.z * c.z + w * w );
        return new Rotation( c.x / len, c.y / len, c.z / len, w / len );
    }

    /// <summary>
    /// Twist part of <paramref name="rotation"/> about unit <paramref name="axis"/> (swing-twist
    /// split, rotation = swing * twist). Identity when the rotation has no twist about the axis.
    /// </summary>
    public static Rotation Twist( Rotation rotation, Vector3 axis )
    {
        float d = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
        float x = axis.x * d, y = axis.y * d, z = axis.z * d, w = rotation.w;
        float len = MathF.Sqrt( x * x + y * y + z * z + w * w );
        if ( len < 1e-6f )
            return Rotation.Identity;
        return new Rotation( x / len, y / len, z / len, w / len );
    }

    /// <summary>Weighted rotation between identity and <paramref name="delta"/> (shortest path).</summary>
    public static Rotation Weighted( Rotation delta, float weight )
    {
        if ( weight >= 0.9999f )
            return delta;
        if ( weight <= 0.0001f )
            return Rotation.Identity;
        return Slerp( Rotation.Identity, delta, weight );
    }

    /// <summary>Shortest-path spherical interpolation that does not depend on engine helpers.</summary>
    public static Rotation Slerp( Rotation a, Rotation b, float t )
    {
        float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        float bx = b.x, by = b.y, bz = b.z, bw = b.w;
        if ( dot < 0f )
        {
            dot = -dot;
            bx = -bx; by = -by; bz = -bz; bw = -bw;
        }

        float s0, s1;
        if ( dot > 0.9995f )
        {
            s0 = 1f - t;
            s1 = t;
        }
        else
        {
            float theta = MathF.Acos( Math.Clamp( dot, -1f, 1f ) );
            float sin = MathF.Sin( theta );
            s0 = MathF.Sin( (1f - t) * theta ) / sin;
            s1 = MathF.Sin( t * theta ) / sin;
        }

        float x = s0 * a.x + s1 * bx;
        float y = s0 * a.y + s1 * by;
        float z = s0 * a.z + s1 * bz;
        float w = s0 * a.w + s1 * bw;
        float len = MathF.Sqrt( x * x + y * y + z * z + w * w );
        if ( len < 1e-12f )
            return a;
        return new Rotation( x / len, y / len, z / len, w / len );
    }

    /// <summary>
    /// Frame-rate independent exponential smoothing factor. <paramref name="smoothing"/> 0 returns 1
    /// (no smoothing); 1 gives a 0.1 s half-life.
    /// </summary>
    public static float SmoothingFactor( float smoothing, float deltaTime )
    {
        if ( smoothing <= 0f )
            return 1f;
        if ( deltaTime <= 0f )
            return 0f;
        float halfLife = Math.Clamp( smoothing, 0f, 1f ) * 0.1f;
        return 1f - MathF.Pow( 2f, -deltaTime / halfLife );
    }
}
