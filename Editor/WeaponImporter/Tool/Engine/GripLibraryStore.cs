using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Import;

namespace WeaponImporter.Tool;

/// <summary>
/// Grip presets available to the importer: the built-in library (captured from open animation
/// packs), the project's own library (<c>Assets/weapons/grip_library.json</c>, grips extracted
/// from files the user added), and the grips of the weapon file's own first-person arms.
/// </summary>
public static class GripLibraryStore
{
    public const string ProjectFile = "weapons/grip_library.json";

    private static List<GripPreset> _project;

    public static IReadOnlyList<GripPreset> BuiltIn { get; } = LoadBuiltIn();

    /// <summary>Presets saved in this project (read once, then kept up to date by <see cref="AddAsync"/>).</summary>
    public static IReadOnlyList<GripPreset> Project => _project ??= LoadProject();

    public static List<GripPreset> All( IEnumerable<GripPreset> own ) => own.Concat( Project ).Concat( BuiltIn )
        .GroupBy( p => p.Name ).Select( g => g.First() ).ToList();

    private static List<GripPreset> LoadBuiltIn()
    {
        try
        {
            return GripLibrary.FromJson( BuiltInGrips.Json );
        }
        catch ( Exception e )
        {
            Log.Warning( $"[weapon importer] built-in grips could not be read: {e.Message}" );
            return new List<GripPreset>();
        }
    }

    private static List<GripPreset> LoadProject()
    {
        try
        {
            var abs = AssetCompiler.Absolute( ProjectFile );
            return System.IO.File.Exists( abs ) ? GripLibrary.FromJson( System.IO.File.ReadAllText( abs ) ) : new List<GripPreset>();
        }
        catch ( Exception e )
        {
            Log.Warning( $"[weapon importer] {ProjectFile} could not be read: {e.Message}" );
            return new List<GripPreset>();
        }
    }

    /// <summary>
    /// Extracts the grips of every file with arms holding a weapon (FBX, GLB, glTF) and adds them
    /// to the project library. Heavy: runs off the main thread, cancellable. Returns how many
    /// grips were added and the files that had none.
    /// </summary>
    public static async Task<(int Added, List<string> Skipped)> AddAsync( IEnumerable<string> files, IProgress<string> progress, CancellationToken cancel )
    {
        var list = files.ToList();
        var found = new List<GripPreset>();
        var skipped = new List<string>();
        for ( var i = 0; i < list.Count; i++ )
        {
            cancel.ThrowIfCancellationRequested();
            var file = list[i];
            progress?.Report( $"Reading grips {i + 1}/{list.Count}: {System.IO.Path.GetFileName( file )}" );
            try
            {
                var presets = await Task.Run( () =>
                {
                    var analysis = WeaponAnalyzer.Analyze( WeaponLoader.Load( file ), new AnalyzeOptions(), cancel );
                    return GripExtractor.Extract( analysis, System.IO.Path.GetFileNameWithoutExtension( file ) );
                }, cancel );
                if ( presets.Count == 0 )
                    skipped.Add( file );
                found.AddRange( presets );
            }
            catch ( OperationCanceledException )
            {
                throw;
            }
            catch ( Exception e )
            {
                Log.Warning( $"[weapon importer] no grips from {file}: {e.Message}" );
                skipped.Add( file );
            }
        }
        await EditorThread.SwitchToMainThread();
        var project = Project.ToList();
        var added = 0;
        foreach ( var p in found )
        {
            project.RemoveAll( x => x.Name == p.Name );
            project.Add( p );
            added++;
        }
        _project = project;
        AssetCompiler.WriteText( ProjectFile, GripLibrary.ToJson( project ) );
        return (added, skipped);
    }

    /// <summary>Removes a grip from the project library.</summary>
    public static void Remove( string name )
    {
        var project = Project.ToList();
        if ( project.RemoveAll( p => p.Name == name ) == 0 )
            return;
        _project = project;
        AssetCompiler.WriteText( ProjectFile, GripLibrary.ToJson( project ) );
    }
}
