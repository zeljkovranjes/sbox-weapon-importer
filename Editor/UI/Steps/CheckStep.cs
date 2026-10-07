using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Setup;

namespace WeaponImporter.EditorTools.UI.Steps;
using WeaponImporter.EditorTools.Engine;
using WeaponImporter.EditorTools.UI.Widgets;

/// <summary>Step 4: what still needs attention, then Bake and the generated files.</summary>
public sealed class CheckStep : StepPanel
{
	public CheckStep( Widget parent, ImporterController controller ) : base( parent, controller )
	{
		controller.BusyChanged += MarkDirty;
	}

	public override string Title => "Export";

	protected override string StructureKey()
	{
		var checks = string.Join( ";", C.Session.Checks.Select( c => $"{c.Name}:{c.Severity}:{c.Message}" ) );
		return $"{checks}|{C.Busy}|{C.LastBake?.GetHashCode()}";
	}

	protected override void Build()
	{
		BuildChecks();
		BuildBake();
	}

	private void BuildChecks()
	{
		var checks = C.Session.Checks;
		var problems = Validation.Problems( checks ).ToList();
		var card = AddCard( "fact_check", "Validation", out var header, "Everything the importer verified" );
		header.AddStretchCell();
		if ( problems.Count == 0 )
			header.Add( new Pill( card, "ALL GOOD", Theme.Green ) );
		else
			header.Add( new Pill( card, $"{problems.Count} TO LOOK AT", problems.Any( p => p.Severity == CheckSeverity.Error ) ? Theme.Red : Theme.Yellow ) );

		foreach ( var check in problems )
			card.Layout.Add( new CheckLine( card, check, expanded: true, () => Fix( check ) ) );
		if ( problems.Count > 0 )
			card.Layout.AddSpacingCell( 4 );
		foreach ( var check in checks.Where( c => !c.NeedsAttention ) )
			card.Layout.Add( new CheckLine( card, check, expanded: false, check.AutoFix is null ? null : () => Fix( check ) ) );
		if ( checks.Count == 0 )
			card.Layout.Add( UiStyle.Muted( new Label( "Nothing checked yet.", card ) ) );
	}

	private void Fix( CheckResult check )
	{
		Safe( () =>
		{
			check.AutoFix?.Invoke();
			C.Session.Revalidate();
			C.MarkChanged();
			C.ScheduleResolve( 0.01f );
			C.SetStatus( $"{check.Name}: {check.AutoFixLabel.ToLowerInvariant()} applied.", Theme.Green );
		} );
	}

