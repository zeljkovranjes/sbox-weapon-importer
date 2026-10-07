using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.Engine;

/// <summary>
/// Registers and compiles generated assets. The asset system exposes no compile-error API, so
/// errors are read from the slice of the editor log written while compiling.
/// </summary>
public static class AssetCompiler
{
    /// <summary>Absolute path inside the current project's assets folder.</summary>
    public static string AssetsRoot => Project.Current?.GetAssetsPath() ?? "";

    public static string Absolute( string relative ) => System.IO.Path.GetFullPath( System.IO.Path.Combine( AssetsRoot, relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) ) );

    /// <summary>The file being imported: writing over it (or its compiled form) is refused.</summary>
    public static string Protected { get; set; }

    private static void Guard( string abs )
    {
        if ( Protected is { Length: > 0 } source && (string.Equals( abs, source, StringComparison.OrdinalIgnoreCase ) || string.Equals( abs, source + "_c", StringComparison.OrdinalIgnoreCase )) )
            throw new InvalidOperationException( $"Refusing to overwrite the imported file {source}." );
    }

    /// <summary>Writes a text file (only when it changed) and returns whether it was rewritten.</summary>
    public static bool WriteText( string relative, string text )
    {
        var abs = Absolute( relative );
        Guard( abs );
        System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( abs )! );
        if ( System.IO.File.Exists( abs ) && System.IO.File.ReadAllText( abs ) == text )
            return false;
        System.IO.File.WriteAllText( abs, text );
        return true;
    }

    public static bool WriteBytes( string relative, byte[] bytes )
    {
        var abs = Absolute( relative );
        Guard( abs );
        System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( abs )! );
        if ( System.IO.File.Exists( abs ) && new System.IO.FileInfo( abs ).Length == bytes.Length && System.IO.File.ReadAllBytes( abs ).AsSpan().SequenceEqual( bytes ) )
            return false;
        System.IO.File.WriteAllBytes( abs, bytes );
        return true;
    }

    /// <summary>Registers every file with the asset system (main thread).</summary>
    public static async Task RegisterAsync( IEnumerable<string> relativePaths )
    {
        await EditorThread.SwitchToMainThread();
        foreach ( var rel in relativePaths )
        {
            var abs = Absolute( rel );
            if ( System.IO.File.Exists( abs ) )
                AssetSystem.RegisterFile( abs );
        }
    }

    /// <summary>
    /// Compiles an asset and waits for a fresh compiled file. Freshly written inputs need a
    /// moment of quiet or the engine abandons the compile, so it retries on that.
    /// </summary>
    public static async Task<CompileResult> CompileAsync( string relative, float timeoutSeconds = 180f, CancellationToken cancel = default )
    {
        await EditorThread.SwitchToMainThread();
        var abs = Absolute( relative );
        if ( !System.IO.File.Exists( abs ) )
            return CompileResult.Failed( relative, "file does not exist" );

        for ( var attempt = 0; attempt < 3; attempt++ )
        {
            var logStart = EngineLog.Length();
            var asset = AssetSystem.FindByPath( abs ) ?? AssetSystem.RegisterFile( abs );
            if ( asset is null )
                return CompileResult.Failed( relative, "the asset system did not accept the file" );

            var compiled = asset.GetCompiledFile( true ) ?? abs + "_c";
            var before = System.IO.File.Exists( compiled ) ? System.IO.File.GetLastWriteTimeUtc( compiled ) : DateTime.MinValue;
            var started = DateTime.UtcNow;
            asset.Compile( true );

            while ( (DateTime.UtcNow - started).TotalSeconds < timeoutSeconds )
            {
                cancel.ThrowIfCancellationRequested();
                await EditorThread.Delay( 200, cancel );
                if ( asset.IsCompileFailed )
                    break;
                compiled = asset.GetCompiledFile( true ) ?? compiled;
                if ( System.IO.File.Exists( compiled ) && System.IO.File.GetLastWriteTimeUtc( compiled ) > before )
                    return new CompileResult( relative, true, Array.Empty<string>() );
            }

            var log = EngineLog.Since( logStart );
            var name = System.IO.Path.GetFileName( relative );
            if ( log.Any( l => l.Contains( "abandoning", StringComparison.OrdinalIgnoreCase ) && l.Contains( name, StringComparison.OrdinalIgnoreCase ) ) )
            {
                await EditorThread.Delay( 2000, cancel );
                continue;
            }
            var errors = EngineLog.ErrorsFor( log, name );
            return new CompileResult( relative, false, errors.Count > 0 ? errors : new[] { asset.IsCompileFailed ? "compile failed (no details in the log)" : "compile timed out" } );
        }
        return CompileResult.Failed( relative, "compile was abandoned repeatedly" );
    }

    /// <summary>Loads a model after compiling, polling until it is valid and has the expected sequences.</summary>
    public static async Task<Model> LoadModelAsync( string relative, IReadOnlyCollection<string> expectSequences = null, float timeoutSeconds = 20f )
    {
        await EditorThread.SwitchToMainThread();
        var started = DateTime.UtcNow;
        Model model = null;
        while ( (DateTime.UtcNow - started).TotalSeconds < timeoutSeconds )
        {
            model = Model.Load( relative );
            if ( model is not null && !model.IsError )
            {
                if ( expectSequences is null || expectSequences.All( s => model.AnimationNames.Contains( s ) ) )
                    return model;
            }
            await EditorThread.Delay( 250 );
        }
        return model;
    }
}
