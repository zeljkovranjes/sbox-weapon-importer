using System.IO.Compression;
using Editor;
using Sandbox;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool;

/// <summary>
/// The optional stock third-person animations (item use, throws, dual pistols, melee stances
/// and attacks, fists, hold poses), retargeted onto the s&amp;box human. They are not part of
/// the library: the Download button fetches them from the GitHub release as a zip holding the
/// compiled model and unpacks it into the project's Assets, where the importer finds it.
/// </summary>
public static class StockAnimations
{
    public const string Version = "1";
    public const string Url = "https://github.com/zeljkovranjes/sbox-weapon-importer/releases/download/stock-animations-v" + Version + "/weapon_importer_stock_animations.zip";

    /// <summary>Project-relative folder the zip unpacks into.</summary>
    public const string Folder = "weapon_importer/stock";

    /// <summary>The model holding every stock sequence.</summary>
    public const string ModelPath = StockThirdPerson.Model;

    private static IReadOnlyList<string> _sequences;
    private static string _sequencesFor;

    /// <summary>Whether the stock animations are in this project (the compiled model, or its source).</summary>
    public static bool Installed
    {
        get
        {
            var root = AssetCompiler.AssetsRoot;
            if ( root.Length == 0 )
                return false;
            var path = AssetCompiler.Absolute( ModelPath );
            return System.IO.File.Exists( path + "_c" ) || System.IO.File.Exists( path );
        }
    }

    /// <summary>The stock model's sequences, or none when not installed.</summary>
    public static IReadOnlyList<string> Sequences
    {
        get
        {
            if ( !Installed )
                return Array.Empty<string>();
            var root = AssetCompiler.AssetsRoot;
            if ( _sequences is { Count: > 0 } && _sequencesFor == root )
                return _sequences;
            var model = Model.Load( ModelPath );
            if ( model is null || model.IsError )
                return Array.Empty<string>();
            _sequences = model.AnimationNames.Where( n => n.StartsWith( "wi_", StringComparison.Ordinal ) ).ToList();
            _sequencesFor = root;
            return _sequences;
        }
    }

    /// <summary>Downloads the zip from the release and unpacks it into Assets. Returns an error, or null.</summary>
    public static async Task<string> InstallAsync( IProgress<string> progress = null, CancellationToken cancel = default )
    {
        var root = AssetCompiler.AssetsRoot;
        if ( root.Length == 0 )
            return "No project is open.";
        progress?.Report( "Downloading stock animations" );
        byte[] bytes;
        try
        {
            bytes = await Http.RequestBytesAsync( Url, cancellationToken: cancel );
        }
        catch ( Exception e ) when ( e is not OperationCanceledException )
        {
            return $"Download failed: {e.Message}";
        }
        cancel.ThrowIfCancellationRequested();
        progress?.Report( "Unpacking stock animations" );
        var written = new List<string>();
        try
        {
            using var zip = new ZipArchive( new System.IO.MemoryStream( bytes ), ZipArchiveMode.Read );
            var rootFull = System.IO.Path.GetFullPath( root );
            var allowed = System.IO.Path.GetFullPath( System.IO.Path.Combine( rootFull, Folder ) ) + System.IO.Path.DirectorySeparatorChar;
            foreach ( var entry in zip.Entries )
            {
                if ( entry.FullName.EndsWith( "/" ) )
                    continue;
                // Only inside Assets/weapon_importer/stock: a zip can't write anywhere else.
                var target = System.IO.Path.GetFullPath( System.IO.Path.Combine( rootFull, entry.FullName ) );
                if ( !target.StartsWith( allowed, StringComparison.OrdinalIgnoreCase ) )
                    continue;
                System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( target ) );
                using var input = entry.Open();
                using var output = System.IO.File.Create( target );
                await input.CopyToAsync( output, cancel );
                written.Add( target );
            }
        }
        catch ( Exception e ) when ( e is System.IO.InvalidDataException or System.IO.IOException )
        {
            return $"The download is not a valid stock animation zip: {e.Message}";
        }
        var compiled = AssetCompiler.Absolute( ModelPath ) + "_c";
        if ( !written.Any( w => string.Equals( w, compiled, StringComparison.OrdinalIgnoreCase ) ) )
            return "The zip holds no stock model.";

        await EditorThread.SwitchToMainThread();
        foreach ( var file in written )
            AssetSystem.RegisterFile( file );
        _sequences = null;
        // The model is ready once it loads with its sequences.
        var model = await AssetCompiler.LoadModelAsync( ModelPath, new[] { "wi_hold_object" } );
        if ( model is null || model.IsError || Sequences.Count == 0 )
            return "The stock animations were unpacked but the model did not load.";
        progress?.Report( $"Stock animations installed ({Sequences.Count} animations)" );
        return null;
    }
}
