using System.Text.Json;

namespace WeaponImporter;

/// <summary>A replacement character animation for one action.</summary>
public sealed class OverlayInfo
{
    public string Role;
    /// <summary>Model that owns the sequence ("" = the character's own model). Must share the character's skeleton.</summary>
    public string Model;
    public string Sequence;
    /// <summary>Fade in/out time in seconds.</summary>
    public float Blend = 0.2f;
    /// <summary>More sequences of the same model played in turn with <see cref="Sequence"/> (attack combos).</summary>
    public List<string> Variants = new();
}

/// <summary>
/// Plays a replacement character sequence for an action on a hidden model with the same
/// skeleton and blends it over the upper body (spine and everything above), so the weapon and
/// the hands follow it while the animgraph keeps driving the legs.
/// </summary>
internal sealed class CharacterOverlay
{
    private SceneModel _model;
    private Model _source;
    private string _sequence;
    private readonly Dictionary<string, int> _boneIndex = new( StringComparer.Ordinal );

    public bool Active { get; private set; }
    public float Weight { get; private set; }
    public float Duration { get; private set; }

    /// <summary>
    /// Bone the overlay is attached at: its upper body is moved so this bone stays where the
    /// character's own animation has it (a lunge or a crouch in the sequence bends the upper
    /// body without pulling it off the hips the animgraph keeps).
    /// </summary>
    public const string AnchorBone = "spine_0";

    private Vector3 _offset;

    /// <summary>Starts the overlay; returns false when the model or sequence is unusable.</summary>
    public bool Begin( SkinnedModelRenderer body, OverlayInfo info, out string error ) => Begin( body, info, info?.Sequence, out error );

    /// <summary>Starts the overlay with one of the info's sequences (its own or a variant).</summary>
    public bool Begin( SkinnedModelRenderer body, OverlayInfo info, string sequence, out string error )
    {
        error = null;
        Active = false;
        Weight = 0f;
        if ( info == null || string.IsNullOrEmpty( sequence ) || body?.SceneModel == null )
            return false;

        var model = string.IsNullOrEmpty( info.Model ) ? body.Model : Sandbox.Model.Load( info.Model );
        if ( model == null || model.IsError )
        {
            error = $"model '{info.Model}' could not be loaded";
            return false;
        }
        if ( !model.AnimationNames.Contains( sequence ) )
        {
            error = $"model '{model.Name}' has no sequence '{sequence}'";
            return false;
        }

        if ( _model == null || !_model.IsValid() || _source != model )
        {
            Dispose();
            _model = new SceneModel( body.SceneModel.World, model, body.SceneModel.Transform )
            {
                UseAnimGraph = false,
                RenderingEnabled = false,
            };
            _source = model;
            _boneIndex.Clear();
            for ( int i = 0; i < model.BoneCount; i++ )
                _boneIndex[model.GetBoneName( i )] = i;
        }

        _sequence = sequence;
        _model.CurrentSequence.Name = _sequence;
        _model.Update( 0f );
        Duration = _model.CurrentSequence.Duration;
        Active = true;
        return true;
    }

    /// <summary>Poses the overlay at <paramref name="seconds"/> into the action with the given blend weight.</summary>
    public void Update( SkinnedModelRenderer body, float seconds, float weight )
    {
        if ( !Active || _model == null || !_model.IsValid() )
            return;
        _model.Transform = body.SceneModel.Transform;
        _model.CurrentSequence.Name = _sequence;
        _model.CurrentSequence.Time = Math.Clamp( seconds, 0f, MathF.Max( 0f, Duration ) );
        _model.Update( 0f );
        Weight = Math.Clamp( weight, 0f, 1f );
        _offset = Vector3.Zero;
        var anchor = body.Model?.Bones.GetBone( AnchorBone );
        if ( anchor != null && _boneIndex.TryGetValue( AnchorBone, out var index ) && body.TryGetBoneTransformAnimation( anchor, out var own ) )
            _offset = own.Position - _model.GetBoneWorldTransform( index ).Position;
    }

    /// <summary>World transform of a bone in the overlay pose.</summary>
    public bool TryGetWorld( string bone, out Transform world )
    {
        world = global::Transform.Zero;
        if ( !Active || _model == null || !_boneIndex.TryGetValue( bone, out var index ) )
            return false;
        world = _model.GetBoneWorldTransform( index );
        world.Position += _offset;
        return true;
    }

    public void End()
    {
        Active = false;
        Weight = 0f;
    }

    public void Dispose()
    {
        if ( _model != null && _model.IsValid() )
            _model.Delete();
        _model = null;
        _source = null;
        Active = false;
    }

    /// <summary>Parses <c>{"reload":{"model":"","sequence":"reload_rifle","blend":0.2,"variants":["reload_2"]}}</c>.</summary>
    public static bool TryParse( string json, Dictionary<string, OverlayInfo> into, out string error )
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
                error = "expected an object";
                return false;
            }
            foreach ( var entry in doc.RootElement.EnumerateObject() )
            {
                if ( entry.Value.ValueKind != JsonValueKind.Object )
                    continue;
                var info = new OverlayInfo { Role = entry.Name.ToLowerInvariant() };
                if ( entry.Value.TryGetProperty( "model", out var m ) && m.ValueKind == JsonValueKind.String )
                    info.Model = m.GetString();
                if ( entry.Value.TryGetProperty( "sequence", out var s ) && s.ValueKind == JsonValueKind.String )
                    info.Sequence = s.GetString();
                if ( entry.Value.TryGetProperty( "blend", out var b ) && b.ValueKind == JsonValueKind.Number )
                    info.Blend = MathF.Max( 0f, b.GetSingle() );
                if ( entry.Value.TryGetProperty( "variants", out var v ) && v.ValueKind == JsonValueKind.Array )
                    foreach ( var item in v.EnumerateArray() )
                        if ( item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty( item.GetString() ) )
                            info.Variants.Add( item.GetString() );
                if ( !string.IsNullOrEmpty( info.Sequence ) )
                    into[info.Role] = info;
            }
            return true;
        }
        catch ( JsonException e )
        {
            error = e.Message;
            return false;
        }
    }
}
