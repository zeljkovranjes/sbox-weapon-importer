using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Grasp;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool.UI;

/// <summary>Step 2: where the hands hold the weapon and how the fit is tuned.</summary>
public sealed class HandsStep : StepPanel
{
	private Side _curlSide = Side.Right;
	private HandPose _curlBase;
	private readonly Dictionary<FingerKind, float> _curl = new();

	public HandsStep( Widget parent, ImporterController controller ) : base( parent, controller )
	{
		controller.PickChanged += MarkDirty;
	}

	public override string Title => "Hands";

	protected override string StructureKey()
	{
		var s = C.Setup;
		return $"{s.UseSupportHand}|{s.Primary.Preset}|{s.Support?.Preset}|{C.Session.Grips.Count}|{C.Pick}|{s.Support is null}|{s.Character}|{_curlSide}|{C.Session.Character?.Left is null}|{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( C.Analysis )}";
	}

	protected override void Build()
	{
		_curlBase = null;
		_curl.Clear();
		BuildFit();
		BuildHand( Side.Right );
		BuildHand( Side.Left );
		BuildTrigger();
		BuildFineTune();
		BuildFingers();
		BuildRegions();
		BuildLibrary();
	}

	// ------------------------------------------------------------------ fit

	private void BuildFit()
	{
		var card = AddCard( "auto_fix_high", "Auto Grip", out var header, "Fit both hands to the weapon: library grips and the procedural fit compete, the best fit wins" );
		header.AddStretchCell();
		header.Add( new UiButton( card, "Auto Grip", "auto_fix_high", () => AutoFit( null ), "Forget hand edits and fit both hands again" ) );

		// The character's hold animation (its holdtype): the one the weapon fits best, or forced.
		var s = C.Setup;
		var holdRow = UiStyle.FieldRow( card, card.Layout, "Hold", "The character's hold animation (holdtype) the weapon is fitted to" );
		var hold = Combo( card, holdRow, "The character's hold animation. Auto tries the ones that suit this weapon type and keeps the best fit." );
		var ranking = C.Session.HoldRanking;
		var best = ranking.Count > 0 ? ranking[0].Hold : s.EffectiveHoldType;
		hold.AddItem( $"Auto ({GripSolver.HoldLabel( best )})", "auto_fix_high", () => SetHold( -1, false ),
			description: ranking.Count > 1 ? "Tried: " + string.Join( ", ", ranking.Select( r => $"{GripSolver.HoldLabel( r.Hold )} {r.Score:0.0}" ) ) : "The weapon type's hold", selected: !s.HoldTypeManual );
		foreach ( var h in new[] { 1, 2, 3, 4, 6, 7 } )
		{
			var value = h;
			hold.AddItem( GripSolver.HoldLabel( h ), "accessibility_new", () => SetHold( value, true ), selected: s.HoldTypeManual && s.EffectiveHoldType == h );
		}

		var mag = card.Layout.Add( new Checkbox( "Reload: reach for this weapon's magazine", card ) { Value = s.ReloadTouchesMagazine } );
		mag.ToolTip = "Off: during third-person reloads the support hand plays the character's own reload and returns to the grip. On: it is moved onto this weapon's magazine where the reload works the magazine.";
		mag.StateChanged = state =>
		{
			if ( s.ReloadTouchesMagazine == mag.Value )
				return;
			s.ReloadTouchesMagazine = mag.Value;
			foreach ( var track in s.Contacts.Values.Where( t => t.Generated ) )
				track.Keys.Clear();
			var session = C.Session;
			_ = C.RunAsync( "Planning the reload", ( p, c ) => session.ReplanContactsAsync( c ), heavy: false );
		};
	}

	private void SetHold( int hold, bool manual )
	{
		var s = C.Setup;
		if ( s.HoldTypeManual == manual && (!manual || s.HoldType == hold) )
			return;
		s.HoldTypeManual = manual;
		s.HoldType = manual ? hold : -1;
		var session = C.Session;
		ForceRebuild();
		_ = C.RunAsync( "Changing the hold", async ( p, c ) =>
		{
			await session.PrepareCharacterAsync( p, c );
			await session.ResolveGripAsync( p, c );
		} );
	}

