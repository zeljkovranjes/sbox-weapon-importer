using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Setup;
using WeaponImporter.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.Tool.UI;

/// <summary>What the next click on the weapon in the viewport places.</summary>
public enum PickTarget { None, RightGrip, LeftGrip, Muzzle, Eject }

/// <summary>Reports progress on the main thread (the editor has no synchronization context).</summary>
public sealed class MainThreadProgress : IProgress<string>
{
	private readonly Action<string> _report;

	public MainThreadProgress( Action<string> report ) => _report = report;

	public void Report( string value )
	{
		if ( ThreadSafe.IsMainThread )
			_report( value );
		else
			MainThread.Queue( () => _report( value ) );
	}
}

/// <summary>
/// The window's model: the current <see cref="ImportSession"/>, every long-running operation
/// (with cancellation and stale-result protection), playback and viewport state shared by the
/// steps, the preview and the timeline. Everything here runs on the main thread.
/// </summary>
public sealed class ImporterController : IDisposable
{
	public ImportSession Session { get; private set; }
	public WeaponSetup Setup => Session?.Setup;
	public WeaponAnalysis Analysis => Session?.Analysis;

	/// <summary>Session data changed (setup edit, solve finished, new session).</summary>
	public event Action Changed;

	/// <summary>A different weapon was loaded (or the session went away).</summary>
	public event Action SessionReplaced;

	public event Action BusyChanged;
	public event Action StatusChanged;
	public event Action RoleChanged;
	public event Action PickChanged;

	// ------------------------------------------------------------------ status

	public string StatusText { get; private set; } = "Drop a weapon to start.";
	public Color StatusColor { get; private set; } = Theme.TextLight;

	public void SetStatus( string text, Color color )
	{
		StatusText = text ?? "";
		StatusColor = color;
		StatusChanged?.Invoke();
	}

	// ------------------------------------------------------------------ operations

	/// <summary>Title of the heavy operation running (load, analysis, setup, bake), or null.</summary>
	public string BusyTitle { get; private set; }
	public string BusyMessage { get; private set; } = "";
	public bool Busy => BusyTitle is not null;

	/// <summary>A light background operation (grip solve) is running.</summary>
	public bool Solving => _lightCount > 0;

	public bool CanCancel => _heavyCancel is not null || _lightCancel is not null;

	private CancellationTokenSource _heavyCancel;
	private CancellationTokenSource _lightCancel;
	private int _heavyRevision;
	private int _lightCount;
	private double _resolveDue = double.MaxValue;

	/// <summary>
	/// Runs <paramref name="work"/> with progress and cancellation. Heavy operations cover the
	/// preview with the loader and replace each other; light ones only show in the status row.
	/// Errors never escape: they end up in the status row.
	/// </summary>
	public async Task RunAsync( string title, Func<IProgress<string>, CancellationToken, Task> work, bool heavy = true, string done = null )
	{
		CancellationTokenSource cancel;
		var revision = 0;
		if ( heavy )
		{
			_heavyCancel?.Cancel();
			cancel = _heavyCancel = new CancellationTokenSource();
			revision = ++_heavyRevision;
			BusyTitle = title;
			BusyMessage = "";
		}
		else
		{
			_lightCancel?.Cancel();
			cancel = _lightCancel = new CancellationTokenSource();
			_lightCount++;
		}
		BusyChanged?.Invoke();
		SetStatus( title + "…", Theme.Yellow );

		var progress = new MainThreadProgress( message =>
		{
			if ( heavy && revision != _heavyRevision )
				return;
			BusyMessage = message;
			SetStatus( message, Theme.Yellow );
			BusyChanged?.Invoke();
		} );
		try
		{
			await work( progress, cancel.Token );
			await EditorThread.SwitchToMainThread();
			if ( !cancel.IsCancellationRequested && (!heavy || revision == _heavyRevision) )
				SetStatus( done ?? ReadyText(), done is null ? ReadyColor() : Theme.Green );
		}
		catch ( OperationCanceledException )
		{
			await EditorThread.SwitchToMainThread();
			if ( !heavy || revision == _heavyRevision )
				SetStatus( "Cancelled.", Theme.TextLight );
		}
		catch ( Exception e )
		{
			await EditorThread.SwitchToMainThread();
			Log.Warning( $"[weapon importer] {title} failed: {e}" );
			SetStatus( $"{title} failed: {e.Message}", Theme.Red );
		}
		finally
		{
			if ( heavy )
			{
				if ( revision == _heavyRevision )
				{
					_heavyCancel = null;
					BusyTitle = null;
				}
			}
			else
			{
				_lightCount--;
				if ( ReferenceEquals( _lightCancel, cancel ) )
					_lightCancel = null;
			}
			BusyChanged?.Invoke();
		}
	}

