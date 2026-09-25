using System.Reflection;

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
}