	private void BuildRegions()
	{
		var regions = GripRegions.Of( C.Analysis );
		if ( regions.Count == 0 )
			return;
		var card = AddCard( "select_all", "Regions", out _, "Parts of the weapon the hands use or must keep clear of. Click a row to show it on the weapon." );
		foreach ( var region in regions )
		{
			var kind = region.Kind;
			var row = card.Layout.Add( new ClickRow( card, () =>
			{
				C.HighlightRegion = C.HighlightRegion == kind ? null : kind;
				MarkDirty();
			}, $"{GripRegion.Label( kind )}: {region.Reason}" ) );
			row.Layout.Add( new ColorDot( row, WeaponViewport.RegionColor( kind ) ) );
			row.Layout.Add( new Label( GripRegion.Label( kind ), row ) { FixedWidth = 110, FixedHeight = UiStyle.ControlHeight, Alignment = TextFlag.LeftCenter } );
			// Wraps: a non-wrapping label is as wide as its text and would widen the whole page.
			row.Layout.Add( UiStyle.Muted( new Label( region.Reason, row ) { WordWrap = true, MinimumHeight = UiStyle.ControlHeight, Alignment = TextFlag.LeftCenter, MinimumWidth = 20 }, small: true ), 1 );
			Bind( () => row.Selected = C.HighlightRegion == kind );
		}
	}

	/// <summary>A small colour swatch (region colours match the preview).</summary>
	private sealed class ColorDot : Widget
	{
		private readonly Color _color;

		public ColorDot( Widget parent, Color color ) : base( parent )
		{
			_color = color;
			FixedSize = new Vector2( 10, UiStyle.ControlHeight );
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( _color );
			Paint.DrawRect( new Rect( 0, Height * 0.5f - 4, 8, 8 ), 4 );
		}
	}

	private void BuildLibrary()
	{
		var card = AddCard( "collections_bookmark", "Grip library", out var header, "Grips captured from first-person animations; they compete in Auto Grip and can be picked per hand" );
		header.AddStretchCell();
		header.Add( new Pill( card, $"{C.Session.Grips.Count} GRIPS", Theme.TextLight ) );
		var row = card.Layout.AddRow();
		row.Add( UiStyle.Secondary( card, "Add grips from animation files…", "library_add", AddGrips, "Pick FBX / GLB / glTF files whose arms hold a weapon: their grips are added to this project's library" ) );
		row.AddStretchCell();
		row.Add( UiStyle.Icon( card, "psychology", ShowGrabNet, "GrabNet: export this weapon for the grasp model, or load the grasps it generated" ) );
	}

	private void ShowGrabNet()
	{
		var menu = new Menu( this );
		menu.AddHeading( "GrabNet grasps" );
		menu.AddOption( "Export weapon for GrabNet…", "file_download", ExportGrabNet );
		menu.AddOption( "Load GrabNet grasps…", "file_upload", LoadGrabNet );
		menu.OpenAtCursor();
	}

	private void ExportGrabNet()
	{
		var session = C.Session;
		var path = EditorUtility.SaveFileDialog( "Weapon for GrabNet (metres)", "obj", $"{session.Asset?.Name ?? "weapon"}.obj" );
		if ( string.IsNullOrEmpty( path ) )
			return;
		try
		{
			session.ExportForGrabNet( path );
			C.SetStatus( $"Exported {System.IO.Path.GetFileName( path )}: run GrabNet on it, then load its grasps (21 keypoints per hand, same frame).", Theme.Green );
		}
		catch ( Exception e )
		{
			C.SetStatus( $"Export failed: {e.Message}", Theme.Red );
		}
	}

	private void LoadGrabNet()
	{
		var session = C.Session;
		var path = EditorUtility.OpenFileDialog( "GrabNet grasps", "json", null );
		if ( string.IsNullOrEmpty( path ) )
			return;
		ForceRebuild();
		_ = C.RunAsync( "Reading GrabNet grasps", async ( p, c ) =>
		{
			var (added, skipped) = await session.AddLearnedGraspsAsync( path, c );
			C.SetStatus( skipped.Count == 0 ? $"Added {added} GrabNet grips; they compete in Auto Grip." : $"Added {added} GrabNet grips; skipped {string.Join( "; ", skipped )}.", added > 0 ? (skipped.Count == 0 ? Theme.Green : Theme.Yellow) : Theme.Red );
			C.ScheduleResolve( 0.01f );
		}, heavy: false );
	}

