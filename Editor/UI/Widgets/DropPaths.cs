using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;
using WeaponImporter.EditorTools.Engine;

/// <summary>What can be dropped on the importer: weapon models and saved setups.</summary>
public static class DropPaths
{
	public static bool IsWeaponFile( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;
		if ( IsSetupFile( path ) )
			return true;
		return System.IO.Path.GetExtension( path ).ToLowerInvariant() is ".fbx" or ".glb" or ".gltf" or ".vmdl";
	}

	public static bool IsSetupFile( string path ) => path?.EndsWith( ".weapon.json", StringComparison.OrdinalIgnoreCase ) ?? false;

	/// <summary>The first supported absolute path in a drag, or null.</summary>
	public static string From( DragData data )
	{
		if ( data is null )
			return null;
		try
		{
			if ( data.Files is { Length: > 0 } files )
			{
				var file = files.FirstOrDefault( IsWeaponFile );
				if ( file is not null )
					return file;
			}
			if ( data.HasFileOrFolder && IsWeaponFile( data.FileOrFolder ) )
				return data.FileOrFolder;
			if ( data.Assets is { Count: > 0 } assets )
			{
				foreach ( var a in assets )
				{
					var p = a?.AssetPath;
					if ( !IsWeaponFile( p ) )
						continue;
					var asset = AssetSystem.FindByPath( p );
					return asset?.AbsolutePath ?? (System.IO.Path.IsPathRooted( p ) ? p : AssetCompiler.Absolute( p ));
				}
			}
		}
		catch ( Exception )
		{
			// A drag with nothing we understand.
		}
		return null;
	}
}
