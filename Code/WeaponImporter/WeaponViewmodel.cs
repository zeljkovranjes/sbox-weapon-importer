using Sandbox;

namespace WeaponImporter;

/// <summary>
/// The first-person view of a weapon: the imported arms and weapon, placed so the rig's camera
/// sits at the player's eye, and shown only to the player who holds it. It plays the same action
/// as the <see cref="WeaponHold"/> beside it on the same clock, so first and third person never
/// drift apart: <c>WeaponHold.Play( "reload" )</c> (or the character's own reload) drives both.
/// </summary>
[Title( "Weapon Viewmodel" )]
[Category( "Weapons" )]
[Icon( "videocam" )]
public sealed class WeaponViewmodel : Component
{
    /// <summary>The first-person model (arms and weapon); default: the renderer on this object.</summary>
    [Property] public SkinnedModelRenderer Renderer { get; set; }

    /// <summary>The third-person hold whose actions this view follows; default: the one above this object.</summary>
    [Property] public WeaponHold Hold { get; set; }

    /// <summary>Show the first-person view (turn off while the player uses a third-person camera).</summary>
    [Property] public bool FirstPerson { get; set; } = true;

    /// <summary>While in first person, the owner sees only the shadow of the third-person weapon.</summary>
    [Property] public bool HideWorldWeapon { get; set; } = true;

    /// <summary>Bone of the rig's camera ("" = none: <see cref="EyeInModel"/> is used as is).</summary>
    [Property] public string CameraBone { get; set; } = "";

    /// <summary>
    /// Turns the camera bone's axes into s&amp;box's view axes (forward +X, up +Z); exporters
    /// disagree on how a camera bone is oriented.
    /// </summary>
    [Property] public Rotation CameraAxes { get; set; } = Rotation.Identity;

    /// <summary>Eye in model space, used when the rig has no camera bone.</summary>
    [Property] public global::Transform EyeInModel { get; set; } = global::Transform.Zero;

    /// <summary>Extra offset of the view, in eye space (fine-tune the weapon's place on screen).</summary>
    [Property] public global::Transform Offset { get; set; } = global::Transform.Zero;

    /// <summary>
    /// Strength of the kick when firing while aimed with a weapon that has no aimed-fire
    /// animation (its graph keeps the sights up; this gives each shot some feedback). 0 = none.
    /// </summary>
    [Property, Range( 0, 3 )] public float AimKick { get; set; } = 1f;

    private const float KickBack = 0.6f;       // inches toward the eye
    private const float KickPitch = 1.5f;      // degrees muzzle up
    private const float KickRecover = 0.15f;   // seconds to settle
    private float _kick;

    /// <summary>Camera near clip while the view shows (the arms sit closer than the default 10).</summary>
    [Property] public float NearClip { get; set; } = 1f;

    private string _sequence = "";
    private WeaponHold _listening;

    /// <summary>The viewmodel plays its own animgraph (the importer generates one when the file has an idle).</summary>
    public bool UsesGraph => Renderer.IsValid() && Renderer.Model?.AnimGraph is not null;
    private bool _detached;
    private bool _showing;
    private CameraComponent _nearCamera;
    private float _savedNear;

    // Visibility is decided every update (not only when a frame is drawn), the pose and place
    // right before rendering, from the camera's final transform.
    protected override void OnUpdate()
    {
        if ( !Renderer.IsValid() )
            Renderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
        if ( !Hold.IsValid() && !_detached )
            Hold = Components.GetInAncestorsOrSelf<WeaponHold>();
        // The view is placed in world space every frame, so it doesn't need its parents. Player
        // controllers hide the body (and everything under it) from the owner's camera in first
        // person, so it leaves the body's hierarchy and follows its weapon from the scene root.
        if ( !_detached && Hold.IsValid() )
        {
            _detached = true;
            GameObject.SetParent( null, true );
        }
        if ( _detached && !Hold.IsValid() )
        {
            GameObject.Destroy();
            return;
        }
        var renderer = Renderer;
        if ( !renderer.IsValid() )
            return;
        Listen( Hold );
        DriveShellReload( renderer );
        if ( UsesGraph && renderer.UseAnimGraph )
            renderer.Set( "ironsights", Hold.IsValid() && Hold.Aiming ? 1 : 0 );
        var camera = Scene.Camera;
        if ( _kick > 0f )
            _kick = MathF.Max( 0f, _kick - Time.Delta / KickRecover );
        _showing = FirstPerson && Hold.IsValid() && Hold.Active && !Hold.IsProxy && camera.IsValid();
        renderer.Enabled = _showing;
        SetNearClip( _showing ? camera : null );
        if ( HideWorldWeapon && Hold.IsValid() && Hold.WeaponRenderer is { } world && world.IsValid() && world != renderer )
            world.RenderType = _showing ? ModelRenderer.ShadowRenderType.ShadowsOnly : ModelRenderer.ShadowRenderType.On;
    }

    protected override void OnPreRender()
    {
        if ( _showing && Scene.Camera.IsValid() )
            UpdateView( Scene.Camera.WorldTransform );
    }

    /// <summary>Hears the hold's actions (the graph plays them on the viewmodel).</summary>
    private void Listen( WeaponHold hold )
    {
        if ( _listening == hold )
            return;
        if ( _listening.IsValid() )
            _listening.ActionStarted -= OnActionStarted;
        _listening = hold;
        if ( hold.IsValid() )
            hold.ActionStarted += OnActionStarted;
    }

