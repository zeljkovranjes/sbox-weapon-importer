using System.Globalization;
using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Setup;
using WeaponImporter.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.Tool.UI;

/// <summary>Step 1: what the weapon is, which way it points, how big it is, its parts and attachment points.</summary>
public sealed class WeaponStep : StepPanel
{
	public WeaponStep( Widget parent, ImporterController controller ) : base( parent, controller )
	{
		controller.PickChanged += MarkDirty;
	}

	public override string Title => "Weapon";

	protected override string StructureKey()
	{
		var s = C.Setup;
		var parts = string.Join( ",", s.Parts.Select( p => $"{p.Kind}:{p.Bone}:{p.Manual}" ) );
		return $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( C.Analysis )}|{s.Type}|{parts}|{s.Muzzle is null}|{s.Eject is null}";
	}

	protected override void Build()
	{
		BuildSource();
		BuildTypeAndOrientation();
		BuildParts();
		BuildAttachments();
	}

	// ------------------------------------------------------------------ source

	private void BuildSource()
	{
		var session = C.Session;
		var card = AddCard( "description", "Source", out var header, "The file this weapon is imported from" );
		header.AddStretchCell();
		header.Add( UiStyle.Icon( card, "refresh", () => Safe( () => _ = C.RunAsync( "Reloading", ( p, c ) => session.ReloadAsync( p, c ) ) ), "Read the source file again (keeps your manual choices)" ) );
		var name = card.Layout.Add( new Label( System.IO.Path.GetFileName( session.SourcePath ), card ) { ToolTip = session.SourcePath } );
		name.SetStyles( "font-weight: 600;" );
		var a = C.Analysis;
		var summary = $"{a.Asset.Skeleton.Count} bones · {a.WeaponTriangles.Length:N0} triangles · {a.Asset.Clips.Count} animation{(a.Asset.Clips.Count == 1 ? "" : "s")} · {a.Length:0.#} in long";
		card.Layout.Add( UiStyle.Muted( new Label( summary, card ) { WordWrap = true }, small: true ) );
		var row = card.Layout.AddRow();
		row.Spacing = 6;
		row.Add( UiStyle.Secondary( card, "Copy from existing weapon…", "content_copy", CopyFromTemplate, "Take grips, animation mapping, events and attachments from a weapon you already set up (*.weapon.json)" ) );
		row.AddStretchCell();
	}

	private void CopyFromTemplate()
	{
		var folder = AssetCompiler.AssetsRoot.Length > 0 ? System.IO.Path.Combine( AssetCompiler.AssetsRoot, "weapons" ) : null;
		var path = EditorUtility.OpenFileDialog( "Copy from existing weapon", "Weapon setup (*.weapon.json)", folder );
		if ( string.IsNullOrEmpty( path ) )
			return;
		new TemplateDialog( this, path, parts => Safe( () =>
		{
			var notes = C.Session.ApplyTemplate( path, parts );
			C.SetStatus( notes.Count == 0 ? $"Copied from {System.IO.Path.GetFileName( path )}." : string.Join( " ", notes ), notes.Count == 0 ? Theme.Green : Theme.Yellow );
		} ) ).Show();
	}

	// ------------------------------------------------------------------ type + orientation + scale

	private void BuildTypeAndOrientation()
	{
		var s = C.Setup;
		var a = C.Analysis;
		var card = AddCard( "category", "Type and orientation", out _ );

		// Type.
		var typeRow = UiStyle.FieldRow( card, card.Layout, "Type", "What kind of weapon this is; picks the character's hold animation and the expected parts" );
		var type = Combo( card, typeRow, "What kind of weapon this is; picks the character's hold animation and the expected parts" );
		foreach ( var t in Enum.GetValues<WeaponType>() )
		{
			var value = t;
			type.AddItem( WeaponTypes.Label( t ), TypeIcon( t ), () => { if ( value != C.Setup.Type ) SetType( value ); }, selected: t == s.Type );
		}
		var typePill = typeRow.Add( new ConfidencePill( card, a.Type.Confidence, s.TypeManual, a.Type.Reason ) );
		Bind( () => typePill.SetConfidence( a.Type.Confidence, C.Setup.TypeManual, a.Type.Reason ) );
		if ( !string.IsNullOrEmpty( a.Type.Reason ) && !s.TypeManual )
			Indented( card, a.Type.Reason );

		card.Layout.Add( new SectionHeader( card, "Orientation" ) );
		var orientRow = UiStyle.FieldRow( card, card.Layout, "Points", "Rotate the model until the muzzle points forward (+X) and the top is up" );
		var reason = orientRow.Add( UiStyle.Muted( new Label( "", card ) { WordWrap = true, MinimumWidth = 20 }, small: true ), 1 );
		var orientPill = orientRow.Add( new ConfidencePill( card, a.Orientation.Confidence, s.OrientationManual, a.Orientation.Reason ) );
		// Fixing the orientation is rare: one menu instead of a row of rotate buttons.
		orientRow.Add( UiStyle.Icon( card, "rotate_90_degrees_ccw", OrientationMenu, "Rotate the model (90° around an axis, turn it around, or back to the detected orientation)" ) );
		Bind( () =>
		{
			reason.Text = C.Setup.OrientationManual ? "set by you" : a.Orientation.Reason;
			orientPill.SetConfidence( a.Orientation.Confidence, C.Setup.OrientationManual, a.Orientation.Reason );
		} );

		// Scale.
		var scaleRow = UiStyle.FieldRow( card, card.Layout, "Scale", "Model units to inches. The weapon's length is shown to the right." );
		var scale = scaleRow.Add( UiStyle.Framed( new LineEdit( card ) { Text = s.Scale.ToString( "0.#####", CultureInfo.InvariantCulture ), FixedWidth = 90, ToolTip = "Model units to inches; press Enter to apply" } ) );
		var length = scaleRow.Add( UiStyle.Muted( new Label( "", card ) ), 1 );
		Bind( () => length.Text = $"= {C.Analysis.Length:0.#} in long" );
		scale.ReturnPressed += () => Safe( () =>
		{
			if ( !float.TryParse( scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) || value <= 0 || !float.IsFinite( value ) )
			{
				C.SetStatus( "Scale must be a positive number.", Theme.Red );
				return;
			}
			Reanalyze( C.Setup.ModelRotationQ, value );
		} );
		if ( !string.IsNullOrEmpty( a.ScaleReason ) )
			Indented( card, a.ScaleReason );

		// Reference pose.
		if ( a.Asset.Clips.Count > 0 )
		{
			var refRow = UiStyle.FieldRow( card, card.Layout, "Reference pose", "The frame the weapon is measured in (the parts rest where this clip starts)" );
			var clip = Combo( card, refRow, "The frame the weapon is measured in (the parts rest where this clip starts)" );
			clip.AddItem( "Bind pose", "accessibility", () => SetReference( "" ), selected: string.IsNullOrEmpty( a.ReferenceClip ) );
			foreach ( var c in a.Asset.Clips )
			{
				var n = c.Name;
				clip.AddItem( n, "movie", () => SetReference( n ), selected: n == a.ReferenceClip );
			}
		}
	}

	private void OrientationMenu()
	{
		var menu = new Menu( this );
		menu.AddOption( "Roll 90° (barrel axis)", "rotate_90_degrees_ccw", () => Rotate( N.Vector3.UnitX, 90f ) );
		menu.AddOption( "Pitch 90° (side axis)", "rotate_90_degrees_ccw", () => Rotate( N.Vector3.UnitY, 90f ) );
		menu.AddOption( "Yaw 90° (up axis)", "rotate_90_degrees_ccw", () => Rotate( N.Vector3.UnitZ, 90f ) );
		menu.AddOption( "Turn around", "swap_horiz", () => Rotate( N.Vector3.UnitZ, 180f ) );
		if ( C.Setup.OrientationManual )
		{
			menu.AddSeparator();
			menu.AddOption( "Detected orientation and scale", "restart_alt", ResetOrientation );
		}
		menu.OpenAtCursor();
	}

	private void Indented( Widget owner, string text )
	{
		var row = owner.Layout.AddRow();
		row.AddSpacingCell( UiStyle.LabelWidth + 6 );
		row.Add( UiStyle.Muted( new Label( text, owner ) { WordWrap = true }, small: true ), 1 );
	}

	private void SetType( WeaponType type )
	{
		var session = C.Session;
		session.Setup.Type = type;
		session.Setup.TypeManual = true;
		session.MarkChanged();
		_ = C.RunAsync( $"Switching to {WeaponTypes.Label( type )}", async ( p, c ) =>
		{
			await session.PrepareCharacterAsync( p, c );
			await session.ResolveGripAsync( p, c );
		} );
	}

	private void Rotate( N.Vector3 axis, float degrees )
	{
		var q = N.Quaternion.CreateFromAxisAngle( axis, degrees * MathF.PI / 180f );
		var current = C.Setup.OrientationManual ? C.Setup.ModelRotationQ : C.Analysis.ModelToCanonical;
		Reanalyze( N.Quaternion.Normalize( q * current ), C.Setup.OrientationManual ? C.Setup.Scale : null );
	}

	private void Reanalyze( N.Quaternion rotation, float? scale )
	{
		var session = C.Session;
		var s = session.Setup;
		s.OrientationManual = true;
		s.ModelRotationQ = rotation;
		if ( scale is { } v )
			s.Scale = v;
		var reference = string.IsNullOrEmpty( s.ReferenceClip ) ? null : s.ReferenceClip;
		_ = C.RunAsync( "Analyzing", async ( p, c ) =>
		{
			await session.ReanalyzeAsync( new AnalyzeOptions { Rotation = rotation, Scale = scale, ReferenceClip = reference }, p, c );
			await EditorThread.SwitchToMainThread();
			if ( scale is null )
			{
				session.Setup.Scale = session.Analysis.Scale;
				session.MarkChanged();
			}
		} );
	}

	private void ResetOrientation()
	{
		var session = C.Session;
		session.Setup.OrientationManual = false;
		var reference = string.IsNullOrEmpty( session.Setup.ReferenceClip ) ? null : session.Setup.ReferenceClip;
		_ = C.RunAsync( "Analyzing", ( p, c ) => session.ReanalyzeAsync( new AnalyzeOptions { ReferenceClip = reference }, p, c ) );
	}

	private void SetReference( string clip )
	{
		var session = C.Session;
		if ( (session.Analysis.ReferenceClip ?? "") == clip )
			return;
		var s = session.Setup;
		var options = s.OrientationManual
			? new AnalyzeOptions { Rotation = s.ModelRotationQ, Scale = s.Scale, ReferenceClip = clip }
			: new AnalyzeOptions { ReferenceClip = clip };
		_ = C.RunAsync( "Analyzing", ( p, c ) => session.ReanalyzeAsync( options, p, c ) );
	}

	// ------------------------------------------------------------------ parts

	private void BuildParts()
	{
		var s = C.Setup;
		var a = C.Analysis;
		var card = AddCard( "extension", "Parts", out var header, "Moving or notable parts found on the weapon; they drive events, hand contacts and attachments" );
		header.AddStretchCell();
		var expected = WeaponTypes.ExpectedParts( s.Type );
		var kinds = s.Parts.Select( p => p.Kind ).Concat( expected ).Distinct().ToList();
		if ( kinds.Count == 0 )
		{
			card.Layout.Add( UiStyle.Muted( new Label( "No moving parts found. That is fine for a static weapon.", card ) { WordWrap = true }, small: true ) );
			return;
		}
		var bones = a.Asset.Skeleton.Bones.Select( b => b.Name ).ToList();
		foreach ( var kind in kinds )
		{
			var part = s.Part( kind );
			var detection = a.Part( kind );
			var row = card.Layout.Add( new HoverRow( card, () => C.HighlightBone = s.Part( kind )?.Bone, () => C.HighlightBone = null ) );
			row.ToolTip = part is null
				? $"{kind}: expected on a {WeaponTypes.Label( s.Type ).ToLowerInvariant()} but not found. Pick its bone if it has one."
				: $"{kind}{(part.Meshes.Count > 0 ? " · meshes: " + string.Join( ", ", part.Meshes ) : "")}{(detection?.Reason is { Length: > 0 } r ? " · " + r : "")}";
			UiStyle.IconCaption( row, row.Layout, PartIcon( kind ), part is null ? Theme.TextLight : Theme.Green, Nice( kind ), muted: part is null );
			var combo = Combo( row, row.Layout, $"Weapon bone that moves the {Nice( kind ).ToLowerInvariant()}" );
			var building = true;
			combo.AddItem( "None", "block", () => { if ( !building ) AssignPart( kind, "" ); }, selected: part is null || part.Bone.Length == 0 );
			foreach ( var bone in bones )
			{
				var b = bone;
				combo.AddItem( b, "linear_scale", () => { if ( !building ) AssignPart( kind, b ); }, selected: part is not null && part.Bone == b );
			}
			building = false;
			if ( part is not null )
				row.Layout.Add( new ConfidencePill( row, part.Confidence, part.Manual, detection?.Reason ) );
			else
				row.Layout.Add( new Pill( row, "MISSING", Theme.TextLight, "Not found on the model", column: true ) );
		}
	}

	private void AssignPart( PartKind kind, string bone )
	{
		Safe( () =>
		{
			var s = C.Setup;
			var part = s.Part( kind );
			if ( part is null )
			{
				if ( bone.Length == 0 )
					return;
				part = new PartSetup { Kind = kind };
				s.Parts.Add( part );
			}
			part.Bone = bone;
			part.Manual = true;
			part.Confidence = 1f;
			C.HighlightBone = bone.Length > 0 ? bone : null;
			C.Session.Revalidate();
			C.MarkChanged();
		} );
	}

	// ------------------------------------------------------------------ attachments

	private void BuildAttachments()
	{
		var s = C.Setup;
		if ( !WeaponTypes.IsFirearm( s.Type ) )
			return;
		var card = AddCard( "push_pin", "Muzzle and shell eject", out _, "Where bullets, muzzle flashes and shells come from" );
		card.Layout.Add( UiStyle.Muted( new Label( "Select a marker here or click it in the preview, then click the weapon to move it. Esc deselects.", card ) { WordWrap = true }, small: true ) );
		PointRow( card, "Muzzle", "flare", PickTarget.Muzzle, () => C.Setup.Muzzle, () => C.Analysis.Muzzle, p => C.Setup.Muzzle = p );
		PointRow( card, "Shell eject", "eject", PickTarget.Eject, () => C.Setup.Eject, () => C.Analysis.Eject, p => C.Setup.Eject = p );
	}

	private void PointRow( Card card, string title, string icon, PickTarget target, Func<PointSetup> get, Func<PointDetection> detected, Action<PointSetup> set )
	{
		var row = card.Layout.Add( new HoverRow( card, null, null ) { Clicked = () => C.BeginPick( target ), ToolTip = $"Select the {title.ToLowerInvariant()} marker, then click the weapon in the preview to move it" } );
		UiStyle.IconCaption( row, row.Layout, icon, target == PickTarget.Muzzle ? Theme.Red : Theme.Yellow, title );
		var where = row.Layout.Add( UiStyle.Muted( new Label( "", row ) { MinimumWidth = 20, FixedHeight = UiStyle.ControlHeight }, small: true ), 1 );
		var reset = row.Layout.Add( UiStyle.Icon( row, "restart_alt", () => Safe( () =>
		{
			var d = detected();
			set( d is null ? null : AutoSetup.Point( C.Analysis, d ) );
			C.Session.Revalidate();
			C.MarkChanged();
		} ), "Back to the detected point" ) );
		var pill = row.Layout.Add( new Pill( row, "", Theme.TextLight, column: true ) );
		Bind( () =>
		{
			var p = get();
			var selected = C.Pick == target;
			where.Text = selected ? "click the weapon to move it" : p is null ? "not placed" : $"{p.Canonical[0]:0.0}, {p.Canonical[1]:0.0}, {p.Canonical[2]:0.0} in";
			where.ToolTip = p is null ? null : $"On bone {p.Bone}";
			if ( p is null )
				pill.Set( "MISSING", Theme.Red );
			else
				pill.Set( p.Manual ? "PLACED" : UiStyle.ConfidenceText( p.Confidence ), UiStyle.ConfidenceColor( p.Confidence, p.Manual ) );
			row.Selected = selected;
			reset.Visible = p is { Manual: true } && detected() is not null;
		} );
	}

	public override void OnDestroyed()
	{
		C.PickChanged -= MarkDirty;
		base.OnDestroyed();
	}

	// ------------------------------------------------------------------ helpers

	public static string Nice( PartKind kind ) => kind switch
	{
		PartKind.ChargingHandle => "Charging handle",
		_ => kind.ToString(),
	};

	public static string PartIcon( PartKind kind ) => kind switch
	{
		PartKind.Magazine => "inventory",
		PartKind.Slide => "swap_horiz",
		PartKind.Bolt => "settings_ethernet",
		PartKind.ChargingHandle => "keyboard_tab",
		PartKind.Trigger => "touch_app",
		PartKind.Hammer => "gavel",
		PartKind.Cylinder => "donut_large",
		PartKind.Pump => "sync_alt",
		PartKind.Foregrip => "pan_tool",
		PartKind.Stock => "chair_alt",
		PartKind.Scope => "center_focus_strong",
		PartKind.Barrel => "straighten",
		_ => "extension",
	};

	public static string TypeIcon( WeaponType type ) => type switch
	{
		WeaponType.Melee => "hardware",
		WeaponType.Launcher => "rocket_launch",
		WeaponType.Custom => "category",
		_ => "gps_fixed",
	};
}

