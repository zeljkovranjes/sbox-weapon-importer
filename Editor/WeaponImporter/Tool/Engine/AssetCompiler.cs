using Editor;
using Sandbox;

namespace WeaponImporter.Tool;

/// <summary>Outcome of compiling one generated file.</summary>
public sealed record CompileResult( string Path, bool Success, IReadOnlyList<string> Errors )
{
    public static CompileResult Failed( string path, string error ) => new( path, false, new[] { error } );
}

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

/// <summary>Reads the editor log file (the only place compile errors show up).</summary>
public static class EngineLog
{
    public static string LogPath
    {
        get
        {
            foreach ( var dir in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory, System.IO.Path.GetDirectoryName( Environment.ProcessPath ?? "" ) ?? "" } )
            {
                var candidate = System.IO.Path.Combine( dir, "logs", "sbox-dev.log" );
                if ( System.IO.File.Exists( candidate ) )
                    return candidate;
                var parent = System.IO.Path.GetDirectoryName( dir.TrimEnd( '\\', '/' ) );
                if ( parent is not null && System.IO.File.Exists( System.IO.Path.Combine( parent, "logs", "sbox-dev.log" ) ) )
                    return System.IO.Path.Combine( parent, "logs", "sbox-dev.log" );
            }
            return "";
        }
    }

    public static long Length()
    {
        try
        {
            var path = LogPath;
            return path.Length > 0 ? new System.IO.FileInfo( path ).Length : 0;
        }
        catch ( Exception )
        {
            return 0;
        }
    }

    public static List<string> Since( long offset )
    {
        var lines = new List<string>();
        try
        {
            var path = LogPath;
            if ( path.Length == 0 )
                return lines;
            using var fs = new System.IO.FileStream( path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete );
            if ( offset > fs.Length )
                offset = 0;
            fs.Seek( offset, System.IO.SeekOrigin.Begin );
            using var reader = new System.IO.StreamReader( fs );
            string line;
            while ( (line = reader.ReadLine()) is not null )
                lines.Add( line );
        }
        catch ( Exception )
        {
        }
        return lines;
    }

    /// <summary>Error-looking lines that mention the file, ModelDoc or the resource compiler (last 12).</summary>
    public static List<string> ErrorsFor( IEnumerable<string> lines, string fileName )
    {
        return lines
            .Where( l => (l.Contains( "error", StringComparison.OrdinalIgnoreCase ) || l.Contains( "failed", StringComparison.OrdinalIgnoreCase ) || l.Contains( "missing", StringComparison.OrdinalIgnoreCase ) || l.Contains( "warning", StringComparison.OrdinalIgnoreCase ))
                && (l.Contains( fileName, StringComparison.OrdinalIgnoreCase ) || l.Contains( "resourcecompiler", StringComparison.OrdinalIgnoreCase ) || l.Contains( "ModelDoc", StringComparison.OrdinalIgnoreCase ) || l.Contains( "dmx", StringComparison.OrdinalIgnoreCase )) )
            .Select( l => l.Trim() )
            .TakeLast( 12 )
            .ToList();
    }
}
