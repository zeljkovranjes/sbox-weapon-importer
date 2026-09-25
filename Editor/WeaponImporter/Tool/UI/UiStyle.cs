using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>Shared look of the importer window (same language as the mocap tool).</summary>
public static class UiStyle
{
	/// <summary>
	/// Lets a wrapping label break long names (file names, bone paths) after separators; without
	/// break opportunities a single long word sets the label's minimum width.
	/// </summary>
	public static string Breakable( string text ) => string.IsNullOrEmpty( text ) ? text
		: System.Text.RegularExpressions.Regex.Replace( text, "([_./\\\\-])", "$1\u200B" );

	/// <summary>Caption column of every labelled row, so controls line up across cards.</summary>
	public const float LabelWidth = 88f;

	/// <summary>One height for every control in a row: dropdowns, inputs, buttons, click fields.</summary>
	public const float ControlHeight = 26f;

	/// <summary>Width of the pill column at the end of rows (confidence, state).</summary>
	public const float PillColumn = 64f;

	public const float RowSpacing = 6f;
	public const float Radius = 4f;

	public static Color ButtonFill => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .07f );
	public static Color ButtonEdge => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .15f );
	public static Color InputEdge => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .17f );

	/// <summary>Inputs sit on the cards' dark gray; a darker fill and a thin edge keep them visible as inputs.</summary>
	public static T Framed<T>( T input ) where T : Widget
	{
		input.SetStyles( $"background-color: {Theme.WindowBackground.Hex}; border: 1px solid {InputEdge.Hex}; border-radius: {Radius}px; padding-left: 4px;" );
		input.FixedHeight = ControlHeight;
		return input;
	}

	public static Label Muted( Label label, bool small = false )
	{
		label.SetStyles( small ? $"color: {Theme.TextLight.Hex}; font-size: 11px;" : $"color: {Theme.TextLight.Hex};" );
		return label;
	}

	public static Label Bold( Label label )
	{
		label.SetStyles( "font-weight: 600;" );
		return label;
	}

	public static Label Colored( Label label, Color color, bool bold = false )
	{
		label.SetStyles( $"color: {color.Hex};" + (bold ? " font-weight: 600;" : "") );
		return label;
	}

	/// <summary>Green when sure, yellow when plausible, red when guessed; blue once a person set it.</summary>
	public static Color ConfidenceColor( float confidence, bool manual = false )
	{
		if ( manual )
			return Theme.Blue;
		if ( confidence >= 0.75f )
			return Theme.Green;
		if ( confidence >= 0.45f )
			return Theme.Yellow;
		return Theme.Red;
	}

	public static string ConfidenceText( float confidence, bool manual = false )
		=> manual ? "EDITED" : $"{Math.Clamp( confidence, 0f, 1f ) * 100f:0}%";

	/// <summary>A labelled row: fixed-width muted caption, then whatever the caller adds.</summary>
	public static Layout FieldRow( Widget owner, Layout parent, string caption, string tooltip = null, float width = LabelWidth )
	{
		var row = parent.AddRow();
		row.Spacing = RowSpacing;
		var label = row.Add( Muted( new Label( caption, owner ) { FixedWidth = width, FixedHeight = ControlHeight, ToolTip = tooltip } ) );
		label.Alignment = TextFlag.LeftCenter;
		return row;
	}

	/// <summary>Row caption with a leading icon; icon + caption take exactly <see cref="LabelWidth"/>.</summary>
	public static Label IconCaption( Widget owner, Layout row, string icon, Color iconColor, string caption, bool muted = false )
	{
		row.Add( new IconLabel( owner, icon, iconColor ) );
		var label = row.Add( new Label( caption, owner ) { FixedWidth = LabelWidth - 20 - RowSpacing, FixedHeight = ControlHeight, Alignment = TextFlag.LeftCenter } );
		if ( muted )
			Muted( label );
		return label;
	}

	public static Button Primary( string text, string icon, Action clicked, string tooltip )
		=> new Button.Primary( text ) { Icon = icon, Tint = Theme.Green, FixedHeight = 30, Clicked = clicked, ToolTip = tooltip };

	/// <summary>The one secondary button style (see <see cref="UiButton"/>).</summary>
	public static UiButton Secondary( Widget parent, string text, string icon, Action clicked, string tooltip, float height = ControlHeight )
		=> new( parent, text, icon, clicked, tooltip, height );

	/// <summary>Square icon button in the secondary style.</summary>
	public static IconButton Icon( Widget parent, string icon, Action clicked, string tooltip, float size = ControlHeight )
		=> new IconButton( icon, clicked, parent )
		{
			FixedSize = size,
			IconSize = 16,
			ToolTip = tooltip,
			Background = ButtonFill,
			Foreground = Theme.Text,
			BackgroundActive = Theme.Green.WithAlpha( .2f ),
			ForegroundActive = Theme.Green,
		};

	/// <summary>Square icon toggle (overlays, loop, hand release).</summary>
	public static IconButton Toggle( Widget parent, string icon, bool on, Action<bool> toggled, string tooltip )
	{
		var button = Icon( parent, icon, null, tooltip );
		button.IsToggle = true;
		button.IsActive = on;
		button.OnToggled = toggled;
		return button;
	}
}
