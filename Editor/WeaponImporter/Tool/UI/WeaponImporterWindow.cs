using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Setup;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// The Weapon Importer tool window. First the drop page (drop a weapon or pick one); once the
/// weapon is loaded, the editor: pages on the left, the live preview in the middle with the
/// timeline under it, and the selected page's properties on the right.
/// </summary>
public sealed partial class WeaponImporterWindow : Widget
{
	public const string DockTitle = "Weapon Importer";
	public const string DockIcon = "back_hand";

	private static WeaponImporterWindow _instance;
	public static WeaponImporterWindow Instance => _instance.IsValid() ? _instance : null;

	public ImporterController Controller { get; } = new();

	private DropArea _drop;
	private ProcessingIndicator _firstLoad;
	private Widget _main;

	private Label _weaponName;
	private Pill _typePill;
	private SegmentedControl _mode;
	private IconButton _autoSetup;
	private Button _bake;

	private NavList _nav;
	private ScrollArea _pageScroll;

	/// <summary>The scrolling page column (layout checks).</summary>
	internal ScrollArea PageScroll => _pageScroll;
	private readonly List<StepPanel> _steps = new();

	private Card _viewportCard;
	private Widget _viewportHost;
	private WeaponViewport _viewport;
	private ProcessingIndicator _viewportLoader;
	private TimelineCard _timeline;

	private StatusDot _statusDot;

	/// <summary>Whether the bottom status line is showing (hidden on the start screen).</summary>
	public bool StatusLineVisible => _status.IsValid() && _status.Visible;
	private Label _status;
	private UiButton _cancel;

	/// <summary>The editor pages, in order (also keys 1–5).</summary>
	private static readonly (string Icon, string Title, string Tip)[] Pages =
	{
		("category", "Weapon", "Type, orientation, parts, muzzle and shell eject"),
		("back_hand", "Grip", "Where the hands hold the weapon"),
		("movie", "Animations", "Which clip plays for each action"),
		("texture", "Materials", "Textures of the weapon's materials"),
		("inventory_2", "Export", "Checks and bake"),
	};

	public WeaponImporterWindow( Widget parent ) : base( parent )
	{
		if ( !_instance.IsValid() )
			_instance = this;
		Name = "WeaponImporter";
		WindowTitle = DockTitle;
		SetWindowIcon( DockIcon );
		MinimumSize = new Vector2( 900, 560 );
		AcceptDrops = true;
		// The first import otherwise pays for loading the analysis code (a two second stall).
		_ = Task.Run( WeaponImporter.Core.Warmup.Run );
		FocusMode = FocusMode.Click;
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 10;
		Build();

		Controller.Changed += OnChanged;
		Controller.SessionReplaced += OnSessionReplaced;
		Controller.BusyChanged += OnBusyChanged;
		Controller.StatusChanged += OnStatusChanged;
		Controller.RoleChanged += OnRoleChanged;
		OnSessionReplaced();
		OnStatusChanged();
	}

	// ------------------------------------------------------------------ registration

	[Event( "tools.editorwindow.createview" )]
	private static void RegisterViewMenu( Menu menu ) => EditorWindow.DockManager.RegisterDockType( new DockManager.DockInfo
	{
		Title = DockTitle,
		Icon = DockIcon,
		CreateAction = () =>
		{
			Open();
			return null;
		},
	} );

	[Event( "tools.editorwindow.postcreateview" )]
	private static void ConfigureViewMenu( Menu menu )
	{
		var option = menu.GetOption( DockTitle );
		if ( option is null )
			return;
		option.Toggled = null;
		option.Checkable = false;
		option.Triggered = () => Open();
	}

	[Event( "asset.contextmenu", Priority = 60 )]
	private static void OnAssetContextMenu( AssetContextMenu e )
	{
		if ( e.SelectedList is not { Count: 1 } )
			return;
		var path = e.SelectedList[0].AbsolutePath;
		if ( DropPaths.IsSetupFile( path ) )
			e.Menu.AddOption( "Open Weapon Setup", DockIcon, () => Open().ImportFile( path ) );
		else if ( DropPaths.IsWeaponFile( path ) )
			e.Menu.AddOption( "Import as Weapon…", DockIcon, () => Open().ImportFile( path ) );
	}