	private void AddGrips()
	{
		var dialog = new FileDialog( null ) { Title = "Animation files with arms holding a weapon" };
		dialog.SetFindExistingFiles();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Models (*.fbx *.glb *.gltf)" );
		if ( !dialog.Execute() )
			return;
		var files = dialog.SelectedFiles.ToList();
		if ( files.Count == 0 )
			return;
		ForceRebuild();
		_ = C.RunAsync( "Reading grips", async ( p, c ) =>
		{
			var (added, skipped) = await GripLibraryStore.AddAsync( files, p, c );
			C.SetStatus( skipped.Count == 0 ? $"Added {added} grips to the library." : $"Added {added} grips; no hands on a weapon in {string.Join( ", ", skipped.Select( System.IO.Path.GetFileName ) )}.", skipped.Count == 0 ? Theme.Green : Theme.Yellow );
			C.ScheduleResolve( 0.01f );
		}, heavy: false );
	}

	private void AutoFit( Side? side )
	{
		var session = C.Session;
		ForceRebuild();
		var what = side switch { Side.Right => "right hand", Side.Left => "left hand", _ => "both hands" };
		_ = C.RunAsync( $"Fitting the {what}", ( p, c ) => session.AutoFitAsync( side ), heavy: false );
	}

	// ------------------------------------------------------------------ hand cards

	/// <summary>How far one Move click moves a hand.</summary>
	private const float MoveInches = 0.1f;

