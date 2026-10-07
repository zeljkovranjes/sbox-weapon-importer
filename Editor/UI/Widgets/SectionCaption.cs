using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

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
