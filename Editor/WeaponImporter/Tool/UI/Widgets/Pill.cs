using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// A rounded tinted label: type, confidence, quality. A column pill keeps a fixed width (and
/// stays in the layout when empty) so pills at the end of rows line up; the pill is drawn
/// right-aligned inside it.
/// </summary>
public class Pill : Widget
{
	private string _text = "";
	private Color _color = Theme.TextLight;
	private readonly bool _column;

	public Pill( Widget parent, string text, Color color, string tooltip = null, bool column = false ) : base( parent )
	{
		_column = column;
		FixedHeight = UiStyle.ControlHeight;
		ToolTip = tooltip;
		if ( column )
			FixedWidth = UiStyle.PillColumn;
		_text = null;
		Set( text, color );
	}

	public string Text => _text;

	public void Set( string text, Color color )
	{
		text ??= "";
		if ( _text == text && _color == color )
			return;
		_text = text;
		_color = color;
		if ( !_column )
		{
			FixedWidth = MeasureWidth( text );
			Visible = text.Length > 0;
		}
		Update();
	}

	public static float MeasureWidth( string text ) => 7.2f * text.Length + 18;

	protected override void OnPaint()
	{
		if ( string.IsNullOrEmpty( _text ) )
			return;
		var w = MathF.Min( Width, MeasureWidth( _text ) );
		Draw( new Rect( Width - w, (Height - 20) * .5f, w, 20 ), _text, _color );
	}

	public static void Draw( Rect rect, string text, Color color )
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( 0.18f ) );
		Paint.DrawRect( rect, rect.Height * 0.5f );
		Paint.SetPen( color );
		Paint.SetDefaultFont( 7, 600 );
		Paint.DrawText( rect, text );
	}
}

/// <summary>A pill showing how sure the importer is about an automatic choice (always a column pill).</summary>
public sealed class ConfidencePill : Pill
{
	public ConfidencePill( Widget parent, float confidence, bool manual = false, string reason = null )
		: base( parent, UiStyle.ConfidenceText( confidence, manual ), UiStyle.ConfidenceColor( confidence, manual ), column: true )
	{
		SetConfidence( confidence, manual, reason );
	}

	public void SetConfidence( float confidence, bool manual, string reason = null )
	{
		Set( UiStyle.ConfidenceText( confidence, manual ), UiStyle.ConfidenceColor( confidence, manual ) );
		ToolTip = manual
			? "Set by you; re-running the analysis keeps it."
			: $"Detected automatically ({confidence * 100f:0}% sure){(string.IsNullOrEmpty( reason ) ? "" : ": " + reason)}";
	}
}

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
