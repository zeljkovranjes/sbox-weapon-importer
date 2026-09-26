using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WeaponImporter;

/// <summary>One key of a hand contact track (see <see cref="WeaponHold.Contacts"/>).</summary>
public sealed class HoldContact
{
    /// <summary>True for the left (support) hand, false for the right hand.</summary>
    public bool Left { get; set; } = true;

    /// <summary>Normalized action time (0..1) at which the blend starts.</summary>
    public float T { get; set; }

    /// <summary>True blends to locked (weight 1), false to released (weight 0).</summary>
    public bool Lock { get; set; }

    /// <summary>Blend duration in seconds.</summary>
    public float Blend { get; set; }

    /// <summary>Weapon bone the hand target follows while locked ("" = the weapon itself).</summary>
    public string Follow { get; set; } = "";

    /// <summary>True when <see cref="Offset"/> was given.</summary>
    public bool HasOffset { get; set; }

    /// <summary>Extra weapon-space offset: position added, rotation pre-multiplied (weapon axes).</summary>
    public Transform Offset { get; set; } = Transform.Zero;
}

/// <summary>One baked weapon action (see <see cref="WeaponHold.Actions"/>).</summary>
public sealed class ActionInfo
{
    /// <summary>Lowercase role key (idle, fire, reload, ...).</summary>
    public string Role { get; set; } = "";

    /// <summary>Action duration in seconds (0 = use the sequence length, or 1 s).</summary>
    public float Seconds { get; set; }

    /// <summary>Weapon model sequence to play ("" = none).</summary>
    public string Sequence { get; set; } = "";

    /// <summary>Character animgraph bool whose rising edge starts the action ("" = code only).</summary>
    public string Trigger { get; set; } = "";

    /// <summary>More weapon sequences played in turn with <see cref="Sequence"/> (left and right punches, a combo of slashes).</summary>
    public List<string> Variants { get; set; } = new();
}

/// <summary>A baked finger bone local rotation.</summary>
public readonly struct FingerRotation
{
    /// <summary>Creates a finger entry.</summary>
    public FingerRotation( string bone, Rotation rotation )
    {
        Bone = bone;
        Rotation = rotation;
    }

    /// <summary>Bone name.</summary>
    public string Bone { get; }

    /// <summary>Rotation relative to the parent bone.</summary>
    public Rotation Rotation { get; }
}

