using Editor;
using Sandbox;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool.UI;

/// <summary>Asks which parts of a configured weapon to copy onto the current one.</summary>
public sealed class TemplateDialog : Dialog
{
	private static readonly (TemplateParts Part, string Label, string Tip)[] Options =
	{
		(TemplateParts.AnimationMappings, "Animation mapping", "Which clip plays for each role, matched by role rather than by name"),
		(TemplateParts.ThirdPerson, "Third-person animations", "What the character plays per role"),
		(TemplateParts.Grips, "Grips", "Hand placement, carried over by its place on the weapon and re-measured on the new surface"),
		(TemplateParts.Attachments, "Muzzle and shell eject", "Attachment points, adapted to the new barrel"),
		(TemplateParts.Events, "Events", "Fire, magazine and bolt events; times are normalized to fit clips of any length"),
		(TemplateParts.Contacts, "Hand contacts", "When the support hand lets go and grabs again"),
		(TemplateParts.Ik, "Fine tuning", "Wrist preference, weapon nudge and smoothing"),
		(TemplateParts.TypeDefaults, "Type and defaults", "Weapon type, trigger finger and support-hand choice"),
	};

	public TemplateDialog( Widget parent, string templatePath, Action<TemplateParts> apply ) : base( parent )
	{
		Window.Title = "Copy from existing weapon";
		Window.SetWindowIcon( "content_copy" );
		Window.SetModal( true, true );
		Layout = Layout.Column();
		Layout.Margin = 14;
		Layout.Spacing = 8;
		Layout.Add( UiStyle.Bold( new Label( System.IO.Path.GetFileName( templatePath ), this ) ) );
		Layout.Add( UiStyle.Muted( new Label( "Choose what to take from it. Everything is adapted to this weapon's geometry and clips.", this ) { WordWrap = true }, small: true ) );
		Layout.AddSpacingCell( 4 );
		var boxes = new List<(TemplateParts, Checkbox)>();
		foreach ( var (part, label, tip) in Options )
			boxes.Add( (part, Layout.Add( new Checkbox( label, this ) { Value = true, ToolTip = tip } )) );
		Layout.AddSpacingCell( 6 );
		var row = Layout.AddRow();
		row.Spacing = 8;
		row.AddStretchCell();
		row.Add( new UiButton( this, "Cancel", "close", Close, "Keep the current setup", 30 ) );
		row.Add( new Button.Primary( "Apply", this ) { Icon = "content_copy", Tint = Theme.Green, ToolTip = "Copy the checked parts onto this weapon", Clicked = () =>
		{
			var parts = TemplateParts.None;
			foreach ( var (part, box) in boxes )
				if ( box.Value )
					parts |= part;
			Close();
			apply( parts );
		} } );
		Window.MinimumSize = new Vector2( 360, 360 );
		Window.Size = new Vector2( 400, 400 );
	}
}