	public void Cancel()
	{
		_heavyCancel?.Cancel();
		_lightCancel?.Cancel();
		_resolveDue = double.MaxValue;
	}

	/// <summary>Re-solves the grip shortly after the last call (sliders call this on every move).</summary>
	public void ScheduleResolve( float delay = 0.15f, Side? edited = null )
	{
		// Two different hands edited before the fit runs: fit both.
		_resolveEdited = _resolveDue == double.MaxValue ? edited : (_resolveEdited == edited ? edited : null);
		_resolveDue = RealTime.Now + delay;
	}

	private Side? _resolveEdited;

	private readonly Dictionary<string, (double Due, Action Action)> _debounced = new();

	/// <summary>Runs <paramref name="action"/> once, <paramref name="delay"/> seconds after the last call with the same key.</summary>
	public void Debounce( string key, Action action, float delay = 0.15f )
	{
		_debounced[key] = (RealTime.Now + delay, action);
	}

	/// <summary>Called every editor frame by the window.</summary>
	public void Tick()
	{
		FlushDrag();
		StepSlide();
		if ( _debounced.Count > 0 )
		{
			foreach ( var (key, (due, action)) in _debounced.ToArray() )
			{
				if ( RealTime.Now < due )
					continue;
				_debounced.Remove( key );
				try
				{
					action();
				}
				catch ( Exception e )
				{
					SetStatus( e.Message, Theme.Red );
				}
			}
		}
		if ( RealTime.Now < _resolveDue || Session is null )
			return;
		if ( Busy )
		{
			// Wait for the heavy operation instead of racing it.
			_resolveDue = RealTime.Now + 0.3f;
			return;
		}
		_resolveDue = double.MaxValue;
		var session = Session;
		var edited = _resolveEdited;
		_resolveEdited = null;
		_ = RunAsync( "Fitting hands", ( p, c ) => session.ResolveGripAsync( p, c, edited ), heavy: false );
	}

	private Color ReadyColor() => Session?.Checks?.Any( c => c.NeedsAttention ) == true ? Theme.Yellow : Theme.Green;

	private string ReadyText()
	{
		if ( Session?.Checks is not { } checks )
			return "Ready.";
		var problems = checks.Count( c => c.NeedsAttention );
		return problems == 0 ? "Ready. Everything checks out." : $"Ready. {problems} thing{(problems == 1 ? "" : "s")} to look at in Check.";
	}

	// ------------------------------------------------------------------ session lifecycle

	public string LoadingPath { get; private set; }

