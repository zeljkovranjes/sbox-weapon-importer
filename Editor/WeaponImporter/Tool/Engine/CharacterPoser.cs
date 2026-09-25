using Sandbox;
using WeaponImporter.Core.Ik;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Tool;

/// <summary>
/// Poses a character with its own animgraph in a hidden editor scene and samples the result:
/// the reference pose grips are fitted to (holdtype idle, aiming straight ahead).
/// </summary>
public sealed class CharacterPoser : IDisposable
{
    private Scene _scene;
    private SkinnedModelRenderer _body;

    public Model Model { get; }
    public Skeleton Skeleton { get; }

    public CharacterPoser( string modelPath )
    {
        Model = Model.Load( modelPath );
        if ( Model is null || Model.IsError )
            throw new InvalidOperationException( $"Character model '{modelPath}' could not be loaded." );
        Skeleton = CharacterLibrary.SkeletonOf( Model );
        _scene = Scene.CreateEditorScene();
        using ( _scene.Push() )
        {
            var go = new GameObject( true, "character" );
            _body = go.AddComponent<SkinnedModelRenderer>();
            CharacterLibrary.Setup( _body, Model );
        }
    }

    /// <summary>Settles the animgraph with the weapon's holdtype and returns the pose.</summary>
    public CharacterPose Sample( int holdType, float seconds = 1.2f, string trigger = null, float triggerTime = 0f )
    {
        using ( _scene.Push() )
        {
            ApplyParameters( _body, holdType );
            var steps = Math.Max( 1, (int)(seconds * 30f) );
            var now = 0f;
            for ( var i = 0; i < steps; i++ )
            {
                if ( trigger is not null && MathF.Abs( now - triggerTime ) < 1f / 60f )
                    _body.Set( trigger, true );
                now += 1f / 30f;
                _scene.EditorTick( now, 1f / 30f );
            }
            return CharacterLibrary.Sample( _body, Skeleton );
        }
    }

    /// <summary>
    /// Whether the graph actually poses the arms for a holdtype (a graph without the model's
    /// sequences leaves the character in its bind pose).
    /// </summary>
    public bool Animates( WeaponImporter.Core.Grip.CharacterRig rig, int holdType )
    {
        var pose = Sample( holdType, 0.5f );
        var rest = new CharacterPose( Skeleton, Skeleton.Bones.Select( b => b.RestLocal ).ToArray() );
        bool Moved( int bone ) => System.Numerics.Vector3.Distance( pose.World[bone].Pos, rest.World[bone].Pos ) > 1f;
        return Moved( rig.Right.Hand ) || (rig.Left is { } left && Moved( left.Hand ));
    }

    /// <summary>Settle time before an action is triggered (the holdtype idle blends in first).</summary>
    public const float ActionLeadIn = 0.6f;

    // Scene clock of the track sampler (kept monotonic across calls).
    private float _clock = 1000f;

    /// <summary>
    /// Plays one action through the animgraph (trigger fired after <see cref="ActionLeadIn"/>)
    /// and samples the pose at each normalized time of an action lasting <paramref name="seconds"/>,
    /// in a single pass (60 Hz), the way <c>WeaponHold</c> times it at runtime.
    /// </summary>
    public List<(float Time, CharacterPose Pose)> SampleTrack( int holdType, string trigger, float seconds, int count )
    {
        var result = new List<(float, CharacterPose)>( count );
        count = Math.Max( 2, count );
        const float step = 1f / 60f;
        using ( _scene.Push() )
        {
            // Fresh graph state: reset every parameter and let any earlier action finish, so the
            // action starts from the settled holdtype idle.
            _body.SceneModel?.ResetAnimParameters();
            ApplyParameters( _body, holdType );
            var clock = _clock;
            for ( var i = 0; i < 150; i++ )
            {
                clock += step;
                _scene.EditorTick( clock, step );
            }
            var now = 0f;
            var next = 0;
            var end = ActionLeadIn + seconds + step;
            var fired = false;
            while ( now <= end && next < count )
            {
                if ( !fired && trigger is not null && now >= ActionLeadIn - step * 0.5f )
                {
                    _body.Set( trigger, true );
                    fired = true;
                }
                now += step;
                clock += step;
                _scene.EditorTick( clock, step );
                var t = next / (float)(count - 1);
                if ( now >= ActionLeadIn + t * seconds - step * 0.5f )
                {
                    result.Add( (t, CharacterLibrary.Sample( _body, Skeleton )) );
                    next++;
                }
            }
            _clock = clock;
        }
        return result;
    }

