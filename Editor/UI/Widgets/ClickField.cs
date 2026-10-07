using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

/// <summary>
/// A read-out that opens a menu when clicked, styled like the framed inputs: current value on the
/// left, a small chevron on the right. Used where a full dropdown would be heavier than needed.
/// </summary>
public sealed class ClickField : Widget
{
	private string _text;
	private readonly string _icon;
	private Color _iconColor;
	private bool _muted;

	public Action Clicked { get; set; }

	public ClickField( Widget parent, string text, string icon, Color iconColor, Action clicked, string tooltip ) : base( parent )
	{
		_text = text ?? "";
		_icon = icon;
		_iconColor = iconColor;
		Clicked = clicked;
		ToolTip = tooltip;
		FixedHeight = UiStyle.ControlHeight;
		MinimumWidth = 40;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
	}

	/// <summary>Muted text for "nothing set" values.</summary>
	public bool Muted
	{
		get => _muted;
		set
		{
			_muted = value;
			Update();
		}
	}

	public void Set( string text, Color iconColor, bool muted )
	{
		_text = text ?? "";
		_iconColor = iconColor;
		_muted = muted;
		Update();
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || !Enabled )
			return;
		e.Accepted = true;
		Clicked?.Invoke();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var hover = Paint.HasMouseOver && Enabled;
		Paint.SetPen( hover ? Color.Lerp( UiStyle.ButtonEdge, Color.White, .1f ) : UiStyle.InputEdge, 1 );
		Paint.SetBrush( hover ? Color.Lerp( Theme.WindowBackground, Color.White, .04f ) : Theme.WindowBackground );
		Paint.DrawRect( LocalRect.Shrink( .5f ), UiStyle.Radius );
		var left = 8f;
		if ( !string.IsNullOrEmpty( _icon ) )
		{
			Paint.SetPen( _iconColor );
			Paint.DrawIcon( new Rect( 6, 0, 16, Height ), _icon, 14, TextFlag.Center );
			left = 28f;
		}
		Paint.SetPen( Theme.TextLight );
		Paint.DrawIcon( new Rect( Width - 22, 0, 16, Height ), "expand_more", 14, TextFlag.Center );
		Paint.SetDefaultFont( 8 );
		Paint.SetPen( _muted ? Theme.TextLight : Theme.Text );
		var width = Width - left - 26;
		Paint.DrawText( new Rect( left, 0, width, Height ), Paint.GetElidedText( _text, width, ElideMode.Right, TextFlag.LeftCenter ), TextFlag.LeftCenter );
	}
}
