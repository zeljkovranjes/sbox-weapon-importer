using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Setup;

namespace WeaponImporter.EditorTools.UI.Steps;
using WeaponImporter.EditorTools.Engine;
using WeaponImporter.EditorTools.UI.Widgets;

/// <summary>"✓ Skeleton" / "! Left wrist penetration detected" / "× Muzzle missing".</summary>
public sealed class CheckLine : Widget
{
	private readonly CheckResult _check;
	private readonly bool _expanded;

	public CheckLine( Widget parent, CheckResult check, bool expanded, Action fix ) : base( parent )
	{
		_check = check;
		_expanded = expanded;
		Layout = Layout.Row();
		Layout.Spacing = 8;
		Layout.Margin = expanded ? new Sandbox.UI.Margin( 10, 7, 7, 7 ) : new Sandbox.UI.Margin( 10, 1, 4, 1 );
		var color = ColorOf( check.Severity );
		Layout.Add( new Glyph( this, GlyphOf( check.Severity ), color ) );
		var text = Layout.AddColumn( 1 );
		text.Spacing = 2;
		if ( expanded )
		{
			text.Add( UiStyle.Colored( new Label( check.Name, this ), color, bold: true ) );
			if ( !string.IsNullOrEmpty( check.Message ) )
				text.Add( new Label( check.Message, this ) { WordWrap = true } );
		}
		else
		{
			var line = text.AddRow();
			line.Spacing = 6;
			line.Add( new Label( check.Name, this ) );
			if ( !string.IsNullOrEmpty( check.Message ) )
				line.Add( UiStyle.Muted( new Label( check.Message, this ) { ToolTip = check.Message, WordWrap = true, MinimumWidth = 20 }, small: true ), 1 );
			else
				line.AddStretchCell();
		}
		if ( fix is not null && check.AutoFix is not null )
		{
			var button = Layout.Add( new UiButton( this, check.AutoFixLabel, "auto_fix_high", null, $"Apply the safe fix for: {check.Name}" ) );
			button.Clicked = fix;
		}
		ToolTip = string.IsNullOrEmpty( check.Message ) ? check.Name : $"{check.Name}: {check.Message}";
	}

	public static Color ColorOf( CheckSeverity s ) => s switch
	{
		CheckSeverity.Error => Theme.Red,
		CheckSeverity.Warning => Theme.Yellow,
		CheckSeverity.Info => Theme.Blue,
		_ => Theme.Green,
	};

	public static string GlyphOf( CheckSeverity s ) => s switch
	{
		CheckSeverity.Error => "×",
		CheckSeverity.Warning => "!",
		CheckSeverity.Info => "i",
		_ => "✓",
	};

	protected override void OnPaint()
	{
		if ( !_expanded )
			return;
		Paint.Antialiasing = true;
		var color = ColorOf( _check.Severity );
		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( .07f ) );
		Paint.DrawRect( LocalRect, 4 );
		Paint.SetBrush( color );
		Paint.DrawRect( new Rect( 0, 0, 3, Height ), 1.5f );
	}

	private sealed class Glyph : Widget
	{
		private readonly string _glyph;
		private readonly Color _color;

		public Glyph( Widget parent, string glyph, Color color ) : base( parent )
		{
			_glyph = glyph;
			_color = color;
			FixedSize = 16;
		}

		protected override void OnPaint()
		{
			Paint.SetPen( _color );
			Paint.SetDefaultFont( 9, 700 );
			Paint.DrawText( LocalRect, _glyph, TextFlag.Center );
		}
	}
}