	private void BuildBake()
	{
		var session = C.Session;
		var card = AddCard( "inventory_2", "Bake", out _, "Write the model, animations, attachments and a ready prefab" );
		card.Layout.Add( UiStyle.Muted( new Label( $"Output: {session.OutputFolder}/", card ) { WordWrap = true, ToolTip = "Files are written here inside the project's assets" }, small: true ) );
		var s = session.Setup;
		if ( session.Analysis?.ArmBones.Count > 0 )
		{
			var fp = card.Layout.Add( new Checkbox( "Bake the first-person viewmodel", card ) { Value = s.ExportFirstPerson } );
			fp.ToolTip = "Also write <name>_fp.vmdl: this file's own arms, weapon and camera with every animation as authored. The prefab shows it to the player holding the weapon, in sync with the third-person hold.";
			fp.StateChanged = _ => s.ExportFirstPerson = fp.Value;
		}
		var sizeRow = UiStyle.FieldRow( card, card.Layout, "Textures", "Largest texture size in the baked weapon; bigger images are scaled down (smaller files, same look at normal distances)" );
		var size = sizeRow.Add( UiStyle.Framed( new ComboBox( card ) ), 1 );
		foreach ( var (label, value) in new[] { ("Original size", 0), ("4096", 4096), ("2048", 2048), ("1024", 1024) } )
		{
			var v = value;
			size.AddItem( label, "photo_size_select_large", () => s.MaxTextureSize = v, selected: s.MaxTextureSize == value );
		}
		var corrected = card.Layout.Add( new Checkbox( "Bake corrected third-person animations", card ) { Value = s.BakeCorrectedAnimations } );
		corrected.ToolTip = "Also record the character's actions with the hands on this weapon as new clips (<name>_corrected.vmdl). The character's own animations are never changed.";
		corrected.StateChanged = _ => s.BakeCorrectedAnimations = corrected.Value;
		var use = card.Layout.Add( new Checkbox( "Play the corrected clips in the prefab", card ) { Value = s.UseCorrectedAnimations } );
		use.ToolTip = "Off: the prefab corrects the character's live animation every frame (works with any animgraph blend). On: it plays the baked corrected clips over the upper body.";
		use.StateChanged = _ => s.UseCorrectedAnimations = use.Value;
		var bake = card.Layout.Add( new Button.Primary( "Bake", card ) { Icon = "inventory_2", Tint = Theme.Green, FixedHeight = 36, Enabled = !C.Busy, ToolTip = "Write and compile the weapon model and its prefab" } );
		bake.Clicked = () => _ = C.BakeAsync();

		var result = C.LastBake;
		if ( result is null )
			return;

		card.Layout.Add( new SectionHeader( card, result.Success ? "Baked" : "Bake finished with errors" ) );
		var links = card.Layout.AddRow();
		links.Spacing = 6;
		links.Add( new UiButton( card, "Open model", "view_in_ar", () => Open( result.ModelPath ), result.ModelPath ) { Enabled = FindAsset( result.ModelPath ) is not null } );
		links.Add( new UiButton( card, "Open prefab", "inventory_2", () => Open( result.PrefabPath ), result.PrefabPath ) { Enabled = FindAsset( result.PrefabPath ) is not null } );
		links.Add( new UiButton( card, "Show in asset browser", "folder_open", () => Reveal( result.ModelPath ), "Reveal the generated model in the asset browser" ) );
		links.AddStretchCell();

		foreach ( var file in result.Files )
		{
			var errors = result.Errors.TryGetValue( file, out var list ) ? list : null;
			var row = card.Layout.AddRow();
			row.Spacing = 6;
			row.Add( new IconLabel( card, errors is null ? "check_circle" : "error", errors is null ? Theme.Green : Theme.Red ) );
			row.Add( new Label( file, card ) { ToolTip = AssetCompiler.Absolute( file ) }, 1 );
			if ( errors is not null )
			{
				foreach ( var e in errors.Take( 6 ) )
				{
					var er = card.Layout.AddRow();
					er.AddSpacingCell( 26 );
					er.Add( UiStyle.Colored( new Label( e, card ) { WordWrap = true }, Theme.Red ), 1 );
				}
			}
		}
		foreach ( var (file, errors) in result.Errors.Where( kv => !result.Files.Contains( kv.Key ) ) )
			card.Layout.Add( UiStyle.Colored( new Label( $"{file}: {errors.FirstOrDefault()}", card ) { WordWrap = true }, Theme.Red ) );
		foreach ( var note in result.Notes )
			card.Layout.Add( UiStyle.Muted( new Label( note, card ) { WordWrap = true }, small: true ) );
	}

	private static Asset FindAsset( string relative ) => string.IsNullOrEmpty( relative ) ? null : AssetSystem.FindByPath( relative );

	private void Open( string relative )
	{
		Safe( () =>
		{
			var asset = FindAsset( relative );
			if ( asset is null )
				C.SetStatus( $"{relative} is not in the asset system yet.", Theme.Yellow );
			else
				asset.OpenInEditor();
		} );
	}

	private void Reveal( string relative )
	{
		Safe( () =>
		{
			var asset = FindAsset( relative );
			if ( asset is not null )
				AssetBrowser.OpenTo( asset );
			else
				EditorUtility.OpenFolder( System.IO.Path.GetDirectoryName( AssetCompiler.Absolute( relative ) ) );
		} );
	}

	public override void OnDestroyed()
	{
		C.BusyChanged -= MarkDirty;
		base.OnDestroyed();
	}
}
