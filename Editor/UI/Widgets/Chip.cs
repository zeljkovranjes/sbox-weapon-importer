using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

/// <summary>A clickable rounded chip: quick animation switches and overlay toggles.</summary>
public sealed class Chip : Widget
{
	private string _text;
	private readonly string _icon;
	private bool _active;

	public Action Clicked { get; set; }

	public Chip( Widget parent, string text, string icon = null, string tooltip = null ) : base( parent )
	{
		_text = text;
		_icon = icon;
		ToolTip = tooltip;
		FixedHeight = 22;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		Measure();
	}

	public string Text
	{
		get => _text;
		set
		{
			_text = value;
			Measure();
			Update();
		}
	}

	public bool Active
	{
		get => _active;
		set
		{
			if ( _active == value )
				return;
			_active = value;
			Update();
		}
	}

	private void Measure() => FixedWidth = 7.4f * _text.Length + (_icon is null ? 20 : 38);

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !Enabled )
			return;
		Clicked?.Invoke();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var accent = Theme.Green;
		var fill = _active ? accent.WithAlpha( .22f ) : Paint.HasMouseOver && Enabled ? Theme.ControlBackground.Lighten( .5f ) : Theme.WindowBackground;
		var edge = _active ? accent.WithAlpha( .8f ) : Theme.ControlBackground.Lighten( .55f );
		Paint.SetPen( edge, 1 );
		Paint.SetBrush( fill );
		var r = LocalRect.Shrink( .5f );
		Paint.DrawRect( r, r.Height * .5f );
		var color = !Enabled ? Theme.TextDisabled : _active ? accent : Theme.Text;
		Paint.SetPen( color );
		var textRect = r;
		if ( _icon is not null )
		{
			Paint.DrawIcon( new Rect( r.Left + 8, r.Top, 14, r.Height ), _icon, 13 );
			textRect = new Rect( r.Left + 24, r.Top, r.Width - 30, r.Height );
		}
		Paint.SetDefaultFont( 8, _active ? 600 : 400 );
		Paint.DrawText( textRect, _text, TextFlag.Center );
	}
}