	/// <summary>Loads a weapon file or a saved *.weapon.json setup (the drop and file-dialog path).</summary>
	public Task LoadAsync( string path, string templatePath = null, TemplateParts templateParts = TemplateParts.All )
	{
		if ( DropPaths.IsSetupFile( path ) )
		{
			try
			{
				var saved = WeaponSetup.FromJson( System.IO.File.ReadAllText( path ) );
				if ( string.IsNullOrEmpty( saved.Source ) || !System.IO.File.Exists( saved.Source ) )
				{
					SetStatus( $"The setup's source file '{saved.Source}' was not found.", Theme.Red );
					return Task.CompletedTask;
				}
				path = saved.Source;
			}
			catch ( Exception e )
			{
				SetStatus( $"Could not read the setup: {e.Message}", Theme.Red );
				return Task.CompletedTask;
			}
		}

		LoadingPath = path;
		var name = System.IO.Path.GetFileName( path );
		return RunAsync( $"Loading {name}", async ( progress, cancel ) =>
		{
			var session = await ImportSession.LoadAsync( path, progress, cancel );
			await EditorThread.SwitchToMainThread();
			if ( cancel.IsCancellationRequested )
			{
				session.Dispose();
				return;
			}
			SetSession( session );
			if ( templatePath is not null )
			{
				var notes = session.ApplyTemplate( templatePath, templateParts );
				if ( notes.Count > 0 )
					Log.Info( $"[weapon importer] template: {string.Join( "; ", notes )}" );
			}
		} );
	}

	private void SetSession( ImportSession session )
	{
		if ( Session is not null )
		{
			Session.Changed -= OnSessionChanged;
			Session.GripMoved -= OnGripMoved;
			Session.Status -= OnSessionStatus;
			Session.Dispose();
		}
		Session = session;
		LastBake = null;
		BakedModelPath = null;
		Pick = PickTarget.None;
		HighlightBone = null;
		Role = AnimationRole.Idle;
		Time = 0;
		Playing = true;
		RestartRequested = true;
		if ( session is not null )
		{
			session.Changed += OnSessionChanged;
			session.GripMoved += OnGripMoved;
			session.Status += OnSessionStatus;
		}
		RefreshSurfaces();
		SessionReplaced?.Invoke();
		Changed?.Invoke();
	}

	/// <summary>Raised on every live re-fit while a fine-tune or finger slider moves.</summary>
	public event Action GripMoved;

	private void OnGripMoved() => GripMoved?.Invoke();

	/// <summary>
	/// Applies a fine-tune or finger edit to the preview immediately (no search, no freeze) and
	/// runs the action check and validation once the edits pause. Falls back to a full fit when
	/// nothing is fitted yet.
	/// </summary>
	public void LiveRefit( Side? editedHand = null, HandPose editedPose = null )
	{
		if ( Session is null )
			return;
		if ( Busy || !Session.Refit( editedHand, editedPose ) )
		{
			ScheduleResolve();
			return;
		}
		var session = Session;
		Debounce( "settle", () => _ = RunAsync( "Checking hands", ( p, c ) => session.SettleAsync( p, c ), heavy: false ), 0.4f );
	}

	private void OnSessionChanged()
	{
		RefreshSurfaces();
		Changed?.Invoke();
	}

	private void OnSessionStatus( string message )
	{
		if ( string.IsNullOrEmpty( message ) )
			return;
		if ( Busy )
		{
			BusyMessage = message;
			BusyChanged?.Invoke();
		}
		SetStatus( message == "Ready" ? ReadyText() : message, message == "Ready" ? ReadyColor() : Theme.Yellow );
	}

	/// <summary>Tell everyone the setup changed (after an edit that does not need a re-solve).</summary>
	public void MarkChanged() => Session?.MarkChanged();

	// ------------------------------------------------------------------ bake

	public BakeResult LastBake { get; private set; }

	/// <summary>Compiled weapon model once a bake succeeded (preview switches to it).</summary>
	public string BakedModelPath { get; private set; }

	public Task BakeAsync()
	{
		var session = Session;
		if ( session is null )
			return Task.CompletedTask;
		return RunAsync( "Baking", async ( progress, cancel ) =>
		{
			var result = await session.BakeAsync( progress, cancel );
			await EditorThread.SwitchToMainThread();
			LastBake = result;
			if ( result.Success )
				BakedModelPath = result.ModelPath;
			SetStatus( result.Success ? $"Baked {result.Files.Count} files into {session.OutputFolder}." : "Bake finished with errors; see Check.", result.Success ? Theme.Green : Theme.Red );
			Changed?.Invoke();
		}, done: null );
	}

