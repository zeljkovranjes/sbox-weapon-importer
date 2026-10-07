using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

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