/// <summary>
/// A row that reports hover (to highlight the matching bone in the viewport) and, optionally,
/// clicks and a selected state (green edge, like the animation rows).
/// </summary>
public sealed class HoverRow : Widget
{
	private readonly Action _enter;
	private readonly Action _leave;
	private bool _selected;

	public Action Clicked { get; set; }

	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value )
				return;
			_selected = value;
			Update();
		}
	}

	public HoverRow( Widget parent, Action enter, Action leave ) : base( parent )
	{
		_enter = enter;
		_leave = leave;
		Layout = Layout.Row();
		Layout.Spacing = UiStyle.RowSpacing;
		MouseTracking = true;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( Clicked is null || !e.LeftMouseButton )
			return;
		Clicked();
		e.Accepted = true;
	}

	protected override void OnMouseEnter()
	{
		_enter?.Invoke();
		Update();
	}

	protected override void OnMouseLeave()
	{
		_leave?.Invoke();
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		if ( _selected )
		{
			Paint.SetPen( Theme.Green.WithAlpha( .6f ), 1 );
			Paint.SetBrush( Theme.Green.WithAlpha( .08f ) );
			Paint.DrawRect( LocalRect.Shrink( .5f ), UiStyle.Radius );
			return;
		}
		if ( !Paint.HasMouseOver )
			return;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground.Lighten( .3f ) );
		Paint.DrawRect( LocalRect, UiStyle.Radius );
	}
}

/// <summary>A tinted 18px icon.</summary>
public sealed class IconLabel : Widget
{
	private readonly string _icon;
	private readonly Color _color;

	public IconLabel( Widget parent, string icon, Color color ) : base( parent )
	{
		_icon = icon;
		_color = color;
		FixedSize = 20;
	}

	protected override void OnPaint()
	{
		Paint.SetPen( _color );
		Paint.DrawIcon( LocalRect, _icon, 16 );
	}
}