	private void BuildHand( Side side )
	{
		var s = C.Setup;
		var right = side == Side.Right;
		var card = AddCard( right ? "back_hand" : "front_hand", right ? "Right hand" : "Left hand", out var header,
			right ? "The firing hand" : "The support hand" );
		header.AddStretchCell();
		var quality = header.Add( new Pill( card, "", Theme.TextLight ) );

		if ( !right )
		{
			var enabled = card.Layout.Add( new Checkbox( "Use the support hand", card ) { Value = s.UseSupportHand, ToolTip = "Off makes the weapon one-handed: the left arm keeps its animation" } );
			enabled.Toggled = () => Safe( () =>
			{
				C.Setup.UseSupportHand = enabled.Value;
				if ( enabled.Value && C.Setup.Support is null )
				{
					AutoFit( Side.Left );
					return;
				}
				C.MarkChanged();
				C.ScheduleResolve( 0.01f );
			} );
			if ( !s.UseSupportHand )
			{
				quality.Visible = false;
				return;
			}
		}

		var grip = right ? s.Primary : s.Support;
		var styleRow = UiStyle.FieldRow( card, card.Layout, "Grip style", "How the hand closes around the surface" );
		var style = Combo( card, styleRow, "How the hand closes around the surface" );
		foreach ( var st in Enum.GetValues<GripStyle>() )
		{
			var value = st;
			style.AddItem( st.ToString(), StyleIcon( st ), () => SetStyle( side, value ), description: StyleHelp( st ), selected: grip is not null && grip.Style == st );
		}
		if ( grip is not null && !grip.Manual )
			styleRow.Add( new ConfidencePill( card, grip.Confidence, false, grip.Reason ) );
		else if ( grip is { Manual: true } )
			styleRow.Add( new ConfidencePill( card, 1f, true ) );

		// One dropdown for where the grip comes from: automatic (every grip competes), a grip from the
		// library (captured from first-person animations), the procedural fit, or a spot picked on the weapon.
		var target = right ? PickTarget.RightGrip : PickTarget.LeftGrip;
		var gripRow = UiStyle.FieldRow( card, card.Layout, "Grip", "Where this hand's grip comes from" );
		var source = Combo( card, gripRow, "Where this hand's grip comes from. Pick a grip, then fine-tune it below or in the viewport." );
		var chosen = grip?.Preset ?? "";
		source.AddItem( "Auto (best fit)", "auto_fix_high", () => SetPreset( side, "" ), description: "Every grip below competes; the one that fits this weapon and animation best wins", selected: chosen == "" );
		foreach ( var preset in C.Session.Grips.Where( p => p.Side == side ) )
		{
			var name = preset.Name;
			source.AddItem( name, name.StartsWith( ImportSession.OwnGripSource ) ? "star" : "front_hand", () => SetPreset( side, name ), description: preset.Source, selected: chosen == name );
		}
		source.AddItem( "Procedural fit", "gesture", () => SetPreset( side, ChoiceGripGenerator.Procedural ), description: "Close the fingers around the surface from scratch", selected: chosen == ChoiceGripGenerator.Procedural );
		source.AddItem( "Pick on the weapon…", "ads_click", () => C.BeginPick( target ), description: "Then click on the weapon where this hand should hold it" );

		// Move just the hand (grip shape, fingers and wrist angle unchanged). Dragging the hand's
		// dot in the preview slides it along the surface instead.
		var moveRow = UiStyle.FieldRow( card, card.Layout, "Move", "Move only this hand on the weapon; everything else stays as it is. Auto Grip undoes it." );
		foreach ( var (icon, delta, tip) in new[]
		{
			("arrow_forward", new System.Numerics.Vector3( MoveInches, 0, 0 ), "Forward, toward the muzzle"),
			("arrow_back", new System.Numerics.Vector3( -MoveInches, 0, 0 ), "Back, toward the stock"),
			("arrow_upward", new System.Numerics.Vector3( 0, 0, MoveInches ), "Up, toward the top of the gun"),
			("arrow_downward", new System.Numerics.Vector3( 0, 0, -MoveInches ), "Down"),
			("west", new System.Numerics.Vector3( 0, MoveInches, 0 ), "To the weapon's left"),
			("east", new System.Numerics.Vector3( 0, -MoveInches, 0 ), "To the weapon's right"),
		} )
		{
			var d = delta;
			moveRow.Add( UiStyle.Icon( card, icon, () => C.MoveHand( side, d ), $"{tip} ({MoveInches} in)" ) );
		}
		moveRow.AddStretchCell();

		var surface = UiStyle.FieldRow( card, card.Layout, "Surface", "The part of the weapon this hand holds, as measured" ).Add( UiStyle.Muted( new Label( "", card ) { WordWrap = true, MinimumWidth = 20 }, small: true ), 1 );
		var detail = UiStyle.FieldRow( card, card.Layout, "Grasp", "Fingers touching the weapon, fingers in the air, and how deep the hand passes into it" ).Add( UiStyle.Muted( new Label( "", card ) { WordWrap = true, MinimumWidth = 20 }, small: true ), 1 );
		Bind( () =>
		{
			var candidate = right ? C.RightSurface : C.LeftSurface;
			surface.Text = candidate?.Surface is { } g
				? $"radius {g.Radius:0.00} in · {g.Depth:0.0} × {g.Width:0.0} in · clearance {g.Clearance:0.0} in"
				: "not measured";
			var solution = C.Session.Grip;
			var q = right ? solution?.RightQuality : solution?.LeftQuality;
			var (text, color, info) = Describe( q );
			quality.Set( text, color );
			quality.ToolTip = info;
			var from = right ? solution?.RightSource : solution?.LeftSource;
			detail.Text = string.IsNullOrEmpty( from ) ? info : $"{info} · {ShortSource( from )}";
			if ( !right && solution is { Left: not null } && !solution.LeftReach.Reached )
				detail.Text += $" · {solution.LeftReach.Shortfall:0.0} in out of reach";
		} );
	}

	/// <summary>"Procedural roll 8° slide 0.3" reads as "procedural fit"; library grips keep their name.</summary>
	private static string ShortSource( string source ) => source.StartsWith( "Procedural" ) ? "procedural fit" : source == "Edited" ? "edited" : source;

	private void SetPreset( Side side, string preset )
	{
		var grip = side == Side.Right ? C.Setup.Primary : C.Setup.Support;
		if ( grip is null || grip.Preset == preset )
			return;
		grip.Preset = preset;
		// A different grip replaces the edited pose.
		grip.Pose = null;
		C.MarkChanged();
		C.ScheduleResolve( 0.01f, side );
	}

	private void SetStyle( Side side, GripStyle style )
	{
		var grip = side == Side.Right ? C.Setup.Primary : C.Setup.Support;
		if ( grip is null || grip.Style == style )
			return;
		grip.Style = style;
		grip.Manual = true;
		grip.Pose = null;
		C.MarkChanged();
		C.ScheduleResolve( 0.01f, side );
	}

