using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// Hand contact and event lanes of the selected action. Locked contact is a green block,
/// released is a gap, blends are drawn as ramps. Right-click a lane to add a key or event at
/// the playhead, drag markers to move them, right-click a marker to edit or delete it, and
/// double-click a contact block to make the hand follow a weapon bone.
/// </summary>
public sealed class TimelineLanes : Widget
{
	public const float LabelWidth = 84f;
	private const float LaneHeight = 18f;
	private const float LaneGap = 4f;
	private const float Pad = 6f;

	private readonly ImporterController _c;
	private float _playhead;
	private object _drag;
	private bool _dragMoved;

	public TimelineLanes( Widget parent, ImporterController controller ) : base( parent )
	{
		_c = controller;
		FixedHeight = 3 * LaneHeight + 2 * LaneGap + 4;
		MouseTracking = true;
		ToolTip = LanesTip;
	}

	public void SetPlayhead( float t )
	{
		if ( MathF.Abs( _playhead - t ) < 1e-4f )
			return;
		_playhead = t;
		Update();
	}

	private AnimationRole Role => _c.Role;
	private float Seconds => MathF.Max( 0.05f, _c.RoleSeconds( Role ) );
	private float TrackLeft => LabelWidth + Pad;
	private float TrackWidth => MathF.Max( 1f, Width - TrackLeft - Pad );
	private float X( float t ) => TrackLeft + Math.Clamp( t, 0f, 1f ) * TrackWidth;
	private float T( float x ) => Math.Clamp( (x - TrackLeft) / TrackWidth, 0f, 1f );
	private static float LaneTop( int lane ) => 2 + lane * (LaneHeight + LaneGap);
	private static Rect LaneRect( int lane, float left, float width ) => new( left, LaneTop( lane ), width, LaneHeight );

	/// <summary>Lane 0 = left hand, 1 = right hand, 2 = events.</summary>
	private static int LaneAt( float y ) => Math.Clamp( (int)((y - 2) / (LaneHeight + LaneGap)), 0, 2 );

	private ContactTrack Track( bool create = false )
	{
		var setup = _c.Setup;
		if ( setup is null )
			return null;
		if ( setup.Contacts.TryGetValue( Role, out var track ) )
			return track;
		if ( !create )
			return null;
		track = new ContactTrack();
		setup.Contacts[Role] = track;
		return track;
	}

	private IEnumerable<WeaponEvent> Events => _c.Setup?.Events.Where( e => e.Role == Role ) ?? Enumerable.Empty<WeaponEvent>();

	// ------------------------------------------------------------------ paint

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		string[] labels = { "Left hand", "Right hand", "Events" };
		for ( var lane = 0; lane < 3; lane++ )
		{
			Paint.SetDefaultFont( 8 );
			Paint.SetPen( Theme.TextLight );
			Paint.DrawText( new Rect( 0, LaneTop( lane ), LabelWidth - 24, LaneHeight ), labels[lane], TextFlag.LeftCenter );
			if ( lane < 2 && _c.Setup is not null )
				DrawHandToggle( lane );
			Paint.ClearPen();
			Paint.SetBrush( Theme.WindowBackground );
			Paint.DrawRect( LaneRect( lane, TrackLeft, TrackWidth ), 3 );
		}
		if ( _c.Setup is null )
			return;

		var track = Track();
		DrawContacts( track, Side.Left, 0 );
		DrawContacts( track, Side.Right, 1 );
		DrawEvents();

