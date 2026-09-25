using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// The importer's secondary button: dark fill a step lighter than the card, a hairline border,
/// light text and a hover lighten, with the inputs' corner radius. Every non-primary button uses
/// it so they all look the same. Optional toggle state shows as a green tint.
/// </summary>
public sealed class UiButton : Widget
{
	private string _text;
	private string _icon;
	private bool _checked;

	public Action Clicked { get; set; }

	/// <summary>When set, the button shows as pressed (green tint).</summary>
	public bool IsChecked
	{
		get => _checked;
		set
		{
			if ( _checked == value )
				return;
			_checked = value;
			Update();
		}
	}

	public string Text
	{
		get => _text;
		set
		{
			_text = value ?? "";
			Measure();
			Update();
		}
	}

	public string Icon
	{
		get => _icon;
		set
		{
			_icon = value;
			Measure();
			Update();
		}
	}

	public UiButton( Widget parent, string text, string icon = null, Action clicked = null, string tooltip = null, float height = UiStyle.ControlHeight ) : base( parent )
	{
		_text = text ?? "";
		_icon = icon;
		Clicked = clicked;
		ToolTip = tooltip;
		FixedHeight = height;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		FocusMode = FocusMode.None;
		Measure();
	}

	private const float PadX = 11f;
	private const float IconSize = 16f;
	private const float IconGap = 6f;

	/// <summary>Estimated width until the first paint measures the real text.</summary>
	private void Measure()
	{
		FixedWidth = WidthFor( _text.Length == 0 ? 0 : 6.2f * _text.Length );
	}

	private float WidthFor( float textWidth )
	{
		if ( _text.Length == 0 )
			return string.IsNullOrEmpty( _icon ) ? PadX * 2 : UiStyle.ControlHeight;
		var icon = string.IsNullOrEmpty( _icon ) ? 0 : IconSize + IconGap;
		return MathF.Ceiling( PadX * 2 + icon + textWidth );
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( !Enabled || !e.LeftMouseButton || !LocalRect.IsInside( e.LocalPosition ) )
			return;
		Clicked?.Invoke();
		e.Accepted = true;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
			e.Accepted = true;
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var hover = Paint.HasMouseOver && Enabled;
		var fill = _checked ? Theme.Green.WithAlpha( .18f ) : hover ? Color.Lerp( UiStyle.ButtonFill, Color.White, .06f ) : UiStyle.ButtonFill;
		var edge = _checked ? Theme.Green.WithAlpha( .75f ) : hover ? Color.Lerp( UiStyle.ButtonEdge, Color.White, .1f ) : UiStyle.ButtonEdge;
		Paint.SetPen( edge, 1 );
		Paint.SetBrush( fill );
		Paint.DrawRect( LocalRect.Shrink( .5f ), UiStyle.Radius );

		var color = !Enabled ? Theme.TextDisabled : _checked ? Theme.Green : Theme.Text;
		Paint.SetPen( color );
		if ( _text.Length == 0 )
		{
			if ( !string.IsNullOrEmpty( _icon ) )
				Paint.DrawIcon( LocalRect, _icon, 15, TextFlag.Center );
			return;
		}

		// Fit the width to the measured text so the padding is the same on both sides, and
		// centre icon + text as one group.
		Paint.SetDefaultFont( 8 );
		var textWidth = Paint.MeasureText( _text ).x;
		var wanted = WidthFor( textWidth );
		if ( MathF.Abs( wanted - FixedWidth ) > 0.5f )
			FixedWidth = wanted;

		var hasIcon = !string.IsNullOrEmpty( _icon );
		var group = textWidth + (hasIcon ? IconSize + IconGap : 0);
		var x = LocalRect.Left + MathF.Max( PadX, (LocalRect.Width - group) * 0.5f );
		if ( hasIcon )
		{
			Paint.DrawIcon( new Rect( x, LocalRect.Top, IconSize, LocalRect.Height ), _icon, 15, TextFlag.Center );
			x += IconSize + IconGap;
		}
		Paint.DrawText( new Rect( x, LocalRect.Top, LocalRect.Right - x, LocalRect.Height ), _text, TextFlag.LeftCenter );
	}
}

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