/// <summary>Parsing, formatting and evaluation of the plain-string baked hold data.</summary>
public static class HoldData
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Parses "px,py,pz,qx,qy,qz,qw" (invariant culture). The quaternion is normalized.</summary>
    public static bool TryParseTransform( string s, out Transform t )
    {
        t = Transform.Zero;
        if ( string.IsNullOrWhiteSpace( s ) )
            return false;

        var parts = s.Split( ',' );
        if ( parts.Length != 7 )
            return false;

        var v = new float[7];
        for ( int i = 0; i < 7; i++ )
        {
            if ( !TryFloat( parts[i], out v[i] ) )
                return false;
        }

        if ( !TryMakeRotation( v[3], v[4], v[5], v[6], out var rotation ) )
            return false;

        t = new Transform( new Vector3( v[0], v[1], v[2] ), rotation );
        return true;
    }

    /// <summary>Formats a transform as "px,py,pz,qx,qy,qz,qw" with 6 decimals (invariant culture).</summary>
    public static string FormatTransform( Transform t )
    {
        var p = t.Position;
        var r = t.Rotation;
        var sb = new StringBuilder( 96 );
        Append( sb, p.x ).Append( ',' );
        Append( sb, p.y ).Append( ',' );
        Append( sb, p.z ).Append( ',' );
        Append( sb, r.x ).Append( ',' );
        Append( sb, r.y ).Append( ',' );
        Append( sb, r.z ).Append( ',' );
        Append( sb, r.w );
        return sb.ToString();
    }

    /// <summary>
    /// Parses "bone=qx,qy,qz,qw;bone=..." into <paramref name="into"/> (cleared first).
    /// Empty input is valid and yields no entries.
    /// </summary>
    public static bool TryParseFingers( string s, List<FingerRotation> into, out string error )
    {
        into.Clear();
        error = null;
        if ( string.IsNullOrWhiteSpace( s ) )
            return true;

        foreach ( var raw in s.Split( ';' ) )
        {
            var entry = raw.Trim();
            if ( entry.Length == 0 )
                continue;

            int eq = entry.IndexOf( '=' );
            if ( eq <= 0 )
            {
                error = $"finger entry '{entry}' has no bone name";
                into.Clear();
                return false;
            }

            var name = entry.Substring( 0, eq ).Trim();
            var q = entry.Substring( eq + 1 ).Split( ',' );
            if ( q.Length != 4
                || !TryFloat( q[0], out var x ) || !TryFloat( q[1], out var y )
                || !TryFloat( q[2], out var z ) || !TryFloat( q[3], out var w )
                || !TryMakeRotation( x, y, z, w, out var rotation ) )
            {
                error = $"finger entry '{entry}' needs 4 finite quaternion components";
                into.Clear();
                return false;
            }

            into.Add( new FingerRotation( name, rotation ) );
        }

        return true;
    }

    /// <summary>
    /// Parses the Actions JSON object into <paramref name="into"/> (cleared first, keys lowercase).
    /// Empty input is valid.
    /// </summary>
    public static bool TryParseActions( string json, Dictionary<string, ActionInfo> into, out string error )
    {
        into.Clear();
        error = null;
        if ( string.IsNullOrWhiteSpace( json ) )
            return true;

        try
        {
            using var doc = JsonDocument.Parse( json );
            if ( doc.RootElement.ValueKind != JsonValueKind.Object )
            {
                error = "actions JSON must be an object";
                return false;
            }

            foreach ( var prop in doc.RootElement.EnumerateObject() )
            {
                if ( prop.Value.ValueKind != JsonValueKind.Object )
                    continue;

                var role = prop.Name.Trim().ToLowerInvariant();
                var info = new ActionInfo
                {
                    Role = role,
                    Seconds = MathF.Max( 0f, ReadFloat( prop.Value, "seconds", 0f ) ),
                    Sequence = ReadString( prop.Value, "sequence" ),
                    Trigger = ReadString( prop.Value, "trigger" ),
                };
                if ( prop.Value.TryGetProperty( "variants", out var variants ) && variants.ValueKind == JsonValueKind.Array )
                    foreach ( var v in variants.EnumerateArray() )
                        if ( v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace( v.GetString() ) )
                            info.Variants.Add( v.GetString() );
                into[role] = info;
            }

            return true;
        }
        catch ( JsonException e )
        {
            into.Clear();
            error = "actions JSON is invalid: " + e.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses the Contacts JSON object into <paramref name="into"/> (cleared first, keys lowercase,
    /// keys of each role sorted by time). Empty input is valid.
    /// </summary>
    public static bool TryParseContacts( string json, Dictionary<string, HoldContact[]> into, out string error )
    {
        into.Clear();
        error = null;
        if ( string.IsNullOrWhiteSpace( json ) )
            return true;

        try
        {
            using var doc = JsonDocument.Parse( json );
            if ( doc.RootElement.ValueKind != JsonValueKind.Object )
            {
                error = "contacts JSON must be an object";
                return false;
            }

            foreach ( var prop in doc.RootElement.EnumerateObject() )
            {
                if ( prop.Value.ValueKind != JsonValueKind.Array )
                    continue;

                var keys = new List<HoldContact>();
                foreach ( var item in prop.Value.EnumerateArray() )
                {
                    if ( item.ValueKind != JsonValueKind.Object )
                        continue;

                    var key = new HoldContact
                    {
                        Left = !string.Equals( ReadString( item, "hand" ).Trim(), "R", StringComparison.OrdinalIgnoreCase ),
                        T = Math.Clamp( ReadFloat( item, "t", 0f ), 0f, 1f ),
                        Lock = ReadBool( item, "lock", false ),
                        Blend = MathF.Max( 0f, ReadFloat( item, "blend", 0f ) ),
                        Follow = ReadString( item, "follow" ).Trim(),
                    };

                    if ( TryReadOffset( item, out var offset ) )
                    {
                        key.HasOffset = true;
                        key.Offset = offset;
                    }

                    keys.Add( key );
                }

                // Stable sort by time so authoring order breaks ties.
                into[prop.Name.Trim().ToLowerInvariant()] = keys.OrderBy( k => k.T ).ToArray();
            }

            return true;
        }
        catch ( JsonException e )
        {
            into.Clear();
            error = "contacts JSON is invalid: " + e.Message;
            return false;
        }
    }

    /// <summary>
    /// Evaluates a hand contact track. The hand starts locked on <paramref name="baseTarget"/>;
    /// each key reached blends (smoothstep over its blend seconds) towards locked or released.
    /// Lock keys also blend the target to the key's own target (resolved by <paramref name="resolve"/>),
    /// release keys keep the previous target. Returns the weight (0..1).
    /// </summary>
    /// <param name="keys">Keys sorted by time; only keys matching <paramref name="left"/> are used.</param>
    /// <param name="left">Evaluate the left-hand (true) or right-hand (false) keys.</param>
    /// <param name="seconds">Elapsed action time in seconds.</param>
    /// <param name="duration">Action duration in seconds (for the normalized key times).</param>
    /// <param name="baseTarget">Weapon-space target before any key.</param>
    /// <param name="resolve">Resolves a lock key's weapon-space target (null = base target + offset).</param>
    /// <param name="target">Resulting weapon-space hand target.</param>
    public static float EvaluateContacts( HoldContact[] keys, bool left, float seconds, float duration,
        Transform baseTarget, Func<HoldContact, Transform> resolve, out Transform target )
    {
        float weight = 1f;
        target = baseTarget;
        if ( keys == null || keys.Length == 0 )
            return weight;

        duration = MathF.Max( duration, 1e-4f );
        for ( int i = 0; i < keys.Length; i++ )
        {
            var key = keys[i];
            if ( key.Left != left )
                continue;

            float start = key.T * duration;
            if ( seconds < start )
                break;

            float s = key.Blend <= 0f ? 1f : SmoothStep( (seconds - start) / key.Blend );
            float before = weight;
            weight = weight + ((key.Lock ? 1f : 0f) - weight) * s;

            if ( key.Lock )
            {
                var keyTarget = resolve != null ? resolve( key ) : ApplyOffset( key, baseTarget );
                // Coming from a released hand (playing its own animation) the old target has no
                // weight, so the new one is taken at once rather than slid across the weapon.
                target = before < ReleasedWeight ? keyTarget : Lerp( target, keyTarget, s );
            }
        }

        return Math.Clamp( weight, 0f, 1f );
    }

    /// <summary>Contact weight under which a hand counts as released (its target no longer shows).</summary>
    public const float ReleasedWeight = 0.02f;

    /// <summary>Applies a key's optional offset to a weapon-space target.</summary>
    public static Transform ApplyOffset( HoldContact key, Transform target )
    {
        if ( key == null || !key.HasOffset )
            return target;
        return new Transform( target.Position + key.Offset.Position, key.Offset.Rotation * target.Rotation );
    }

    /// <summary>Position lerp + shortest-path rotation slerp (scale 1).</summary>
    public static Transform Lerp( Transform a, Transform b, float t )
    {
        if ( t <= 0f )
            return a;
        if ( t >= 1f )
            return b;
        return new Transform( a.Position + (b.Position - a.Position) * t, ArmSolver.Slerp( a.Rotation, b.Rotation, t ) );
    }

    /// <summary>Clamped smoothstep.</summary>
    public static float SmoothStep( float x )
    {
        x = Math.Clamp( x, 0f, 1f );
        return x * x * (3f - 2f * x);
    }

    private static bool TryFloat( string s, out float value )
    {
        if ( float.TryParse( s.Trim(), NumberStyles.Float, Invariant, out value ) && float.IsFinite( value ) )
            return true;
        value = 0f;
        return false;
    }

    private static bool TryMakeRotation( float x, float y, float z, float w, out Rotation rotation )
    {
        rotation = Rotation.Identity;
        float len = MathF.Sqrt( x * x + y * y + z * z + w * w );
        if ( !float.IsFinite( len ) || len < 1e-6f )
            return false;
        rotation = new Rotation( x / len, y / len, z / len, w / len );
        return true;
    }

    private static StringBuilder Append( StringBuilder sb, float v )
    {
        if ( !float.IsFinite( v ) )
            v = 0f;
        var text = v.ToString( "F6", Invariant );
        // Avoid "-0.000000".
        if ( text == "-0.000000" )
            text = "0.000000";
        return sb.Append( text );
    }

    private static bool TryGet( JsonElement obj, string name, out JsonElement value )
    {
        foreach ( var p in obj.EnumerateObject() )
        {
            if ( string.Equals( p.Name, name, StringComparison.OrdinalIgnoreCase ) )
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static float ReadFloat( JsonElement obj, string name, float fallback )
    {
        if ( !TryGet( obj, name, out var v ) )
            return fallback;
        if ( v.ValueKind == JsonValueKind.Number && v.TryGetDouble( out var d ) && double.IsFinite( d ) )
            return (float)d;
        if ( v.ValueKind == JsonValueKind.String && TryFloat( v.GetString() ?? "", out var f ) )
            return f;
        return fallback;
    }

    private static string ReadString( JsonElement obj, string name )
    {
        if ( !TryGet( obj, name, out var v ) )
            return "";
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    private static bool ReadBool( JsonElement obj, string name, bool fallback )
    {
        if ( !TryGet( obj, name, out var v ) )
            return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetDouble( out var d ) && d != 0,
            JsonValueKind.String => string.Equals( v.GetString(), "true", StringComparison.OrdinalIgnoreCase ),
            _ => fallback,
        };
    }

    private static bool TryReadOffset( JsonElement obj, out Transform offset )
    {
        offset = Transform.Zero;
        if ( !TryGet( obj, "offset", out var v ) )
            return false;

        if ( v.ValueKind == JsonValueKind.String )
            return TryParseTransform( v.GetString(), out offset );

        if ( v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 7 )
            return false;

        var f = new float[7];
        int i = 0;
        foreach ( var e in v.EnumerateArray() )
        {
            if ( e.ValueKind != JsonValueKind.Number || !e.TryGetDouble( out var d ) || !double.IsFinite( d ) )
                return false;
            f[i++] = (float)d;
        }

        if ( !TryMakeRotation( f[3], f[4], f[5], f[6], out var rotation ) )
            return false;

        offset = new Transform( new Vector3( f[0], f[1], f[2] ), rotation );
        return true;
    }
}
