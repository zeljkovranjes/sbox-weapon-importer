using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// First-load surface: a dark-gray rounded box with a centered prompt, a green choose button and
/// a template button. Accepts files from the OS and assets from the asset browser.
/// </summary>
public sealed class DropArea : Widget
{
	private readonly Action<string> _import;
	private int _hover;

	public DropArea( Widget parent, Action<string> import, Action chooseFile, Action chooseFromDisk, Action useTemplate ) : base( parent )
	{
		_import = import;
		AcceptDrops = true;
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;
		Layout.AddStretchCell();
		var row = Layout.AddRow();
		row.AddStretchCell();
		var center = row.AddColumn();
		center.Spacing = 12;
		center.Add( new BigIcon( this ) );
		center.Add( new Label.Subtitle( "Drop a weapon", this ) { Alignment = TextFlag.Center } );
		center.Add( UiStyle.Muted( new Label( "FBX, GLB, glTF or VMDL", this ) { Alignment = TextFlag.Center } ) );
		center.Add( UiStyle.Muted( new Label( "It is analyzed and set up automatically: type, grips, animations and attachments.", this ) { Alignment = TextFlag.Center, WordWrap = true, MaximumWidth = 420 }, small: true ) );
		center.AddSpacingCell( 4 );
		var choice = center.AddRow();
		choice.Spacing = 8;
		choice.AddStretchCell();
		choice.Add( new Button.Primary( "Choose File" ) { Icon = "folder_open", Tint = Theme.Green, MinimumWidth = 140, FixedHeight = 32, Clicked = chooseFile, ToolTip = "Pick a weapon model from the asset browser (.fbx, .glb, .gltf, .vmdl)" } );
		choice.Add( new UiButton( this, "From disk…", "file_open", chooseFromDisk, "Pick a weapon file outside the project", 32 ) );
		choice.Add( new UiButton( this, "Use a template…", "content_copy", useTemplate, "Start from a weapon you already set up: its grips, animation mapping, events and attachments are adapted to the new model", 32 ) );
		choice.AddStretchCell();
		row.AddStretchCell();
		Layout.AddStretchCell();
	}

	public override void OnDragHover( DragEvent e )
	{
		var path = DropPaths.From( e.Data );
		var valid = path is not null;
		_hover = valid ? 1 : -1;
		if ( valid )
			e.Action = DropAction.Link;
		Update();
	}

	public override void OnDragDrop( DragEvent e )
	{
		_hover = 0;
		var path = DropPaths.From( e.Data );
		if ( path is not null )
		{
			e.Action = DropAction.Link;
			_import( path );
		}
		Update();
	}

	public override void OnDragLeave()
	{
		_hover = 0;
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( _hover == 1 ? Theme.Green : _hover < 0 ? Theme.Red : Theme.ControlBackground.Lighten( .2f ), _hover == 0 ? 1 : 2 );
		Paint.SetBrush( _hover == 1 ? Theme.Green.WithAlpha( .06f ) : _hover < 0 ? Theme.Red.WithAlpha( .05f ) : Paint.HasMouseOver ? Theme.ControlBackground.Lighten( .3f ) : Theme.ControlBackground );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
	}

	private sealed class BigIcon : Widget
	{
		public BigIcon( Widget parent ) : base( parent )
		{
			FixedHeight = 56;
		}

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.TextLight );
			Paint.DrawIcon( new Rect( (Width - 48) * .5f, 4, 48, 48 ), "back_hand", 48 );
		}
	}
}

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