	// ------------------------------------------------------------------ measured surfaces

	/// <summary>The right grip surface re-measured on the geometry (for readouts and the ring).</summary>
	public GripCandidate RightSurface { get; private set; }
	public GripCandidate LeftSurface { get; private set; }

	private void RefreshSurfaces()
	{
		RightSurface = null;
		LeftSurface = null;
		if ( Analysis is null || Setup is null )
			return;
		try
		{
			RightSurface = AutoSetup.Candidate( Analysis, Setup.Primary );
			if ( Setup.Support is not null )
				LeftSurface = AutoSetup.Candidate( Analysis, Setup.Support );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[weapon importer] could not measure the grip surface: {e.Message}" );
		}
	}

	// ------------------------------------------------------------------ playback

	public AnimationRole Role { get; private set; } = AnimationRole.Idle;

	/// <summary>Normalized time of the selected role (0..1).</summary>
	public float Time { get; set; }
	public bool Playing { get; set; } = true;
	public float Speed { get; set; } = 1f;
	public bool Loop { get; set; } = true;

	/// <summary>Set when the selected action must start over (role switch, replay).</summary>
	public bool RestartRequested { get; set; }

	/// <summary>Seconds of each role as the preview hold sees them (filled by the viewport).</summary>
	public Func<AnimationRole, float> RoleSeconds { get; set; } = _ => 1f;

	public void SelectRole( AnimationRole role )
	{
		Role = role;
		Time = 0;
		Playing = true;
		RestartRequested = true;
		RoleChanged?.Invoke();
	}

	public void TogglePlay()
	{
		Playing = !Playing;
		if ( Playing && Time >= 0.999f )
			Time = 0;
		RestartRequested = Playing;
		RoleChanged?.Invoke();
	}

	public void Seek( float normalized )
	{
		Playing = false;
		Time = Math.Clamp( normalized, 0f, 1f );
		RoleChanged?.Invoke();
	}

	public void StepFrame( int frames )
	{
		var seconds = MathF.Max( 0.05f, RoleSeconds( Role ) );
		Seek( Time + frames / (30f * seconds) );
	}

	// ------------------------------------------------------------------ viewport state

	public PickTarget Pick { get; private set; }
	public string HighlightBone { get; set; }

	/// <summary>Characters the hands can be fitted to (id, label).</summary>
	public static readonly (string Id, string Label)[] Characters = { ("human", "Human"), ("human_female", "Human (female)"), ("citizen", "Citizen") };

	/// <summary>Fits the hands to another character (re-samples its poses and actions in the background).</summary>
	public void SetCharacter( string character )
	{
		var session = Session;
		if ( session?.Setup is null || session.Setup.Character == character )
			return;
		if ( CharacterLibrary.Problem( character ) is { } problem )
		{
			SetStatus( problem, Theme.Red );
			Changed?.Invoke(); // the character list goes back to the current one
			return;
		}
		var previous = session.Setup.Character;
		session.Setup.Character = character;
		_ = RunAsync( "Changing character", async ( p, c ) =>
		{
			try
			{
				await session.PrepareCharacterAsync( p, c );
			}
			catch
			{
				// The previous character's poses are still loaded; keep using it.
				session.Setup.Character = previous;
				Changed?.Invoke();
				throw;
			}
			await session.ResolveGripAsync( p, c );
		} );
	}

	/// <summary>Original | Corrected: show the character's animation without the hand correction.</summary>
	public bool ShowOriginal { get; set; }

	public bool ShowSkeleton { get; set; } = true;

	/// <summary>Semantic regions (grips, trigger, magazine, keep-clear) drawn on the weapon.</summary>
	public bool ShowRegions { get; set; }

