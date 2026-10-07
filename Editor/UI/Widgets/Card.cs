using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

/// <summary>
/// A rounded dark-gray panel on the gray window. Its column layout holds an optional header
/// row (icon, title, then controls) above the content.
/// </summary>
public class Card : Widget
{
	public Card( Widget parent, bool row = false ) : base( parent )
	{
		Layout = row ? Layout.Row() : Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;
	}

	/// <summary>Adds the header row: an icon and a title, then whatever the caller adds to the returned row.</summary>
	public Layout Header( string icon, string title, string tooltip = null )
	{
		var row = Layout.AddRow();
		row.Spacing = 6;
		row.Add( new CardIcon( this, icon ) );
		var label = row.Add( new Label( title, this ) { ToolTip = tooltip } );
		label.SetStyles( "font-weight: 600;" );
		return row;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.ControlBackground.Lighten( .25f ), 1 );
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
	}

	private sealed class CardIcon : Widget
	{
		private readonly string _icon;

		public CardIcon( Widget parent, string icon ) : base( parent )
		{
			_icon = icon;
			FixedSize = 18;
		}

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( LocalRect, _icon, 16 );
		}
	}
}
