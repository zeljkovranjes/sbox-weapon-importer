using Editor;
using Sandbox;
using WeaponImporter.Core.Materials;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// Materials: which texture fills each material slot. Attach Textures (on by default) uses the
/// images found beside the model that belong to its materials; sure matches are attached, weaker
/// ones are proposed. Click a row to use or skip that texture.
/// </summary>
public sealed class MaterialsStep : StepPanel
{
	public MaterialsStep( Widget parent, ImporterController controller ) : base( parent, controller )
	{
	}

	public override string Title => "Materials";

	protected override string StructureKey()
	{
		var s = C.Setup;
		return $"{s.AttachTextures}|{string.Join( ",", s.TextureChoices.Select( kv => kv.Key + "=" + kv.Value ) )}|{C.Session.TextureMatches.Count}|{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( C.Analysis )}";
	}

	protected override void Build()
	{
		var session = C.Session;
		var s = C.Setup;
		var matches = session.TextureMatches;
		var found = matches.Where( m => !m.Linked ).ToList();
		var used = found.Count( m => ImportSession.Accepted( s, m ) );

		var card = AddCard( "texture", "Attach Textures", out var header, "Images beside the model that belong to its materials but were never linked" );
		header.AddStretchCell();
		header.Add( new Pill( card, found.Count == 0 ? "NONE FOUND" : $"{used} / {found.Count} USED", found.Count == 0 ? Theme.TextLight : Theme.Green ) );
		var toggle = card.Layout.Add( new Checkbox( "Attach textures found beside the model", card ) { Value = s.AttachTextures } );
		toggle.ToolTip = "Match image files next to the model (and in texture folders) to material slots by name. Sure matches are attached; weaker ones are shown for you to confirm.";
		toggle.StateChanged = _ =>
		{
			if ( s.AttachTextures == toggle.Value )
				return;
			s.AttachTextures = toggle.Value;
			Reapply();
		};
		if ( found.Count == 0 )
			card.Layout.Add( UiStyle.Muted( new Label( matches.Count == 0 ? "No textures linked or found beside the model." : "Every texture the materials use is linked in the file.", card ) { WordWrap = true }, small: true ) );

		// One card per material with its slots.
		foreach ( var group in matches.GroupBy( m => m.MaterialName ) )
		{
			var materialCard = AddCard( "palette", group.Key, out var h, "Material" );
			foreach ( var slotGroup in group.GroupBy( m => m.Slot ).OrderBy( g => g.Key ) )
				foreach ( var match in slotGroup.OrderByDescending( m => m.Confidence ) )
					AddRow( materialCard, match );
		}
	}

	private void AddRow( Card card, TextureMatch match )
	{
		var s = C.Setup;
		var accepted = ImportSession.Accepted( s, match );
		var tip = $"{match.Path}\n{match.Reason}{(match.Linked ? "" : $" · {match.Confidence * 100:0}% sure")}" + (match.Linked ? "" : accepted ? "\nClick to leave this slot empty." : "\nClick to use this texture.");
		var row = card.Layout.Add( new ClickRow( card, match.Linked ? null : () => Toggle( match, !accepted ), tip ) );
		row.Layout.Add( UiStyle.Muted( new Label( TextureMatch.Label( match.Slot ), row ) { FixedWidth = 84, FixedHeight = UiStyle.ControlHeight, Alignment = TextFlag.LeftCenter } ) );
		var name = row.Layout.Add( new Label( UiStyle.Breakable( System.IO.Path.GetFileName( match.Path ) ), row ) { WordWrap = true, MinimumHeight = UiStyle.ControlHeight, Alignment = TextFlag.LeftCenter, MinimumWidth = 40 }, 1 );
		if ( !accepted )
			UiStyle.Muted( name );
		if ( match.Linked )
			row.Layout.Add( new Pill( row, "LINKED", Theme.TextLight, "Linked in the model file", column: true ) );
		else if ( accepted )
			row.Layout.Add( new Pill( row, match.Confidence >= TextureMatcher.AutoAttach ? "ATTACHED" : "USED", Theme.Green, $"{match.Confidence * 100:0}% sure: {match.Reason}", column: true ) );
		else
			row.Layout.Add( new Pill( row, $"{match.Confidence * 100:0}%", Theme.Yellow, $"Proposed: {match.Reason}", column: true ) );
	}

	private void Toggle( TextureMatch match, bool use )
	{
		var s = C.Setup;
		s.TextureChoices[match.Key] = use ? match.Path : "";
		Reapply();
	}

	private void Reapply()
	{
		var session = C.Session;
		ForceRebuild();
		_ = C.RunAsync( "Attaching textures", ( p, c ) => session.ApplyTextureChoicesAsync( p, c ) );
	}
}