	/// <summary>A region picked in the Grip page, drawn highlighted.</summary>
	public WeaponImporter.Core.Grip.GripRegionKind? HighlightRegion { get; set; }
	public bool ShowContacts { get; set; } = true;
	public bool ShowIkTargets { get; set; }
	public bool ShowAttachments { get; set; } = true;
	public bool ShowNormals { get; set; }

	public void BeginPick( PickTarget target )
	{
		Pick = Pick == target ? PickTarget.None : target;
		if ( Pick != PickTarget.None )
			SetStatus( PickHint( Pick ), Theme.Blue );
		PickChanged?.Invoke();
	}

	public static string PickHint( PickTarget target ) => target switch
	{
		PickTarget.RightGrip => "Click the weapon where the right hand should hold it. Esc cancels.",
		PickTarget.LeftGrip => "Click the weapon where the left hand should support it. Esc cancels.",
		PickTarget.Muzzle => "Muzzle selected: click the weapon to move it. Esc or a click on empty space deselects.",
		PickTarget.Eject => "Shell eject selected: click the ejection port to move it. Esc or a click on empty space deselects.",
		_ => "",
	};

	// ------------------------------------------------------------------ grip dragging

	private (Side Side, N.Vector3 Point, N.Vector3 Normal)? _dragTarget;
	private double _dragDue;

	/// <summary>Seconds between re-fits while a grip is dragged (each new fit cancels the previous one).</summary>
	public const float DragFitInterval = 0.06f;

	/// <summary>A grip contact dragged across the weapon: the marker follows now, the hand within a few frames.</summary>
	public void DragGrip( Side side, N.Vector3 point, N.Vector3 normal )
	{
		var grip = side == Side.Right ? Setup?.Primary : Setup?.Support;
		if ( grip is null )
			return;
		_dragging = true;
		// The grasp slides toward the contact a little every frame (continuous, no search);
		// without a fitted grip yet, the spot gets a fit instead.
		if ( !Busy && Session.Grip is not null )
		{
			_slideTarget = (side, point, normal);
			StepSlide();
			return;
		}
		grip.Contact = V.A( point );
		_dragTarget = (side, point, normal);
		_lastDrag = _dragTarget;
	}

	private (Side Side, N.Vector3 Point, N.Vector3 Normal)? _slideTarget;

	/// <summary>One slide step toward the dragged spot; stops once the grasp is there.</summary>
	private void StepSlide()
	{
		if ( _slideTarget is not { } t || Session is null || Busy )
			return;
		var remaining = Session.SlideGrip( t.Side, t.Point, t.Normal );
		if ( remaining is null || remaining < 0.01f )
		{
			_slideTarget = null;
			if ( !_dragging )
				SettleSlide();
		}
	}

	private void SettleSlide()
	{
		var session = Session;
		if ( session is not null )
			_ = RunAsync( "Checking hands", ( p, c ) => session.SettleAsync( p, c ), heavy: false, done: "Grip moved." );
	}

	private (Side Side, N.Vector3 Point, N.Vector3 Normal)? _lastDrag;
	private bool _dragging;

	/// <summary>Drag released: one full-quality fit at the final spot.</summary>
	public void EndDragGrip()
	{
		_dragging = false;
		_dragDue = 0;
		_dragTarget ??= _lastDrag;
		_lastDrag = null;
		if ( _dragTarget is not null )
		{
			FlushDrag();
			return;
		}
		// Slid grip: keep it; once it has arrived, re-check and re-plan the actions once.
		if ( _slideTarget is null )
			SettleSlide();
	}

	private void FlushDrag()
	{
		if ( _dragTarget is not { } t || Session is null || RealTime.Now < _dragDue )
			return;
		_dragTarget = null;
		_dragDue = RealTime.Now + DragFitInterval;
		var session = Session;
		var quick = _dragging;
		_ = RunAsync( quick ? "Moving the grip" : "Fitting the grip", ( p, c ) => session.PickGripAsync( t.Side, t.Point, t.Normal, quickOnly: quick ), heavy: false, done: "Grip moved." );
	}