    /// <summary>
    /// The hold started an action: the viewmodel's graph gets the matching parameter (the
    /// importer's generated graphs use these names; so do Facepunch's first-person graphs).
    /// </summary>
    private void OnActionStarted( string role )
    {
        var renderer = Renderer;
        if ( !UsesGraph || !renderer.IsValid() )
            return;
        renderer.UseAnimGraph = true;
        switch ( role )
        {
            case "fire":
            case "adsfire":
            case "fireempty":
                renderer.Set( "b_attack", true );
                // Aimed without an aimed-fire clip: the graph holds the sights up; kick instead.
                if ( Hold.IsValid() && Hold.Aiming && string.IsNullOrEmpty( Hold.SequenceFor( "adsfire" ) ) )
                    _kick = MathF.Min( 1.5f, _kick + 1f );
                break;
            case "reload":
            case "tacticalreload":
            case "emptyreload":
                renderer.Set( "b_empty", role == "emptyreload" );
                renderer.Set( "b_reload", true );
                break;
            case "reloadstart":
            case "reloadinsert":
                // Shell reload: DriveShellReload keeps b_reload on while more shells follow.
                renderer.Set( "b_reload", true );
                break;
            case "inspect":
                renderer.Set( "b_inspect", true );
                break;
            case "holster":
                renderer.Set( "b_holster", true );
                break;
            case "draw":
                renderer.Set( "b_holster", false );
                break;
        }
    }

    protected override void OnDestroy() => Listen( null );

    private bool _drivingShells;

    /// <summary>
    /// Shell-by-shell reloads: the graph loops its insert while b_reload stays on, so it is held
    /// exactly while another shell follows the current one, then released for the end part.
    /// </summary>
    private void DriveShellReload( SkinnedModelRenderer renderer )
    {
        if ( !UsesGraph || !Hold.IsValid() )
            return;
        if ( Hold.ShellReloading )
        {
            _drivingShells = true;
            // Held at the start of each insert too, so the graph always enters the reload; it only
            // reads b_reload again when the insert finishes.
            var more = Hold.CurrentRole == "reloadstart" ? Hold.ShellsRemaining > 0 : Hold.ShellsRemaining > 1 || Hold.CurrentTime < 0.25f;
            renderer.Set( "b_reload", more );
        }
        else if ( _drivingShells )
        {
            _drivingShells = false;
            renderer.Set( "b_reload", false );
        }
    }

    /// <summary>Lowers the camera's near clip while the view shows, and restores it afterwards.</summary>
    private void SetNearClip( CameraComponent camera )
    {
        if ( _nearCamera.IsValid() && _nearCamera != camera )
        {
            _nearCamera.ZNear = _savedNear;
            _nearCamera = null;
        }
        if ( camera.IsValid() && _nearCamera != camera )
        {
            _nearCamera = camera;
            _savedNear = camera.ZNear;
        }
        if ( _nearCamera.IsValid() )
            _nearCamera.ZNear = MathF.Min( _savedNear, NearClip );
    }

    protected override void OnDisabled()
    {
        SetNearClip( null );
        if ( Hold.IsValid() && Hold.WeaponRenderer is { } world && world.IsValid() )
            world.RenderType = ModelRenderer.ShadowRenderType.On;
    }

    /// <summary>Poses and places the view for an eye (the player's camera); called every frame it shows.</summary>
    public void UpdateView( global::Transform eye )
    {
        var renderer = Renderer;
        if ( !renderer.IsValid() )
            return;
        renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
        Animate( renderer );
        Place( renderer, eye );
    }

    /// <summary>The sequence the view plays (follows the hold's current action).</summary>
    public string PlayingSequence => _sequence;

    /// <summary>
    /// Without a graph: plays the hold's current action at the hold's own time. With one, the
    /// graph plays (actions arrive through <see cref="OnActionStarted"/>).
    /// </summary>
    private void Animate( SkinnedModelRenderer renderer )
    {
        if ( UsesGraph )
        {
            renderer.UseAnimGraph = true;
            return;
        }
        if ( !Hold.IsValid() )
            return;
        var sequence = Hold.SequenceFor( Hold.CurrentRole );
        if ( string.IsNullOrEmpty( sequence ) )
            sequence = Hold.SequenceFor( "idle" );
        var model = renderer.Model;
        if ( string.IsNullOrEmpty( sequence ) || model is null || !model.AnimationNames.Contains( sequence ) )
            return;
        renderer.UseAnimGraph = false;
        var player = renderer.Sequence;
        if ( _sequence != sequence )
        {
            _sequence = sequence;
            player.Name = sequence;
        }
        player.PlaybackRate = 0f;
        player.Time = Hold.CurrentTime * player.Duration;
    }

    /// <summary>Puts the model where its camera (as animated) meets the player's eye.</summary>
    private void Place( SkinnedModelRenderer renderer, global::Transform eye )
    {
        var eyeInModel = EyeInModel;
        if ( !string.IsNullOrEmpty( CameraBone ) && renderer.TryGetBoneTransform( CameraBone, out var boneWorld ) )
        {
            // Follow the camera's animation (recoil, reload sway).
            var bone = renderer.WorldTransform.ToLocal( boneWorld );
            eyeInModel = new global::Transform( bone.Position, bone.Rotation * CameraAxes );
        }
        var view = eye.ToWorld( Offset );
        if ( _kick > 0f && AimKick > 0f )
        {
            // Eased: a sharp push that settles smoothly.
            var k = _kick * _kick * AimKick;
            view = view.ToWorld( new global::Transform( new Vector3( -KickBack * k, 0f, 0f ), Rotation.FromPitch( -KickPitch * k ) ) );
        }
        // root * eyeInModel = view  =>  root = view * eyeInModel⁻¹
        renderer.WorldTransform = view.ToWorld( eyeInModel.ToLocal( global::Transform.Zero ) );
    }
}
