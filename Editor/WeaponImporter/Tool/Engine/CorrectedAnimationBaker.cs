using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Generation;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Output;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool;

/// <summary>
/// Bakes the corrected third-person animation: each character action is played in a hidden
/// scene with the same Weapon Hold the game uses, the corrected pose is recorded every frame and
/// written as new clips on a model based on the character (<c>&lt;name&gt;_corrected.vmdl</c>).
/// The character's own animations are never changed.
/// </summary>
public static class CorrectedAnimationBaker
{
    public const float Fps = 30f;

    public sealed class Result
    {
        public string ModelPath { get; set; } = "";
        public List<(AnimationRole Role, string Sequence)> Clips { get; } = new();
        public List<string> Files { get; } = new();
        public List<string> Errors { get; } = new();
    }

    /// <summary>Actions baked: the idle loop and every action the character triggers itself.</summary>
    public static readonly AnimationRole[] Roles = { AnimationRole.Idle, AnimationRole.Fire, AnimationRole.Reload, AnimationRole.Draw };

    public static async Task<Result> BakeAsync( ImportSession session, string folder, string name, IProgress<string> progress, CancellationToken cancel )
    {
        await EditorThread.SwitchToMainThread();
        var result = new Result();
        var setup = session.Setup;
        var characterPath = session.CharacterModel;
        var model = Model.Load( characterPath );
        if ( model is null || model.IsError )
        {
            result.Errors.Add( $"character '{characterPath}' could not be loaded" );
            return result;
        }
        var skeleton = CharacterLibrary.SkeletonOf( model );
        var generated = $"{folder}/generated";
        var vmdl = new VmdlBuilder { BaseModel = characterPath };

        var scene = Scene.CreateEditorScene();
        try
        {
            SkinnedModelRenderer body;
            WeaponHold hold;
            using ( scene.Push() )
            {
                var bodyObject = new GameObject( true, "character" );
                body = bodyObject.AddComponent<SkinnedModelRenderer>();
                CharacterLibrary.Setup( body, model );
                var weaponObject = new GameObject( true, "weapon" );
                var weapon = weaponObject.AddComponent<SkinnedModelRenderer>();
                weapon.Model = PreviewModels.BuildWeaponModel( session.Analysis );
                weapon.UseAnimGraph = false;
                hold = weaponObject.AddComponent<WeaponHold>();
                hold.Body = body;
                hold.Weapon = weapon;
                WeaponBaker.ConfigureHold( hold, session );
                hold.TransitionSeconds = 0f;
            }

            var clock = 1000f;
            const float step = 1f / Fps;
            void Tick( float dt )
            {
                hold.BeginFrame();
                clock += dt;
                scene.EditorTick( clock, dt );
                hold.Apply( dt );
            }

            foreach ( var role in Roles )
            {
                cancel.ThrowIfCancellationRequested();
                var trigger = AnimationRoles.GraphTrigger( role );
                if ( role != AnimationRole.Idle && trigger is null )
                    continue;
                progress?.Report( $"Baking corrected {AnimationRoles.Label( role )}" );
                var seconds = role == AnimationRole.Idle ? 2f : ActionTiming.Seconds( setup, session.Analysis?.Asset, role );
                var frames = new List<XForm[]>();
                using ( scene.Push() )
                {
                    body.SceneModel?.ResetAnimParameters();
                    CharacterPoser.ApplyParameters( body, setup.EffectiveHoldType );
                    hold.Play( "idle" );
                    for ( var t = 0f; t < 2f; t += step )
                        Tick( step );
                    if ( trigger is not null )
                        body.Set( trigger, true );
                    hold.Play( role.ToString().ToLowerInvariant() );
                    var count = Math.Max( 2, (int)MathF.Ceiling( seconds * Fps ) + 1 );
                    for ( var f = 0; f < count; f++ )
                    {
                        if ( f > 0 )
                            Tick( step );
                        frames.Add( Sample( body, skeleton ) );
                    }
                }
                // Let the editor breathe between actions.
                await EditorThread.Delay( 1, cancel );

                var sequence = $"{role.ToString().ToLowerInvariant()}_corrected";
                var clip = new Clip( sequence, Fps, AnimationRoles.Loops( role ), frames );
                var file = $"{generated}/{name}_{sequence}.dmx";
                AssetCompiler.WriteText( file, AnimationDmxWriter.Write( skeleton, clip, DmxSpace.SourceZUpInches, sequence ) );
                result.Files.Add( file );
                vmdl.Animations.Add( new VmdlAnimation { Name = sequence, File = file, Looping = AnimationRoles.Loops( role ), FrameCount = clip.FrameCount, Fps = Fps } );
                result.Clips.Add( (role, sequence) );
            }
        }
        finally
        {
            scene.Destroy();
        }

        var modelPath = $"{folder}/{name}_corrected.vmdl";
        AssetCompiler.WriteText( modelPath, vmdl.Build() );
        result.Files.Add( modelPath );
        result.ModelPath = modelPath;
        return result;
    }

    /// <summary>The final (corrected) pose of the character as parent-relative locals.</summary>
    private static XForm[] Sample( SkinnedModelRenderer body, Skeleton skeleton )
    {
        var root = body.WorldTransform;
        var world = new XForm[skeleton.Count];
        var locals = new XForm[skeleton.Count];
        var bones = body.Model.Bones;
        for ( var i = 0; i < skeleton.Count; i++ )
        {
            var bone = bones.GetBone( skeleton[i].Name );
            world[i] = bone is not null && body.TryGetBoneTransform( bone, out var tx )
                ? root.ToLocal( tx ).ToCore()
                : (skeleton[i].ParentIndex >= 0 ? XForm.Compose( world[skeleton[i].ParentIndex], skeleton[i].RestLocal ) : skeleton[i].RestLocal);
            var parent = skeleton[i].ParentIndex;
            locals[i] = parent < 0 ? world[i] : XForm.ToLocal( world[parent], world[i] );
        }
        return locals;
    }
}
