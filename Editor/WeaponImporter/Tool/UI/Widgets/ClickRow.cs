using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// A row that is itself the control: click anywhere on it. Same look as the animation rows
/// (hover tint, selected outline with a green bar).
/// </summary>
public sealed class ClickRow : Widget
{
	private bool _selected;

	public Action Clicked { get; set; }

	public ClickRow( Widget parent, Action clicked = null, string tooltip = null ) : base( parent )
	{
		Clicked = clicked;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 8, 3, 4, 3 );
		Layout.Spacing = UiStyle.RowSpacing;
		MouseTracking = true;
		Cursor = CursorShape.Finger;
		ToolTip = tooltip;
	}

	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value )
				return;
			_selected = value;
			Update();
		}
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || Clicked is null )
			return;
		Clicked();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		if ( _selected )
		{
			Paint.SetPen( Theme.Green.WithAlpha( .6f ), 1 );
			Paint.SetBrush( Theme.Green.WithAlpha( .08f ) );
			Paint.DrawRect( LocalRect.Shrink( .5f ), 4 );
			Paint.ClearPen();
			Paint.SetBrush( Theme.Green );
			Paint.DrawRect( new Rect( 0, 6, 3, Height - 12 ), 1.5f );
		}
		else if ( Paint.HasMouseOver && Clicked is not null )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( .3f ) );
			Paint.DrawRect( LocalRect, 4 );
		}
	}
}