		var x = X( _playhead );
		Paint.SetPen( Theme.Text.WithAlpha( .85f ), 1 );
		Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Height ) );
	}

	/// <summary>The "on weapon / own animation" switch at the end of a hand lane's label.</summary>
	private static Rect ToggleRect( int lane ) => new( LabelWidth - 22, LaneTop( lane ), LaneHeight, LaneHeight );

	private static Side LaneSide( int lane ) => lane == 0 ? Side.Left : Side.Right;

	private void DrawHandToggle( int lane )
	{
		var released = _c.IsHandReleased( Role, LaneSide( lane ) );
		var rect = ToggleRect( lane );
		var hover = rect.IsInside( _mouse );
		Paint.SetPen( released ? UiStyle.ButtonEdge : Theme.Green.WithAlpha( .7f ), 1 );
		Paint.SetBrush( released ? ( hover ? Color.Lerp( UiStyle.ButtonFill, Color.White, .06f ) : UiStyle.ButtonFill ) : Theme.Green.WithAlpha( hover ? .3f : .18f ) );
		Paint.DrawRect( rect.Shrink( .5f ), UiStyle.Radius );
		Paint.SetPen( released ? Theme.TextLight : Theme.Green );
		Paint.DrawIcon( rect, released ? "do_not_touch" : "pan_tool", 12, TextFlag.Center );
	}

	private Vector2 _mouse = new( -1, -1 );

	private void DrawContacts( ContactTrack track, Side side, int lane )
	{
		var rect = LaneRect( lane, TrackLeft, TrackWidth );
		if ( _c.IsHandReleased( Role, side ) )
		{
			// Own animation for the whole action: an empty, dimmed lane.
			Paint.SetPen( Theme.TextLight.WithAlpha( .35f ), 1, PenStyle.Dash );
			Paint.ClearBrush();
			Paint.DrawRect( rect.Shrink( .5f ), 3 );
			Paint.SetPen( Theme.TextLight );
			Paint.SetDefaultFont( 7 );
			Paint.DrawText( rect.Shrink( 8, 0 ), "own animation", TextFlag.LeftCenter );
			return;
		}
		var inactive = side == Side.Left && !(_c.Setup.UseSupportHand && _c.Setup.Support is not null);
		var color = inactive ? Theme.TextLight : Theme.Green;
		var seconds = Seconds;

		// Weight curve as a filled area: full = locked, empty = released, slopes = blends.
		var steps = Math.Max( 2, (int)(rect.Width / 3) );
		var points = new List<Vector2> { new( rect.Left, rect.Bottom - 2 ) };
		for ( var i = 0; i <= steps; i++ )
		{
			var t = i / (float)steps;
			var w = track?.Weight( side, t, seconds ) ?? 1f;
			points.Add( new Vector2( rect.Left + t * rect.Width, rect.Bottom - 2 - w * (rect.Height - 5) ) );
		}
		points.Add( new Vector2( rect.Right, rect.Bottom - 2 ) );
		Paint.SetPen( color.WithAlpha( .9f ), 1 );
		Paint.SetBrush( color.WithAlpha( inactive ? .08f : .22f ) );
		Paint.DrawPolygon( points.ToArray() );

		if ( track is null )
			return;
		foreach ( var key in track.Keys.Where( k => k.Hand == side ) )
		{
			var kx = X( key.Time );
			var locked = key.State == ContactState.Locked;
			var c = locked ? Theme.Green : Theme.TextLight;
			if ( ReferenceEquals( _drag, key ) )
				c = Theme.Primary;
			DrawDiamond( new Vector2( kx, rect.Center.y ), 5f, c );
			if ( locked && !string.IsNullOrEmpty( key.Follow ) )
			{
				Paint.SetPen( Theme.Text );
				Paint.SetDefaultFont( 7 );
				Paint.DrawText( new Rect( kx + 7, rect.Top, 140, rect.Height ), "follows " + key.Follow, TextFlag.LeftCenter );
			}
		}
	}

	private void DrawEvents()
	{
		var rect = LaneRect( 2, TrackLeft, TrackWidth );
		foreach ( var e in Events )
		{
			var ex = X( e.Time );
			var marker = new Rect( ex - 8, rect.Top + 1, 16, 16 );
			Paint.ClearPen();
			Paint.SetBrush( ReferenceEquals( _drag, e ) ? Theme.Primary : Theme.Blue.WithAlpha( e.Manual ? .9f : .6f ) );
			Paint.DrawRect( marker, 8 );
			Paint.SetPen( Color.White );
			Paint.DrawIcon( marker, EventIcon( e.Kind ), 11 );
		}
	}

	private static void DrawDiamond( Vector2 c, float r, Color color )
	{
		Paint.SetPen( Theme.WindowBackground, 1 );
		Paint.SetBrush( color );
		Paint.DrawPolygon( new[] { new Vector2( c.x, c.y - r ), new Vector2( c.x + r, c.y ), new Vector2( c.x, c.y + r ), new Vector2( c.x - r, c.y ) } );
	}

	public static string EventIcon( WeaponEventKind kind ) => kind switch
	{
		WeaponEventKind.Fire => "local_fire_department",
		WeaponEventKind.MuzzleFlash => "flare",
		WeaponEventKind.ShellEject => "eject",
		WeaponEventKind.MagazineDetach => "file_download",
		WeaponEventKind.MagazineInsert => "file_upload",
		WeaponEventKind.BoltPull => "arrow_back",
		WeaponEventKind.BoltRelease => "arrow_forward",
		WeaponEventKind.ReloadComplete => "check_circle",
		_ => "circle",
	};

	// ------------------------------------------------------------------ hit testing

	private object HitMarker( Vector2 p )
	{
		if ( _c.Setup is null )
			return null;
		var lane = LaneAt( p.y );
		if ( lane == 2 )
			return Events.OrderBy( e => MathF.Abs( X( e.Time ) - p.x ) ).FirstOrDefault( e => MathF.Abs( X( e.Time ) - p.x ) <= 8 );
		var side = lane == 0 ? Side.Left : Side.Right;
		return Track()?.Keys.Where( k => k.Hand == side ).OrderBy( k => MathF.Abs( X( k.Time ) - p.x ) ).FirstOrDefault( k => MathF.Abs( X( k.Time ) - p.x ) <= 6 );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		if ( _drag is not null && (e.ButtonState & MouseButtons.Left) != 0 )
		{
			var t = T( e.LocalPosition.x );
			if ( _drag is ContactKey key )
				key.Time = t;
			else if ( _drag is WeaponEvent ev )
			{
				ev.Time = t;
				ev.Manual = true;
			}
			_dragMoved = true;
			Update();
			return;
		}
		var hit = HitMarker( e.LocalPosition );
		_mouse = e.LocalPosition;
		Cursor = hit is not null ? CursorShape.SizeH : CursorShape.Finger;
		ToolTip = HandToggleSideAt( e.LocalPosition ) is { } side
			? $"{(side == Side.Left ? "Left" : "Right")} hand during {AnimationRoles.Label( Role )}: {(_c.IsHandReleased( Role, side ) ? "plays its own animation" : "on the weapon")}. Click to switch."
			: LanesTip;
		Update();
	}

	private const string LanesTip = "Right-click a lane to add a key or an event at the playhead · drag markers to move them · double-click a hand block to follow a weapon bone · the hand buttons switch a hand between the weapon and its own animation";

	private int? HandToggleAt( Vector2 p )
	{
		for ( var lane = 0; lane < 2; lane++ )
			if ( ToggleRect( lane ).IsInside( p ) )
				return lane;
		return null;
	}

	private Side? HandToggleSideAt( Vector2 p ) => HandToggleAt( p ) is { } lane ? LaneSide( lane ) : null;

	protected override void OnMouseLeave()
	{
		_mouse = new Vector2( -1, -1 );
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( _c.Setup is null )
			return;
		if ( e.LeftMouseButton && HandToggleSideAt( e.LocalPosition ) is { } toggled )
		{
			_c.ToggleHand( Role, toggled );
			e.Accepted = true;
			return;
		}
		var hit = HitMarker( e.LocalPosition );
		if ( e.LeftMouseButton )
		{
			if ( e.IsDoubleClick && hit is null && LaneAt( e.LocalPosition.y ) < 2 )
			{
				FollowMenu( LaneAt( e.LocalPosition.y ) == 0 ? Side.Left : Side.Right, T( e.LocalPosition.x ) );
				e.Accepted = true;
				return;
			}
			if ( hit is not null )
			{
				_drag = hit;
				_dragMoved = false;
			}
			else if ( e.LocalPosition.x >= TrackLeft )
				_c.Seek( T( e.LocalPosition.x ) );
			e.Accepted = true;
		}
		else if ( e.RightMouseButton )
		{
			if ( hit is ContactKey key )
				KeyMenu( key );
			else if ( hit is WeaponEvent ev )
				EventMenu( ev );
			else
				LaneMenu( LaneAt( e.LocalPosition.y ) );
			e.Accepted = true;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( _drag is not null )
		{
			_drag = null;
			if ( _dragMoved )
				Commit( "Moved on the timeline." );
			Update();
		}
	}

	private void Commit( string message )
	{
		var track = Track();
		track?.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
		// Edited by hand: the importer no longer re-plans this action.
		if ( track is not null )
			track.Generated = false;
		_c.Session?.Revalidate();
		_c.MarkChanged();
		_c.SetStatus( message, Theme.Green );
		Update();
	}

	// ------------------------------------------------------------------ menus

	private void LaneMenu( int lane )
	{
		var t = _c.Time;
		var menu = new Menu( this );
		if ( lane == 2 )
		{
			menu.AddHeading( $"Add event at {t * Seconds:0.00} s" );
			foreach ( var kind in Enum.GetValues<WeaponEventKind>() )
			{
				var k = kind;
				menu.AddOption( WeaponEvent.Label( kind ), EventIcon( kind ), () =>
				{
					_c.Setup.Events.Add( new WeaponEvent { Role = Role, Kind = k, Time = t, Manual = true } );
					Commit( $"{WeaponEvent.Label( k )} added." );
				} );
			}
		}
		else
		{
			var side = lane == 0 ? Side.Left : Side.Right;
			menu.AddHeading( $"{(side == Side.Left ? "Left" : "Right")} hand at {t * Seconds:0.00} s" );
			foreach ( var blend in new[] { 0f, 0.1f, 0.2f, 0.35f } )
			{
				var b = blend;
				menu.AddOption( $"Lock here{(b > 0 ? $" · blend {b:0.##} s" : " · instant")}", "lock", () => AddKey( side, t, ContactState.Locked, b ) );
			}
			menu.AddSeparator();
			foreach ( var blend in new[] { 0f, 0.1f, 0.2f, 0.35f } )
			{
				var b = blend;
				menu.AddOption( $"Release here{(b > 0 ? $" · blend {b:0.##} s" : " · instant")}", "lock_open", () => AddKey( side, t, ContactState.Released, b ) );
			}
		}
		menu.OpenAtCursor();
	}

	private void AddKey( Side side, float t, ContactState state, float blend )
	{
		Track( create: true ).Keys.Add( new ContactKey { Hand = side, Time = t, State = state, Blend = blend } );
		Commit( $"{(side == Side.Left ? "Left" : "Right")} hand {(state == ContactState.Locked ? "locks" : "lets go")} at {t * Seconds:0.00} s." );
	}

	private void KeyMenu( ContactKey key )
	{
		var menu = new Menu( this );
		menu.AddHeading( $"{(key.Hand == Side.Left ? "Left" : "Right")} hand · {(key.State == ContactState.Locked ? "lock" : "release")} at {key.Time * Seconds:0.00} s" );
		menu.AddOption( key.State == ContactState.Locked ? "Make it a release" : "Make it a lock", key.State == ContactState.Locked ? "lock_open" : "lock", () =>
		{
			key.State = key.State == ContactState.Locked ? ContactState.Released : ContactState.Locked;
			Commit( "Key changed." );
		} );
		var blend = menu.AddMenu( $"Blend ({key.Blend:0.##} s)", "timeline" );
		foreach ( var b in new[] { 0f, 0.05f, 0.1f, 0.15f, 0.2f, 0.35f, 0.5f } )
		{
			var v = b;
			blend.AddOption( $"{v:0.##} s", MathF.Abs( key.Blend - v ) < 1e-3f ? "check" : null, () =>
			{
				key.Blend = v;
				Commit( "Blend changed." );
			} );
		}
		if ( key.State == ContactState.Locked )
			AddFollowOptions( menu.AddMenu( "Follow bone", "link" ), key );
		menu.AddOption( "Move to playhead", "vertical_align_center", () =>
		{
			key.Time = _c.Time;
			Commit( "Key moved." );
		} );
		menu.AddSeparator();
		menu.AddOption( "Delete key", "delete", () =>
		{
			Track()?.Keys.Remove( key );
			Commit( "Key deleted." );
		} );
		menu.OpenAtCursor();
	}

	private void EventMenu( WeaponEvent ev )
	{
		var menu = new Menu( this );
		menu.AddHeading( $"{WeaponEvent.Label( ev.Kind )} at {ev.Time * Seconds:0.00} s" );
		var kinds = menu.AddMenu( "Change kind", "swap_horiz" );
		foreach ( var kind in Enum.GetValues<WeaponEventKind>() )
		{
			var k = kind;
			kinds.AddOption( WeaponEvent.Label( kind ), EventIcon( kind ), () =>
			{
				ev.Kind = k;
				ev.Manual = true;
				Commit( "Event changed." );
			} );
		}
		menu.AddOption( "Move to playhead", "vertical_align_center", () =>
		{
			ev.Time = _c.Time;
			ev.Manual = true;
			Commit( "Event moved." );
		} );
		menu.AddSeparator();
		menu.AddOption( "Delete event", "delete", () =>
		{
			_c.Setup.Events.Remove( ev );
			Commit( "Event deleted." );
		} );
		menu.OpenAtCursor();
	}

	/// <summary>Double-click on a contact block: choose the weapon bone the hand follows while locked.</summary>
	private void FollowMenu( Side side, float t )
	{
		var track = Track( create: true );
		if ( track.Weight( side, t, Seconds ) < 0.5f )
		{
			_c.SetStatus( "The hand is released there; double-click where it holds the weapon.", Theme.TextLight );
			return;
		}
		var key = track.Keys.Where( k => k.Hand == side && k.State == ContactState.Locked && k.Time <= t ).OrderByDescending( k => k.Time ).FirstOrDefault();
		if ( key is null )
		{
			// Locked from the start: an explicit lock key at 0 carries the follow bone.
			key = new ContactKey { Hand = side, Time = 0f, State = ContactState.Locked, Blend = 0f };
			track.Keys.Add( key );
		}
		var menu = new Menu( this );
		menu.AddHeading( $"{(side == Side.Left ? "Left" : "Right")} hand follows" );
		AddFollowOptions( menu, key );
		menu.OpenAtCursor();
	}

	private void AddFollowOptions( Menu menu, ContactKey key )
	{
		menu.AddOption( "The weapon (no bone)", string.IsNullOrEmpty( key.Follow ) ? "check" : "block", () =>
		{
			key.Follow = "";
			Commit( "The hand follows the weapon." );
		} );
		var analysis = _c.Analysis;
		if ( analysis is null )
			return;
		var partBones = _c.Setup.Parts.Where( p => p.Bone.Length > 0 ).ToDictionary( p => p.Bone, p => WeaponStep.Nice( p.Kind ) );
		foreach ( var bone in analysis.Asset.Skeleton.Bones.OrderByDescending( b => partBones.ContainsKey( b.Name ) ) )
		{
			var name = bone.Name;
			var label = partBones.TryGetValue( name, out var part ) ? $"{name}  ({part})" : name;
			menu.AddOption( label, key.Follow == name ? "check" : "linear_scale", () =>
			{
				key.Follow = name;
				Commit( $"The hand follows {name}." );
			} );
		}
	}
}
