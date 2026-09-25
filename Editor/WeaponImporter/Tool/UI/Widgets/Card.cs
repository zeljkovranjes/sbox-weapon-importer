using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

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

/// <summary>A small round light before the status text: amber while working, green when ready, red on errors.</summary>
public sealed class StatusDot : Widget
{
	private Color _color = Theme.TextLight;

	public StatusDot( Widget parent ) : base( parent )
	{
		FixedSize = 10;
	}

	public Color Color
	{
		get => _color;
		set
		{
			if ( _color == value )
				return;
			_color = value;
			Update();
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( _color );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );
	}
}

/// <summary>Small uppercase caption with a hairline, separating groups inside a card.</summary>
public sealed class SectionHeader : Widget
{
	private readonly string _text;

	public SectionHeader( Widget parent, string text ) : base( parent )
	{
		_text = text.ToUpperInvariant();
		FixedHeight = 18;
	}

	protected override void OnPaint()
	{
		Paint.SetDefaultFont( 7, 600 );
		Paint.SetPen( Theme.TextLight );
		var size = Paint.MeasureText( _text );
		Paint.DrawText( new Rect( 0, 0, size.x + 2, Height ), _text, TextFlag.LeftCenter );
		Paint.SetPen( Theme.ControlBackground.Lighten( .45f ), 1 );
		Paint.DrawLine( new Vector2( size.x + 10, Height * .5f ), new Vector2( Width, Height * .5f ) );
	}
}

/// <summary>Small muted uppercase column caption (list headers).</summary>
public sealed class SectionCaption : Widget
{
	private readonly string _text;
	private readonly TextFlag _align;

	public SectionCaption( Widget parent, string text, float width, TextFlag align = TextFlag.LeftCenter ) : base( parent )
	{
		_text = text.ToUpperInvariant();
		_align = align;
		FixedHeight = 16;
		if ( width > 0 )
			FixedWidth = width;
	}

	protected override void OnPaint()
	{
		Paint.SetDefaultFont( 7, 600 );
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( LocalRect, _text, _align );
	}
}