    /// <summary>
    /// How long the character's own action runs after its trigger: the time until both hands are
    /// back at their idle place (in the hold bone's frame) and stay there. 0 when the trigger
    /// doesn't move the hands at all.
    /// </summary>
    public float MeasureAction( int holdType, string trigger, float maxSeconds = 5f )
    {
        if ( trigger is null )
            return 0f;
        const float step = 1f / 60f;
        var hold = Model.Bones.GetBone( "hold_R" );
        var left = Model.Bones.GetBone( "hand_L" );
        var right = Model.Bones.GetBone( "hand_R" );
        if ( left is null || right is null )
            return 0f;
        using ( _scene.Push() )
        {
            _body.SceneModel?.ResetAnimParameters();
            ApplyParameters( _body, holdType );
            var clock = _clock;
            for ( var i = 0; i < 150; i++ )
            {
                clock += step;
                _scene.EditorTick( clock, step );
            }
            (Vector3 L, Vector3 R) Hands()
            {
                var root = _body.WorldTransform;
                _body.TryGetBoneTransformAnimation( left, out var l );
                _body.TryGetBoneTransformAnimation( right, out var r );
                // The body moves (steps, leans) during actions: measure hands against the chest.
                var frame = hold is not null && _body.TryGetBoneTransformAnimation( hold, out var h ) ? root.ToLocal( h ) : Transform.Zero;
                return (frame.PointToLocal( root.PointToLocal( l.Position ) ), frame.PointToLocal( root.PointToLocal( r.Position ) ));
            }
            var idle = Hands();
            _body.Set( trigger, true );
            var last = 0f;
            var moved = false;
            for ( var now = 0f; now < maxSeconds; now += step )
            {
                clock += step;
                _scene.EditorTick( clock, step );
                var (l, r) = Hands();
                var deviation = MathF.Max( (l - idle.L).Length, (r - idle.R).Length );
                if ( deviation > 1.0f )
                {
                    last = now;
                    moved = true;
                }
            }
            _clock = clock;
            return moved ? last + step : 0f;
        }
    }

    /// <summary>Animgraph parameters for a weapon held at the ready, aiming forward.</summary>
    public static void ApplyParameters( SkinnedModelRenderer body, WeaponType type ) => ApplyParameters( body, WeaponTypes.HoldType( type ) );

    /// <summary>Settles a weapon type's default hold.</summary>
    public CharacterPose Sample( WeaponType type ) => Sample( WeaponTypes.HoldType( type ) );

    /// <summary>Animgraph parameters for holding with <paramref name="holdType"/> (the citizen holdtype enum), aiming forward.</summary>
    public static void ApplyParameters( SkinnedModelRenderer body, int holdType )
    {
        body.Set( "holdtype", holdType );
        body.Set( "holdtype_handedness", 0 );
        body.Set( "b_grounded", true );
        body.Set( "move_x", 0f );
        body.Set( "move_y", 0f );
        body.Set( "move_speed", 0f );
        body.Set( "move_groundspeed", 0f );
        body.SetLookDirection( "aim_eyes", Vector3.Forward, 1f );
        body.SetLookDirection( "aim_head", Vector3.Forward, 1f );
        body.SetLookDirection( "aim_body", Vector3.Forward, 1f );
    }

    public void Dispose()
    {
        _scene?.Destroy();
        _scene = null;
    }
}