	public static WeaponImporterWindow Open()
	{
		if ( Instance is { } existing )
		{
			existing.GetWindow().Show();
			existing.Show();
			existing.GetWindow().Raise();
			return existing;
		}
		var dialog = new Dialog( null );
		dialog.Window.Title = DockTitle;
		dialog.Window.SetWindowIcon( DockIcon );
		dialog.Layout = Layout.Column();
		var window = dialog.Layout.Add( new WeaponImporterWindow( dialog ), 1 );
		dialog.Window.MinimumSize = new Vector2( 900, 560 );
		dialog.Window.Size = new Vector2( 1340, 800 );
		dialog.Show();
		return window;
	}

	// ------------------------------------------------------------------ layout

	private void Build()
	{
		// The first page: unchanged from the previous importer (drop area, three buttons, loader).
		_drop = Layout.Add( new DropArea( this, ImportFile, () => ChooseFile(), ChooseFromDisk, UseTemplate ), 1 );
		_firstLoad = Layout.Add( new ProcessingIndicator( this ) { Visible = false }, 1 );

		_main = Layout.Add( new Widget( this ) { Visible = false }, 1 );
		_main.Layout = Layout.Column();
		_main.Layout.Spacing = 10;
		BuildHeader( _main.Layout );

		var body = _main.Layout.AddRow( 1 );
		body.Spacing = 10;

		// Pages: clickable rows.
		var navColumn = body.AddColumn();
		_nav = navColumn.Add( new NavList( _main ) );
		foreach ( var (icon, title, tip) in Pages )
			_nav.Add( icon, title, tip );
		_nav.OnSelected = SelectStep;
		navColumn.AddStretchCell();

		// Preview + timeline | page properties.
		var split = body.Add( new Splitter( _main ) { IsHorizontal = true }, 1 );
		var center = new Widget( split );
		center.Layout = Layout.Column();
		center.Layout.Spacing = 10;
		BuildViewportCard( center );
		_timeline = center.Layout.Add( new TimelineCard( center, Controller ) );
		split.AddWidget( center );

		var right = new PagePane( split );
		right.Layout = Layout.Column();
		right.Layout.Margin = new Sandbox.UI.Margin( 6, 0, 0, 0 );
		_pageScroll = right.Layout.Add( new ScrollArea( right ), 1 );
		_pageScroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		var canvas = new Widget( _pageScroll );
		canvas.Layout = Layout.Column();
		canvas.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 12, 0 ); // clear of the overlay scrollbar
		canvas.Layout.Spacing = 10;
		_steps.Add( canvas.Layout.Add( new WeaponStep( canvas, Controller ) ) );
		_steps.Add( canvas.Layout.Add( new HandsStep( canvas, Controller ) ) );
		_steps.Add( canvas.Layout.Add( new AnimationsStep( canvas, Controller ) ) );
		_steps.Add( canvas.Layout.Add( new MaterialsStep( canvas, Controller ) ) );
		_steps.Add( canvas.Layout.Add( new CheckStep( canvas, Controller ) ) );
		canvas.Layout.AddStretchCell();
		_pageScroll.Canvas = canvas;
		split.AddWidget( right );
		split.SetStretch( 0, 1 );
		split.SetStretch( 1, 0 );
		split.SetCollapsible( 0, false );
		split.SetCollapsible( 1, false );

		// Status line: a light (working, ready, error) and what is happening, with Cancel beside it.
		var status = Layout.AddRow();
		status.Spacing = 8;
		_statusDot = status.Add( new StatusDot( this ) );
		// Fixed height and one line: if this row ever changed height (a wrapped message, Cancel
		// appearing) the preview above would resize and re-create its render surface, stalling
		// the editor for seconds. Long messages are elided; the full text is the tooltip.
		_status = status.Add( new Label( "", this ) { WordWrap = false, FixedHeight = UiStyle.ControlHeight, MinimumWidth = 40 }, 1 );
		_cancel = status.Add( new UiButton( this, "Cancel", "cancel", () => Controller.Cancel(), "Stop the running operation" ) { Visible = false } );

