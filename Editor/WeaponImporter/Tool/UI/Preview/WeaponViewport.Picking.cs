using Editor;
using Sandbox;
using WeaponImporter.Core.Hands;

namespace WeaponImporter.Tool.UI;

/// <summary>Clicks in the preview: attachment markers, arms (per-action release) and the weapon surface.</summary>
public sealed partial class WeaponViewport
{
	private const float MarkerRadiusPx = 14f;
	private const float ArmRadiusPx = 12f;

	private readonly Dictionary<int, Side?> _boneSide = new();

	/// <summary>Right/left from the bone name suffix (hand_R, arm_lower_L, finger_index_0_R...).</summary>
	private Side? SideOf( int bone )
	{
		if ( _boneSide.TryGetValue( bone, out var side ) )
			return side;
		var name = _body.IsValid() && _body.Model is { } m ? m.GetBoneName( bone ).ToLowerInvariant() : "";
		side = name.EndsWith( "_r" ) ? Side.Right : name.EndsWith( "_l" ) ? Side.Left : null;
		_boneSide[bone] = side;
		return side;
	}

	private bool ToScreen( Vector3 world, out Vector2 screen )
	{
		screen = Camera.PointToScreenPixels( world, out var behind );
		return !behind;
	}

	/// <summary>The muzzle or eject marker under a viewport pixel (None when neither).</summary>
	public PickTarget HitMarker( Vector2 local )
	{
		var setup = _c.Setup;
		if ( setup is null || !Camera.IsValid() || !_weaponObject.IsValid() || FirstPerson )
			return PickTarget.None;
		var weapon = WeaponWorld;
		var best = PickTarget.None;
		var bestDistance = MarkerRadiusPx;
		foreach ( var (target, point) in new[] { (PickTarget.Muzzle, setup.Muzzle), (PickTarget.Eject, setup.Eject) } )
		{
			if ( point is null || !ToScreen( weapon.PointToWorld( Core.Setup.V.Of( point.Canonical ).ToEngine() ), out var screen ) )
				continue;
			var d = (screen - local).Length;
			if ( d < bestDistance )
			{
				bestDistance = d;
				best = target;
			}
		}
		return best;
	}

	/// <summary>Which character arm (upper arm, forearm, hand or fingers) is under a viewport pixel.</summary>
	public Side? HitArm( Vector2 local )
	{
		if ( !Camera.IsValid() || !_body.IsValid() || !_body.SceneModel.IsValid() || FirstPerson )
			return null;
		Side? best = null;
		var bestDistance = ArmRadiusPx;
		foreach ( var (bone, parent) in _armBones )
		{
			if ( SideOf( bone ) is not { } side )
				continue;
			if ( !ToScreen( _body.SceneModel.GetBoneWorldTransform( parent ).Position, out var a ) || !ToScreen( _body.SceneModel.GetBoneWorldTransform( bone ).Position, out var b ) )
				continue;
			var d = DistanceToSegment( local, a, b );
			if ( d < bestDistance )
			{
				bestDistance = d;
				best = side;
			}
		}
		return best;
	}

	private static float DistanceToSegment( Vector2 p, Vector2 a, Vector2 b )
	{
		var ab = b - a;
		var lengthSq = ab.x * ab.x + ab.y * ab.y;
		var t = lengthSq < 1e-4f ? 0f : Math.Clamp( ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / lengthSq, 0f, 1f );
		return (p - (a + ab * t)).Length;
	}

	/// <summary>Where a marker is on screen (gate and tests), or null.</summary>
	public Vector2? MarkerScreenPosition( PickTarget target )
	{
		var point = target == PickTarget.Muzzle ? _c.Setup?.Muzzle : target == PickTarget.Eject ? _c.Setup?.Eject : null;
		if ( point is null || !Camera.IsValid() || !_weaponObject.IsValid() )
			return null;
		return ToScreen( WeaponWorld.PointToWorld( Core.Setup.V.Of( point.Canonical ).ToEngine() ), out var s ) ? s : null;
	}