	/// <summary>Pill text, colour and a readable detail line for a grasp.</summary>
	public static (string Text, Color Color, string Detail) Describe( GraspQuality q )
	{
		if ( q is null )
			return ("NOT FITTED", Theme.TextLight, "No fit yet.");
		var penetration = MathF.Max( q.MaxPenetration, q.PalmPenetration );
		var detail = $"{q.TouchingFingers} touching · {q.FloatingFingers} floating · {penetration:0.00} in deep";
		// A little finger clipping into a grip reads fine in game; only deep passes are errors.
		if ( penetration > Validation.SeverePenetration * 2f )
			return ("PENETRATING", Theme.Red, detail);
		if ( penetration > Validation.SeverePenetration )
			return ("CLIPPING", Theme.Yellow, detail);
		if ( q.TouchingFingers < 2 || q.FloatingFingers >= 3 )
			return ("LOOSE", Theme.Yellow, detail);
		if ( penetration > 0.2f || q.FloatingFingers >= 2 || q.HandOverlap > 0.3f )
			return ("FAIR", Theme.Yellow, detail);
		return ("GOOD GRIP", Theme.Green, detail);
	}

	private static string StyleIcon( GripStyle style ) => style switch
	{
		GripStyle.Wrap => "back_hand",
		GripStyle.Cradle => "front_hand",
		GripStyle.Pump => "sync_alt",
		GripStyle.Overlay => "join_inner",
		GripStyle.Handle => "sports_mma",
		_ => "pan_tool",
	};

	private static string StyleHelp( GripStyle style ) => style switch
	{
		GripStyle.Wrap => "Fingers wrap a vertical handle: pistol grip, vertical foregrip",
		GripStyle.Cradle => "Palm under a horizontal handguard, fingers up the side",
		GripStyle.Pump => "Hand around a shotgun pump or forend",
		GripStyle.Overlay => "Support hand cupping the firing hand (pistols)",
		GripStyle.Handle => "Melee handle held in a fist",
		_ => "",
	};

	// ------------------------------------------------------------------ trigger

	private void BuildTrigger()
	{
		var card = AddCard( "touch_app", "Trigger finger", out var header, "Where the right index finger rests" );
		header.AddStretchCell();
		var seg = header.Add( new SegmentedControl( card ) { FixedHeight = 26, FixedWidth = 200, ToolTip = "On the trigger, or straight along the frame (trigger discipline)" } );
		seg.AddOption( "On trigger", "touch_app" );
		seg.AddOption( "Along frame", "straighten" );
		seg.SelectedIndex = C.Setup.IndexOnTrigger ? 0 : 1;
		seg.OnSelectedChanged = _ => Safe( () =>
		{
			var on = seg.SelectedIndex == 0;
			if ( C.Setup.IndexOnTrigger == on )
				return;
			C.Setup.IndexOnTrigger = on;
			C.MarkChanged();
			C.ScheduleResolve( 0.01f );
		} );
	}

	// ------------------------------------------------------------------ fine tune

