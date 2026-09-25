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

    private string _sequence = "";

    protected override void OnPreRender()
    {
        if ( !Renderer.IsValid() )
            Renderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
        if ( !Hold.IsValid() )
            Hold = Components.GetInAncestorsOrSelf<WeaponHold>();
        var renderer = Renderer;
        if ( !renderer.IsValid() )
            return;
        var camera = Scene.Camera;
        var show = FirstPerson && !IsProxy && camera.IsValid();
        renderer.Enabled = show;
        if ( HideWorldWeapon && Hold.IsValid() && Hold.WeaponRenderer is { } world && world.IsValid() && world != renderer )
            world.RenderType = show ? ModelRenderer.ShadowRenderType.ShadowsOnly : ModelRenderer.ShadowRenderType.On;
        if ( !show )
            return;

        UpdateView( camera.WorldTransform );
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
