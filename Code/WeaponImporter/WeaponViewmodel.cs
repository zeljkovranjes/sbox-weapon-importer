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

    /// <summary>Camera near clip while the view shows (the arms sit closer than the default 10).</summary>
    [Property] public float NearClip { get; set; } = 1f;

    private string _sequence = "";
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
        var camera = Scene.Camera;
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

    /// <summary>Plays the hold's current action at the hold's own time.</summary>
    private void Animate( SkinnedModelRenderer renderer )
    {
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
        // root * eyeInModel = view  =>  root = view * eyeInModel⁻¹
        renderer.WorldTransform = view.ToWorld( eyeInModel.ToLocal( global::Transform.Zero ) );
    }
}