	private void BuildFineTune()
	{
		var s = C.Setup;
		var card = AddCard( "tune", "Fine tune", out var header, "Small corrections on top of the automatic fit" );
		header.AddStretchCell();
		header.Add( UiStyle.Icon( card, "restart_alt", () => Safe( () =>
		{
			var ik = C.Setup.Ik;
			ik.WristPreference = new IkSettings().WristPreference;
			ik.WeaponOffsetPosition = new float[3];
			ik.WeaponOffsetRotation = new float[] { 0, 0, 0, 1 };
			ForceRebuild();
			C.LiveRefit();
		} ), "Reset the fine tuning" ) );

		Slider( card, card.Layout, "Wrist vs aim", "Left: the weapon aims exactly along the character's view. Right: keep the animated wrist angle.",
			0f, 1f, s.Ik.WristPreference, v => $"{v * 100f:0}%", v =>
			{
				C.Setup.Ik.WristPreference = v;
				C.LiveRefit();
			} );

		card.Layout.Add( new SectionHeader( card, "Weapon nudge" ) );
		var axes = new[] { ("Forward", "Move the weapon forward/back in the hand (inches)"), ("Left", "Move the weapon left/right in the hand (inches)"), ("Up", "Move the weapon up/down in the hand (inches)") };
		for ( var i = 0; i < 3; i++ )
		{
			var axis = i;
			Slider( card, card.Layout, axes[i].Item1, axes[i].Item2, -2f, 2f, s.Ik.WeaponOffsetPosition[i], v => $"{v:+0.00;-0.00;0.00} in", v =>
			{
				C.Setup.Ik.WeaponOffsetPosition[axis] = v;
				C.LiveRefit();
			} );
		}
		var angles = V.Q( s.Ik.WeaponOffsetRotation ).ToEngine().Angles();
		var euler = new[] { angles.pitch, angles.yaw, angles.roll };
		var names = new[] { ("Pitch", "Tilt the muzzle up/down"), ("Yaw", "Turn the muzzle left/right"), ("Roll", "Cant the weapon") };
		for ( var i = 0; i < 3; i++ )
		{
			var index = i;
			Slider( card, card.Layout, names[i].Item1, names[i].Item2, -15f, 15f, Math.Clamp( euler[i], -15f, 15f ), v => $"{v:+0.0;-0.0;0.0}°", v =>
			{
				euler[index] = v;
				C.Setup.Ik.WeaponOffsetRotation = V.A( Rotation.From( euler[0], euler[1], euler[2] ).ToCore() );
				C.LiveRefit();
			} );
		}
	}

	// ------------------------------------------------------------------ fingers

	private void BuildFingers()
	{
		var rig = C.Session.Character;
		if ( rig is null )
			return;
		var card = AddCard( "front_hand", "Fingers", out var header, "Open or close single fingers of the fitted hand" );
		header.AddStretchCell();
		var seg = header.Add( new SegmentedControl( card ) { FixedHeight = 26, FixedWidth = 150, ToolTip = "Which hand the curl sliders edit" } );
		seg.AddOption( "Right", "back_hand" );
		if ( rig.Left is not null && C.Setup.UseSupportHand )
			seg.AddOption( "Left", "front_hand" );
		else
			_curlSide = Side.Right;
		seg.SelectedIndex = _curlSide == Side.Right ? 0 : 1;
		seg.OnSelectedChanged = _ =>
		{
			var side = seg.SelectedIndex == 0 ? Side.Right : Side.Left;
			if ( side == _curlSide )
				return;
			_curlSide = side;
			ForceRebuild();
		};

		var hand = _curlSide == Side.Right ? rig.Right : rig.Left;
		foreach ( var finger in hand.Fingers )
		{
			var kind = finger.Kind;
			// The character's model copies the ring onto the pinky: one slider moves both.
			if ( hand.PinkyFollowsRing && kind == FingerKind.Pinky )
				continue;
			var together = hand.PinkyFollowsRing && kind == FingerKind.Ring;
			var label = together ? "Ring & pinky" : kind.ToString();
			var tip = together
				? "Curl of the ring and little fingers: left opens them, right closes them. This character's model copies the ring finger onto the pinky, so they move together."
				: $"Curl of the {kind.ToString().ToLowerInvariant()}: left opens it, right closes it";
			Slider( card, card.Layout, label, tip, 0f, 1.6f, 1f, v => $"{v * 100f:0}%", v => Curl( kind, v ) );
		}
		card.Layout.Add( UiStyle.Muted( new Label( "100% is the automatic fit. Changes are kept as a hand edit.", card ) { WordWrap = true }, small: true ) );
	}

	private void Curl( FingerKind kind, float factor )
	{
		var solution = C.Session.Grip;
		var side = _curlSide;
		_curlBase ??= (side == Side.Right ? solution?.Right : solution?.Left)?.Clone();
		if ( _curlBase is null )
			return;
		_curl[kind] = factor;
		var pose = _curlBase.Clone();
		foreach ( var (k, f) in _curl )
		{
			if ( !_curlBase.Flex.TryGetValue( k, out var flex ) )
				continue;
			pose.Flex[k] = flex.Select( x => x * f ).ToArray();
		}
		C.LiveRefit( side, pose );
	}

	public override void OnDestroyed()
	{
		C.PickChanged -= MarkDirty;
		base.OnDestroyed();
	}
}