		SelectStep( 0 );
	}

	private void BuildHeader( Layout parent )
	{
		var header = parent.AddRow();
		header.Spacing = 8;
		_weaponName = header.Add( new Label( "", this ) );
		_weaponName.SetStyles( "font-weight: 600; font-size: 15px;" );
		_typePill = header.Add( new Pill( this, "", Theme.TextLight ) );
		header.AddStretchCell();

		// The two ways of working with a weapon.
		_mode = header.Add( new SegmentedControl( this ) { FixedHeight = 30, FixedWidth = 280, ToolTip = "First person: the weapon's own animation (and arms). Third person: a character holds it." } );
		_mode.AddOption( "First Person", "visibility" );
		_mode.AddOption( "Third Person", "accessibility_new" );
		_mode.SelectedIndex = 1;
		_mode.OnSelectedChanged = _ => SetFirstPerson( _mode.SelectedIndex == 0 );
		header.AddStretchCell();

		header.Add( UiStyle.Icon( this, "file_open", () => ChooseFile(), "Import another weapon (or drop one anywhere on this window)", 30 ) );
		_autoSetup = header.Add( UiStyle.Icon( this, "auto_fix_high", AutoSetup, "Auto setup: set the weapon up again from scratch (clears manual edits)", 30 ) );
		_bake = header.Add( UiStyle.Primary( "Bake", "inventory_2", () => _ = Controller.BakeAsync(), "Write and compile the weapon model and a ready prefab" ) );
		_bake.MinimumWidth = 96;
	}

	private static readonly (ViewportCamera Mode, string Label, string Icon, string Tip)[] Cameras =
	{
		(ViewportCamera.Orbit, "Orbit", "3d_rotation", "Orbit freely: left drag rotates, right drag pans, wheel zooms"),
		(ViewportCamera.Gameplay, "Gameplay", "videogame_asset", "Over the shoulder, like the game camera"),
		(ViewportCamera.Front, "Front", "person", "Straight at the weapon from the front"),
		(ViewportCamera.Side, "Side", "switch_left", "The weapon side on, from the right"),
	};

	private ComboBox _cameraCombo;
	private ComboBox _characterCombo;
	private SegmentedControl _compare;
	private ViewportCamera _thirdPersonCamera = ViewportCamera.Orbit;
	private bool _syncingHeader;

	private void BuildViewportCard( Widget parent )
	{
		_viewportCard = parent.Layout.Add( new Card( parent ), 1 );
		_viewportCard.Layout.Spacing = 8;
		var bar = _viewportCard.Layout.AddRow();
		bar.Spacing = 6;

		_cameraCombo = bar.Add( UiStyle.Framed( new ComboBox( _viewportCard ) { FixedWidth = 132, ToolTip = "Camera" } ) );
		foreach ( var (mode, label, icon, tip) in Cameras )
		{
			var m = mode;
			_cameraCombo.AddItem( label, icon, () =>
			{
				if ( _syncingHeader || !_viewport.IsValid() )
					return;
				_thirdPersonCamera = m;
				_viewport.SetCamera( m );
			}, description: tip, selected: mode == ViewportCamera.Orbit );
		}

		_characterCombo = bar.Add( UiStyle.Framed( new ComboBox( _viewportCard ) { FixedWidth = 150, ToolTip = "Character the hands are fitted to; the prefab works with any character using the same skeleton" } ) );
		FillCharacters( "human" );

		_compare = bar.Add( new SegmentedControl( _viewportCard ) { FixedHeight = UiStyle.ControlHeight, FixedWidth = 190, ToolTip = "Original: the character's animation as authored. Corrected: hands on this weapon." } );
		_compare.AddOption( "Original", "history" );
		_compare.AddOption( "Corrected", "back_hand" );
		_compare.SelectedIndex = 1;
		_compare.OnSelectedChanged = _ => Controller.ShowOriginal = _compare.SelectedIndex == 0;

		bar.AddStretchCell();
		bar.Add( UiStyle.Icon( _viewportCard, "layers", ShowLayers, "Show in the preview: arms, contacts, IK targets, attachments, normals" ) );
		bar.Add( UiStyle.Icon( _viewportCard, "center_focus_strong", () => _viewport?.FrameWeapon(), "Frame the weapon (F)" ) );

		_viewportHost = _viewportCard.Layout.Add( new Widget( _viewportCard ), 1 );
		_viewportHost.Layout = Layout.Column();
		_viewport = _viewportHost.Layout.Add( new WeaponViewport( _viewportHost, Controller ), 1 );
		_viewport.CameraModeChanged += SyncHeader;
		_viewportLoader = _viewportHost.Layout.Add( new ProcessingIndicator( _viewportHost ) { Visible = false }, 1 );
	}

	/// <summary>Preview layers in one dropdown instead of a row of toggle buttons.</summary>
	private void ShowLayers()
	{
		var menu = new Menu( this );
		void Layer( string title, string icon, Func<bool> get, Action<bool> set )
		{
			var option = menu.AddOption( title, icon, () => set( !get() ) );
			option.Checkable = true;
			option.Checked = get();
		}
		Layer( "Arms", "accessibility", () => Controller.ShowSkeleton, v => Controller.ShowSkeleton = v );
		Layer( "Contacts", "touch_app", () => Controller.ShowContacts, v => Controller.ShowContacts = v );
		Layer( "IK targets", "my_location", () => Controller.ShowIkTargets, v => Controller.ShowIkTargets = v );
		Layer( "Muzzle and eject", "push_pin", () => Controller.ShowAttachments, v => Controller.ShowAttachments = v );
		Layer( "Surface normals", "north_east", () => Controller.ShowNormals, v => Controller.ShowNormals = v );
		Layer( "Regions", "select_all", () => Controller.ShowRegions, v => Controller.ShowRegions = v );
		menu.OpenAtCursor();
	}

	/// <summary>First person: the weapon's own animation (and arms); third person: the character.</summary>
	public void SetFirstPerson( bool on )
	{
		if ( !_viewport.IsValid() )
			return;
		if ( on == _viewport.FirstPerson )
			return;
		_viewport.SetCamera( on ? ViewportCamera.FirstPerson : _thirdPersonCamera );
		SyncHeader();
	}

	/// <summary>Keeps the header controls on the current mode and camera without re-triggering them.</summary>
	private void SyncHeader()
	{
		if ( !_cameraCombo.IsValid() || !_viewport.IsValid() )
			return;
		_syncingHeader = true;
		try
		{
			var firstPerson = _viewport.FirstPerson;
			if ( _mode.SelectedIndex != (firstPerson ? 0 : 1) )
				_mode.SelectedIndex = firstPerson ? 0 : 1;
			// Third-person controls only make sense with the character.
			Show( _cameraCombo, !firstPerson );
			Show( _characterCombo, !firstPerson );
			Show( _compare, !firstPerson );
			var camera = Array.FindIndex( Cameras, c => c.Mode == _viewport.CameraMode );
			if ( camera >= 0 )
			{
				_thirdPersonCamera = Cameras[camera].Mode;
				if ( _cameraCombo.CurrentIndex != camera )
					_cameraCombo.CurrentIndex = camera;
			}
			var current = string.IsNullOrEmpty( Controller.Setup?.Character ) ? "human" : Controller.Setup.Character;
			if ( _characterIds.IndexOf( current ) is var character && character < 0 )
			{
				FillCharacters( current );
				character = _characterIds.IndexOf( current );
			}
			if ( character >= 0 && _characterCombo.CurrentIndex != character )
				_characterCombo.CurrentIndex = character;
		}
		finally
		{
			_syncingHeader = false;
		}
	}

	// Character ids in the order of the character list (built-ins, then a custom model).
	private readonly List<string> _characterIds = new();

	/// <summary>The built-in characters, <paramref name="current"/> when it is a custom model, and "Custom model…".</summary>
	private void FillCharacters( string current )
	{
		var wasSyncing = _syncingHeader;
		_syncingHeader = true;
		try
		{
			_characterCombo.Clear();
			_characterIds.Clear();
			var entries = ImporterController.Characters.Select( c => (c.Id, c.Label) ).ToList();
			if ( entries.All( e => e.Id != current ) )
				entries.Add( (current, CharacterLibrary.Label( current )) );
			foreach ( var (id, label) in entries )
			{
				var value = id;
				_characterIds.Add( id );
				_characterCombo.AddItem( label, "person", () => { if ( !_syncingHeader ) Controller.SetCharacter( value ); },
					description: ImporterController.Characters.Any( c => c.Id == id ) ? null : id, selected: id == current );
			}
			_characterIds.Add( "" );
			_characterCombo.AddItem( "Custom model…", "folder_open", () => { if ( !_syncingHeader ) PickCharacter(); },
				description: "Any humanoid using the citizen or human animgraph" );
		}
		finally
		{
			_syncingHeader = wasSyncing;
		}
	}

	private void PickCharacter()
	{
		var picker = AssetPicker.Create( this, AssetType.Model );
		picker.Title = "Character model (citizen or human animgraph)";
		picker.OnAssetPicked = assets =>
		{
			if ( assets?.FirstOrDefault() is { } asset )
				Controller.SetCharacter( asset.Path );
			SyncHeader();
		};
		picker.Show();
		// Until something is picked the list shows the current character.
		SyncHeader();
	}

	public int CurrentStep { get; private set; }

	public void SelectStep( int index )
	{
		index = Math.Clamp( index, 0, _steps.Count - 1 );
		CurrentStep = index;
		if ( _nav.Selected != index )
			_nav.Select( index, notify: false );
		for ( var i = 0; i < _steps.Count; i++ )
			_steps[i].Visible = i == index;
		_steps[index].MarkDirty();
		_steps[index].Flush();
		if ( index == 2 && Controller.Role == AnimationRole.Idle && Controller.Setup is not null )
			Controller.RestartRequested = true;
	}

	/// <summary>Scrolls the page column so its last card is visible.</summary>
	public void ScrollStepToEnd()
	{
		var last = CurrentPanel.Children.LastOrDefault();
		if ( last.IsValid() )
			_pageScroll.MakeVisible( last );
	}

	/// <summary>The page currently shown (for the gate).</summary>
	public StepPanel CurrentPanel => _steps[CurrentStep];

	public WeaponViewport Viewport => _viewport;

	/// <summary>Page dots, for the gate.</summary>
	public string PageStatus => string.Join( ", ", Pages.Select( ( p, i ) => $"{p.Title}={_nav.StatusOf( i )?.Hex ?? "-"}" ) );

	// ------------------------------------------------------------------ actions

	/// <summary>The one path every import takes: drop, file dialog, asset context menu.</summary>
	public void ImportFile( string path )
	{
		if ( !DropPaths.IsWeaponFile( path ) )
		{
			Controller.SetStatus( $"{System.IO.Path.GetFileName( path )} is not a weapon file (FBX, GLB, glTF, VMDL or .weapon.json).", Theme.Red );
			return;
		}
		_ = Controller.LoadAsync( path );
	}

	/// <summary>Picks a weapon from the project's asset browser (models and their FBX/glTF sources).</summary>
	public AssetPicker ChooseFile()
	{
		var picker = new Editor.AssetPickers.GenericPicker( this, WeaponAssetTypes(), new AssetPicker.PickerOptions { EnableCloud = false, EnableMounts = false } );
		picker.Title = "Import weapon";
		picker.OnAssetPicked = assets =>
		{
			var asset = assets?.FirstOrDefault();
			if ( asset is not null )
				ImportFile( asset.AbsolutePath );
		};
		picker.Show();
		return picker;
	}

	/// <summary>Asset types a weapon can be imported from: compiled models and FBX/GLB/glTF sources.</summary>
	public static List<AssetType> WeaponAssetTypes()
	{
		var types = new List<AssetType> { AssetType.Model };
		foreach ( var ext in new[] { "fbx", "glb", "gltf" } )
			if ( AssetType.Find( ext, false ) is { } t )
				types.Add( t );
		return types.Distinct().ToList();
	}

	/// <summary>Picks a weapon file anywhere on disk (outside the project).</summary>
	private void ChooseFromDisk()
	{
		var path = EditorUtility.OpenFileDialog( "Import weapon", "Weapon models (*.fbx *.glb *.gltf *.vmdl);;Weapon setup (*.weapon.json)", null );
		if ( !string.IsNullOrEmpty( path ) )
			ImportFile( path );
	}

	private void UseTemplate()
	{
		var folder = AssetCompiler.AssetsRoot.Length > 0 ? System.IO.Path.Combine( AssetCompiler.AssetsRoot, "weapons" ) : null;
		var template = EditorUtility.OpenFileDialog( "Template: a weapon you already set up", "Weapon setup (*.weapon.json)", folder );
		if ( string.IsNullOrEmpty( template ) )
			return;
		var path = EditorUtility.OpenFileDialog( "Weapon to set up like it", "Weapon models (*.fbx *.glb *.gltf *.vmdl)", null );
		if ( string.IsNullOrEmpty( path ) )
			return;
		new TemplateDialog( this, template, parts => _ = Controller.LoadAsync( path, template, parts ) ).Show();
	}

	private void AutoSetup()
	{
		var session = Controller.Session;
		if ( session is null )
			return;
		_ = Controller.RunAsync( "Setting up the weapon", ( p, c ) => session.AutoSetupAsync( p, c ) );
	}

	// ------------------------------------------------------------------ reactions

	private void OnSessionReplaced()
	{
		var hasSession = Controller.Session is not null;
		_main.Visible = hasSession;
		_drop.Visible = !hasSession && !Controller.Busy;
		_firstLoad.Visible = !hasSession && Controller.Busy;
		OnStatusChanged();
		foreach ( var step in _steps )
			step.ForceRebuild();
		if ( hasSession )
		{
			SelectStep( 0 );
			SetFirstPerson( false );
		}
		OnChanged();
	}

	private void OnChanged()
	{
		var session = Controller.Session;
		if ( session?.Setup is not { } s )
			return;
		_weaponName.Text = s.Name;
		_weaponName.ToolTip = session.SourcePath;
		var a = session.Analysis;
		var confidence = a?.Type.Confidence ?? 0f;
		_typePill.Set( WeaponTypes.Label( s.Type ).ToUpperInvariant(), UiStyle.ConfidenceColor( confidence, s.TypeManual ) );
		_typePill.ToolTip = s.TypeManual ? "Type set by you" : $"Detected type, {confidence * 100:0}% sure{(string.IsNullOrEmpty( a?.Type.Reason ) ? "" : ": " + a.Type.Reason)}";
		UpdatePageStatus( session );
		SyncHeader();
	}

	/// <summary>A dot beside each page that has something to look at.</summary>
	private void UpdatePageStatus( ImportSession session )
	{
		Color? Worst( Func<string, bool> belongs )
		{
			var checks = session.Checks.Where( c => belongs( c.Name ) ).ToList();
			if ( checks.Any( c => c.Severity == CheckSeverity.Error ) )
				return Theme.Red;
			if ( checks.Any( c => c.NeedsAttention ) )
				return Theme.Yellow;
			return null;
		}
		_nav.SetStatus( 0, Worst( n => n is "Skeleton" or "Root" or "Scale" or "Muzzle" or "Eject" or "Type" ) );
		_nav.SetStatus( 1, Worst( n => n.EndsWith( "grip" ) ) );
		_nav.SetStatus( 2, Worst( n => n == "Animations" || n.StartsWith( "Hands in" ) ) );
		var proposals = session.TextureMatches.Any( m => !m.Linked && !session.Setup.TextureChoices.ContainsKey( m.Key ) && !ImportSession.Accepted( session.Setup, m ) && m.Confidence >= 0.35f );
		_nav.SetStatus( 3, proposals ? Theme.Yellow : (Color?)null );
		_nav.SetStatus( 4, Worst( n => n == "Compiles" ) );
	}

	private void OnRoleChanged() => SyncHeader();

	private void OnBusyChanged()
	{
		var busy = Controller.Busy;
		var hasSession = Controller.Session is not null;
		Show( _firstLoad, busy && !hasSession );
		Show( _drop, !busy && !hasSession );
		_firstLoad.SetTitle( Controller.BusyTitle ?? "Processing…" );
		_firstLoad.SetMessage( Controller.BusyMessage );
		_firstLoad.Busy = busy;

		// Toggling the preview's native render surface is expensive (it is re-created), so only
		// touch visibility when it actually changes; light work (live edits, checks) never does.
		Show( _viewportLoader, busy && hasSession );
		Show( _viewport, !(busy && hasSession) );
		_viewportLoader.SetTitle( Controller.BusyTitle ?? "Processing…" );
		_viewportLoader.SetMessage( Controller.BusyMessage );
		_viewportLoader.Busy = busy;

		if ( _autoSetup.Enabled != (!busy && hasSession) )
			_autoSetup.Enabled = !busy && hasSession;
		if ( _bake.Enabled != (!busy && hasSession) )
			_bake.Enabled = !busy && hasSession;
		// No Cancel on the first load after a drop; the loader covers the window.
		Show( _cancel, Controller.CanCancel && Controller.Session is not null );
		OnStatusChanged();
	}

	private static void Show( Widget widget, bool visible )
	{
		if ( widget.Visible != visible )
			widget.Visible = visible;
	}

	private void OnStatusChanged()
	{
		_status.Text = Controller.StatusText;
		_status.ToolTip = Controller.StatusText;
		_status.SetStyles( $"color: {(Controller.StatusColor == Theme.Green || Controller.StatusColor == Theme.TextLight ? Theme.Text.Hex : Controller.StatusColor.Hex)};" );
		_statusDot.Color = Controller.Busy || Controller.Solving ? Theme.Yellow : Controller.StatusColor;

		// No duplicates: while a loader is up it already shows the progress message, and the start
		// screen explains itself. The line appears for results (ready, warnings, errors).
		var hidden = Controller.Busy || (Controller.Session is null && Controller.StatusColor != Theme.Red);
		Show( _statusDot, !hidden );
		Show( _status, !hidden );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !this.IsValid() )
			return;
		try
		{
			Controller.Tick();
			_firstLoad.Tick();
			_viewportLoader.Tick();
			if ( Controller.Session is not null )
			{
				_steps[CurrentStep].Flush();
				_timeline.Tick();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[weapon importer] {e}" );
		}
	}

	// ------------------------------------------------------------------ input

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( Controller.Session is null )
		{
			base.OnKeyPress( e );
			return;
		}
		switch ( e.Key )
		{
			case KeyCode.Space:
				Controller.TogglePlay();
				break;
			case KeyCode.Left:
				Controller.StepFrame( -1 );
				break;
			case KeyCode.Right:
				Controller.StepFrame( 1 );
				break;
			case KeyCode.F:
				_viewport?.FrameWeapon();
				break;
			case KeyCode.Escape when Controller.Pick != PickTarget.None:
				Controller.BeginPick( PickTarget.None );
				break;
			case KeyCode.Num1:
			case KeyCode.Num2:
			case KeyCode.Num3:
			case KeyCode.Num4:
			case KeyCode.Num5:
				SelectStep( (int)e.Key - (int)KeyCode.Num1 );
				break;
			default:
				base.OnKeyPress( e );
				return;
		}
		e.Accepted = true;
	}

	public override void OnDragHover( DragEvent e )
	{
		if ( DropPaths.From( e.Data ) is not null )
			e.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent e )
	{
		var path = DropPaths.From( e.Data );
		if ( path is null )
		{
			Controller.SetStatus( "Drop an FBX, GLB, glTF, VMDL or .weapon.json file.", Theme.Red );
			return;
		}
		e.Action = DropAction.Link;
		ImportFile( path );
	}

	protected override void OnPaint()
	{
		// Paint only this surface; an inherited CSS background hides the segmented controls' selection.
		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );
	}

	public override void OnDestroyed()
	{
		Controller.Changed -= OnChanged;
		Controller.SessionReplaced -= OnSessionReplaced;
		Controller.BusyChanged -= OnBusyChanged;
		Controller.StatusChanged -= OnStatusChanged;
		Controller.RoleChanged -= OnRoleChanged;
		Controller.Dispose();
		if ( _instance == this )
			_instance = null;
		base.OnDestroyed();
	}

	/// <summary>The page column: about 360 px wide by default, resizable with the splitter.</summary>
	private sealed class PagePane : Widget
	{
		public PagePane( Widget parent ) : base( parent )
		{
			MinimumWidth = 320;
		}

		protected override Vector2 SizeHint() => new( 380, 600 );
	}
}