	/// <summary>Middle of a forearm on screen (gate and tests), or null.</summary>
	public Vector2? ArmScreenPosition( Side side )
	{
		if ( !Camera.IsValid() || !_body.IsValid() || !_body.SceneModel.IsValid() )
			return null;
		foreach ( var (bone, parent) in _armBones )
		{
			if ( SideOf( bone ) != side || !_body.Model.GetBoneName( bone ).StartsWith( "hand", StringComparison.OrdinalIgnoreCase ) )
				continue;
			var mid = (_body.SceneModel.GetBoneWorldTransform( parent ).Position + _body.SceneModel.GetBoneWorldTransform( bone ).Position) * 0.5f;
			return ToScreen( mid, out var s ) ? s : null;
		}
		return null;
	}

	public int FirstPersonPieceCount => _fpPieces.Count;

	/// <summary>The hand whose grip contact marker is under a viewport pixel (drag to move the grip).</summary>
	public Side? HitGripContact( Vector2 local )
	{
		var setup = _c.Setup;
		if ( setup is null || !_c.ShowContacts || !Camera.IsValid() || !_weaponObject.IsValid() || OwnArmsView )
			return null;
		var weapon = WeaponWorld;
		Side? best = null;
		var bestDistance = MarkerRadiusPx;
		foreach ( var (side, grip) in new[] { (Side.Right, setup.Primary), (Side.Left, setup.UseSupportHand ? setup.Support : null) } )
		{
			if ( grip is null || !ToScreen( weapon.PointToWorld( Core.Setup.V.Of( grip.Contact ).ToEngine() ), out var screen ) )
				continue;
			var d = (screen - local).Length;
			if ( d < bestDistance )
			{
				bestDistance = d;
				best = side;
			}
		}
		return best;
	}

	/// <summary>Hand grip being dragged across the weapon, or null.</summary>
	private Side? _dragGrip;

	/// <summary>Starts dragging a grip contact when the press lands on one.</summary>
	private bool BeginGripDrag( Vector2 local )
	{
		if ( _c.Pick != PickTarget.None || HitGripContact( local ) is not { } side )
			return false;
		_dragGrip = side;
		Cursor = CursorShape.ClosedHand;
		_c.SetStatus( $"Moving the {(side == Side.Right ? "right" : "left")} grip: drag across the weapon.", Theme.Blue );
		return true;
	}

	private void DragGrip( Vector2 local )
	{
		if ( _dragGrip is not { } side )
			return;
		if ( RaycastWeapon( local ) is { } hit )
			_c.DragGrip( side, hit.Point, hit.Normal );
	}

	private void EndGripDrag()
	{
		if ( _dragGrip is null )
			return;
		_dragGrip = null;
		Cursor = CursorShape.Arrow;
		_c.EndDragGrip();
	}

	/// <summary>A plain left click (no drag) in the preview.</summary>
	private void Click( Vector2 local )
	{
		var pick = _c.Pick;
		if ( pick is PickTarget.Muzzle or PickTarget.Eject )
		{
			// Another marker selects that one; the weapon surface moves the selected one; anything else deselects.
			var marker = HitMarker( local );
			if ( marker != PickTarget.None && marker != pick )
			{
				_c.BeginPick( marker );
				return;
			}
			if ( RaycastWeapon( local ) is { } hit )
				_c.CompletePick( hit.Point, hit.Normal );
			else
			{
				_c.BeginPick( PickTarget.None );
				_c.SetStatus( "Marker deselected.", Theme.TextLight );
			}
			return;
		}
		if ( pick != PickTarget.None )
		{
			if ( RaycastWeapon( local ) is { } hit )
				_c.CompletePick( hit.Point, hit.Normal );
			else
				_c.SetStatus( "That missed the weapon. " + ImporterController.PickHint( pick ), Theme.Yellow );
			return;
		}

		var clicked = HitMarker( local );
		if ( clicked != PickTarget.None )
		{
			_c.BeginPick( clicked );
			return;
		}
		if ( HitArm( local ) is { } side )
			_c.ToggleHand( _c.Role, side );
	}
}
