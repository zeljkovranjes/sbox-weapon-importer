using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.Engine;

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
