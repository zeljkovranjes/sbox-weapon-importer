using Editor;
using Sandbox;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool.UI;

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
