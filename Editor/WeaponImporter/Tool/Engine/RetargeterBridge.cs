using System.Reflection;
using Editor;
using Sandbox;

namespace WeaponImporter.Tool;

/// <summary>
/// Optional hand-off to the Humanoid Retargeter library (installed separately) for turning
/// third-person animations from any rig into s&amp;box character sequences. Found by reflection so
/// the importer works without it.
/// </summary>
public static class RetargeterBridge
{
    private static Type _window;
    private static DateTime _checked;

    /// <summary>The retargeter's window type, or null when the library isn't installed.</summary>
    private static Type WindowType
    {
        get
        {
            if ( (DateTime.UtcNow - _checked).TotalSeconds < 5 )
                return _window;
            _checked = DateTime.UtcNow;
            _window = null;
            foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch ( ReflectionTypeLoadException e )
                {
                    types = e.Types.Where( t => t is not null ).ToArray();
                }
                catch ( Exception )
                {
                    continue;
                }
                _window = types.FirstOrDefault( t => t.Name == "RetargetWindow" && (t.Namespace ?? "").StartsWith( "HumanoidRetargeter", StringComparison.Ordinal ) );
                if ( _window is not null )
                    break;
            }
            return _window;
        }
    }

    public static bool IsInstalled => WindowType is not null;

    public const string InstallUrl = "https://github.com/zeljkovranjes/humanoid-retargeter";

    /// <summary>Opens the retargeter with the given animation files queued. Returns false when unavailable.</summary>
    public static bool Open( IEnumerable<string> files )
    {
        var type = WindowType;
        if ( type is null )
            return false;
        var open = type.GetMethod( "Open", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes );
        var window = open?.Invoke( null, null );
        if ( window is null )
            return false;
        var add = type.GetMethod( "AddFiles", BindingFlags.Public | BindingFlags.Instance );
        add?.Invoke( window, new object[] { files.ToList() } );
        return true;
    }

    /// <summary>The model the retargeted animations were written to, their sequences, or an error.</summary>
    public sealed record RetargetResult( string ModelPath, IReadOnlyList<string> Sequences, string Error );

    private static Type FindType( string fullName ) => AppDomain.CurrentDomain.GetAssemblies().Select( a =>
    {
        try { return a.GetType( fullName ); } catch { return null; }
    } ).FirstOrDefault( t => t is not null );

    /// <summary>
    /// Retargets animation files (any humanoid rig: FBX, BVH, glTF) onto the s&amp;box character
    /// with the Humanoid Retargeter and writes them, with a model holding their sequences, to
    /// <paramref name="folder"/> (project-relative). The model is verified to play: the first
    /// compile of a model based on the character in an editor session can drop its animation
    /// data, so it is compiled again when its sequences come out still.
    /// </summary>
    public static async Task<RetargetResult> RetargetAsync( IReadOnlyList<string> files, string folder, string modelName, IProgress<string> progress = null, CancellationToken cancel = default )
    {
        var requestType = FindType( "HumanoidRetargeter.RetargetRequest" );
        var retargeter = FindType( "HumanoidRetargeter.Retargeter" );
        var pipeline = FindType( "HumanoidRetargeter.Editor.EditorPipeline" );
        var optionsType = FindType( "HumanoidRetargeter.BatchOptions" );
        if ( requestType is null || retargeter is null || pipeline is null )
            return new RetargetResult( "", Array.Empty<string>(), "The Humanoid Retargeter is not installed." );
        try
        {
            progress?.Report( "Retargeting animations" );
            var requests = (System.Collections.IList)Activator.CreateInstance( typeof( List<> ).MakeGenericType( requestType ) );
            foreach ( var file in files )
            {
                var request = Activator.CreateInstance( requestType );
                requestType.GetProperty( "SourceData" ).SetValue( request, System.IO.File.ReadAllBytes( file ) );
                requestType.GetProperty( "SourceFileName" ).SetValue( request, System.IO.Path.GetFileName( file ) );
                requests.Add( request );
            }
            var target = pipeline.GetMethod( "LoadSboxDefaultTarget", BindingFlags.Public | BindingFlags.Static ).Invoke( null, null );
            var convert = retargeter.GetMethods( BindingFlags.Public | BindingFlags.Static ).First( m => m.Name == "ConvertBatch" );
            var parameters = convert.GetParameters();
            var args = new object[parameters.Length];
            args[0] = requests;
            args[1] = target;
            for ( var i = 2; i < parameters.Length; i++ )
                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            // The model names its clips by the folder they are written to.
            if ( optionsType is not null && parameters.Length > 2 && parameters[2].ParameterType == optionsType )
            {
                var options = Activator.CreateInstance( optionsType );
                optionsType.GetProperty( "DmxFolderRelative" )?.SetValue( options, folder );
                args[2] = options;
            }
            var batch = await Task.Run( () => convert.Invoke( null, args ), cancel );
            cancel.ThrowIfCancellationRequested();

            var sequences = new List<string>();
            var failures = new List<string>();
            foreach ( var clip in (System.Collections.IEnumerable)batch.GetType().GetProperty( "Clips" ).GetValue( batch ) )
            {
                var t = clip.GetType();
                if ( (bool)t.GetProperty( "Success" ).GetValue( clip ) )
                    sequences.Add( (string)t.GetProperty( "ClipName" ).GetValue( clip ) );
                else
                    failures.Add( $"{t.GetProperty( "ClipName" ).GetValue( clip )}: {t.GetProperty( "Error" ).GetValue( clip )}" );
            }
            if ( sequences.Count == 0 )
                return new RetargetResult( "", sequences, failures.Count > 0 ? string.Join( "; ", failures ) : "No animation could be retargeted." );

            progress?.Report( "Compiling retargeted animations" );
            await EditorThread.SwitchToMainThread();
            // The character model the clips build on must be loaded before they compile.
            if ( target?.GetType().GetProperty( "BaseModelPath" )?.GetValue( target ) is string baseModel && baseModel.Length > 0 )
                Model.Load( baseModel );
            var write = pipeline.GetMethod( "WriteAndCompileAsync", BindingFlags.Public | BindingFlags.Static );
            var wp = write.GetParameters();
            var writeArgs = new object[wp.Length];
            writeArgs[0] = batch;
            writeArgs[1] = folder;
            for ( var i = 2; i < wp.Length; i++ )
                writeArgs[i] = wp[i].Name == "standaloneVmdlName" ? modelName : wp[i].HasDefaultValue ? wp[i].DefaultValue : null;
            var task = (Task)write.Invoke( null, writeArgs );
            await task;
            await EditorThread.SwitchToMainThread();
            var written = task.GetType().GetProperty( "Result" ).GetValue( task );
            var modelPath = $"{folder}/{modelName}.vmdl";
            if ( written.GetType().GetProperty( "Compiled" )?.GetValue( written ) is false )
            {
                var errors = written.GetType().GetProperty( "Errors" )?.GetValue( written ) as System.Collections.IEnumerable;
                return new RetargetResult( "", sequences, $"The retargeted model did not compile: {string.Join( "; ", errors?.Cast<object>() ?? Array.Empty<object>() )}" );
            }
            if ( !await PlaysAsync( modelPath, sequences ) )
            {
                await AssetCompiler.CompileAsync( modelPath, cancel: cancel );
                if ( !await PlaysAsync( modelPath, sequences ) )
                    return new RetargetResult( modelPath, sequences, "The retargeted animations compiled without motion." );
            }
            return new RetargetResult( modelPath, sequences, failures.Count > 0 ? string.Join( "; ", failures ) : null );
        }
        catch ( TargetInvocationException e )
        {
            return new RetargetResult( "", Array.Empty<string>(), e.InnerException?.Message ?? e.Message );
        }
    }

    /// <summary>
    /// Whether the model's sequences really pose the character: a compile that dropped the
    /// animation data leaves every sequence in the bind pose.
    /// </summary>
    private static async Task<bool> PlaysAsync( string modelPath, IReadOnlyList<string> sequences )
    {
        var model = await AssetCompiler.LoadModelAsync( modelPath, sequences );
        if ( model is null || model.IsError )
            return false;
        var world = new SceneWorld();
        var so = new SceneModel( world, model, global::Transform.Zero ) { UseAnimGraph = false };
        try
        {
            var hand = model.Bones.GetBone( "hand_R" ) ?? model.Bones.AllBones.LastOrDefault();
            if ( hand is null )
                return true;
            foreach ( var sequence in sequences.Where( model.AnimationNames.Contains ) )
            {
                so.CurrentSequence.Name = sequence;
                var positions = new List<Vector3>();
                for ( var i = 0; i <= 8; i++ )
                {
                    so.CurrentSequence.TimeNormalized = i / 8f;
                    so.Update( 0.001f );
                    positions.Add( so.GetBoneWorldTransform( hand.Index ).Position );
                }
                var bind = model.GetBoneTransform( hand.Index ).Position;
                if ( positions.Max( p => p.Distance( positions[0] ) ) > 0.05f || positions.Any( p => p.Distance( bind ) > 0.5f ) )
                    return true;
            }
            return false;
        }
        finally
        {
            so.Delete();
            world.Delete();
        }
    }
}