	/// <summary>The viewport hit the weapon at a canonical-space point while picking.</summary>
	public void CompletePick( N.Vector3 point, N.Vector3 normal )
	{
		var target = Pick;
		Pick = PickTarget.None;
		PickChanged?.Invoke();
		var session = Session;
		if ( session?.Setup is null )
			return;
		switch ( target )
		{
			case PickTarget.RightGrip:
			case PickTarget.LeftGrip:
				var side = target == PickTarget.RightGrip ? Side.Right : Side.Left;
				_ = RunAsync( side == Side.Right ? "Fitting the right hand" : "Fitting the left hand", ( p, c ) => session.PickGripAsync( side, point, normal ), heavy: false );
				break;
			case PickTarget.Muzzle:
			case PickTarget.Eject:
				PlacePoint( target == PickTarget.Muzzle, point );
				break;
		}
	}

	private void PlacePoint( bool muzzle, N.Vector3 point )
	{
		var a = Analysis;
		var s = Setup;
		var skeleton = a.Asset.Skeleton;
		var existing = muzzle ? s.Muzzle : s.Eject;
		var bone = existing is not null && skeleton.IndexOf( existing.Bone ) >= 0 ? skeleton.IndexOf( existing.Bone ) : a.RootBone;
		var rest = skeleton.RestWorld[bone];
		var rotation = muzzle ? N.Quaternion.Identity : WeaponAnalyzer.EjectRotation;
		var model = new XForm( point, rotation );
		var detection = new PointDetection
		{
			Bone = skeleton[bone].Name,
			Local = XForm.ToLocal( rest, model ),
			Model = model,
			Confidence = 1f,
			Reason = "placed in the viewport",
			Manual = true,
		};
		var placed = AutoSetup.Point( a, detection );
		if ( muzzle )
			s.Muzzle = placed;
		else
			s.Eject = placed;
		Session.Revalidate();
		MarkChanged();
		SetStatus( muzzle ? "Muzzle placed." : "Shell eject placed.", Theme.Green );
	}

	// ------------------------------------------------------------------ per-action hands

	/// <summary>
	/// True when a hand plays its own animation for the whole action: its contact keys for the
	/// role are exactly one release at the start (what <see cref="ToggleHand"/> writes).
	/// </summary>
	public bool IsHandReleased( AnimationRole role, Side side )
	{
		if ( Setup?.Contacts is null || !Setup.Contacts.TryGetValue( role, out var track ) )
			return false;
		var keys = track.Keys.Where( k => k.Hand == side ).ToList();
		return keys.Count == 1 && keys[0].Time <= 0.001f && keys[0].State == ContactState.Released;
	}

	/// <summary>
	/// Switches a hand for one action between "on the weapon" and "its own animation": replaces
	/// that hand's keys with a single lock or release at the start; the other hand is untouched.
	/// </summary>
	public void ToggleHand( AnimationRole role, Side side )
	{
		var setup = Setup;
		if ( setup is null )
			return;
		var release = !IsHandReleased( role, side );
		if ( !setup.Contacts.TryGetValue( role, out var track ) )
			setup.Contacts[role] = track = new ContactTrack();
		track.Generated = false;
		track.Keys.RemoveAll( k => k.Hand == side );
		track.Keys.Add( new ContactKey { Hand = side, Time = 0f, State = release ? ContactState.Released : ContactState.Locked, Blend = 0.15f } );
		track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
		Session.Revalidate();
		MarkChanged();
		var hand = side == Side.Left ? "Left" : "Right";
		SetStatus( release
			? $"{hand} hand plays its own animation during {AnimationRoles.Label( role )}."
			: $"{hand} hand holds the weapon during {AnimationRoles.Label( role )}.", Theme.Green );
	}

	public void Dispose()
	{
		Cancel();
		if ( Session is not null )
		{
			Session.Changed -= OnSessionChanged;
			Session.Status -= OnSessionStatus;
			Session.Dispose();
			Session = null;
		}
	}
}
