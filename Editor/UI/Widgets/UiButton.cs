using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

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
