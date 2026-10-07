using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

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
