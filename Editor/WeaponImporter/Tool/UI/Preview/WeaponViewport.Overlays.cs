using Editor;
using Sandbox;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Setup;
using N = System.Numerics;

namespace WeaponImporter.Tool.UI;

public sealed partial class WeaponViewport
{
	private static readonly Color Bones = new( 1f, .58f, .16f );
	private static readonly Color RightColor = new( .3f, 1f, .4f );
	private static readonly Color LeftColor = new( .25f, .6f, 1f );
	private static readonly Color Highlight = new( 1f, .35f, .8f );
	private static readonly Color Muzzle = new( 1f, .3f, .25f );
	private static readonly Color Eject = new( 1f, .85f, .2f );

	private Vector2 _hoverPosition;
	private N.Vector3 _probePoint = new( float.NaN );
	private GripSurface _probe;

	/// <summary>A ray from a viewport pixel into canonical weapon space, hitting the weapon's triangles.</summary>
	public (N.Vector3 Point, N.Vector3 Normal)? RaycastWeapon( Vector2 local )
	{
		var analysis = _c.Analysis;
		if ( analysis is null || !_weaponObject.IsValid() || !Camera.IsValid() )
			return null;
		var ray = GetRay( local );
		var weapon = WeaponWorld;
		var origin = weapon.PointToLocal( ray.Position );
		var direction = (weapon.Rotation.Inverse * ray.Forward).Normal;
		var hit = analysis.WeaponBvh.Raycast( origin.ToCore(), direction.ToCore(), 10000f );
		if ( hit is not { } h )
			return null;
		var normal = h.Normal;
		if ( N.Vector3.Dot( normal, direction.ToCore() ) > 0 )
			normal = -normal;
		return (h.Point, normal);
	}

	private void DrawOverlays()
	{
		var session = _c.Session;
		UpdateGizmoInputs( false );
		using var scope = Gizmo.Scope( "weapon-importer" );
		Gizmo.Transform = Transform.Zero;
		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.LineThickness = 1f;
		if ( !OwnArmsView )
			DrawFloor();
		if ( session?.Setup is null )
			return;
		// First person with the file's own arms is view only: the same markers, following the
		// weapon as its animation moves it, without picking or IK targets.
		var own = OwnArmsView;
		if ( !own && !_weaponObject.IsValid() )
			return;

		var setup = session.Setup;
		var weapon = WeaponWorld;
		Gizmo.Draw.IgnoreDepth = true;

		// Arms: orange while the hand is on the weapon for this action, gray while it plays its
		// own animation; the arm under the cursor lights up (click toggles it).
		var hoverArm = !own && _c.Pick == PickTarget.None && IsUnderMouse && HitMarker( _hoverPosition ) == PickTarget.None ? HitArm( _hoverPosition ) : null;
		if ( own )
		{
			if ( _c.ShowSkeleton )
				DrawOwnArms();
		}
		else if ( (_c.ShowSkeleton || hoverArm is not null) && _body.IsValid() && _body.SceneModel.IsValid() )
		{
			foreach ( var (bone, parent) in _armBones )
			{
				var side = SideOf( bone );
				var hovered = side is not null && side == hoverArm;
				if ( !_c.ShowSkeleton && !hovered )
					continue;
				var released = side is { } s && _c.IsHandReleased( _c.Role, s );
				Gizmo.Draw.LineThickness = hovered ? 3f : 1.5f;
				Gizmo.Draw.Color = hovered ? Color.White : released ? Theme.TextLight.WithAlpha( .6f ) : Bones.WithAlpha( .85f );
				var a = _body.SceneModel.GetBoneWorldTransform( parent ).Position;
				var b = _body.SceneModel.GetBoneWorldTransform( bone ).Position;
				Gizmo.Draw.Line( a, b );
			}
		}

		if ( (_c.ShowRegions || _c.HighlightRegion is not null) && _c.Analysis is { } regionsOf )
			DrawRegions( weapon, regionsOf );

		if ( _c.ShowContacts || _c.ShowNormals )
		{
			DrawGrip( weapon, setup.Primary, _c.RightSurface, RightColor, true );
			if ( setup.UseSupportHand && setup.Support is not null )
				DrawGrip( weapon, setup.Support, _c.LeftSurface, LeftColor, true );
		}

		if ( !own && _c.ShowIkTargets && _hold.IsValid() )
		{
			if ( WeaponHold.TryParse( _hold.RightHand, out var r ) )
				Triad( weapon.ToWorld( r ), 3f );
			if ( setup.UseSupportHand && WeaponHold.TryParse( _hold.LeftHand, out var l ) )
				Triad( weapon.ToWorld( l ), 3f );
		}

		if ( _c.ShowAttachments || _c.Pick is PickTarget.Muzzle or PickTarget.Eject )
		{
			var hoverMarker = !own && _c.Pick == PickTarget.None && IsUnderMouse ? HitMarker( _hoverPosition ) : PickTarget.None;
			DrawPoint( weapon, setup.Muzzle, Muzzle, 5f, _c.Pick == PickTarget.Muzzle, hoverMarker == PickTarget.Muzzle );
			DrawPoint( weapon, setup.Eject, Eject, 3.5f, _c.Pick == PickTarget.Eject, hoverMarker == PickTarget.Eject );
		}

		if ( !string.IsNullOrEmpty( _c.HighlightBone ) && _c.Analysis is { } analysis )
		{
			var skeleton = analysis.Asset.Skeleton;
			var index = skeleton.IndexOf( _c.HighlightBone );
			if ( index >= 0 )
			{
				// First person shows the bone where the animation has it.
				Vector3 At( int bone ) => own && FirstPersonBone( bone ) is { } animated ? animated : weapon.PointToWorld( skeleton.RestWorld[bone].Pos.ToEngine() );
				var p = At( index );
				Gizmo.Draw.Color = Highlight;
				Gizmo.Draw.LineThickness = 2f;
				Gizmo.Draw.LineSphere( p, 0.8f, 8 );
				var parent = skeleton[index].ParentIndex;
				if ( parent >= 0 )
					Gizmo.Draw.Line( At( parent ), p );
				Gizmo.Draw.ScreenText( _c.HighlightBone, p, new Vector2( 10, -8 ), "Roboto", 12, TextFlag.LeftTop );
			}
		}

		if ( own )
		{
			DrawFirstPersonHud();
			return;
		}
		if ( _c.Pick != PickTarget.None )
			DrawPickProbe( weapon );

		DrawHud();
	}

	/// <summary>The file's own first-person arms as bone lines (orange, like the character's).</summary>
	private void DrawOwnArms()
	{
		var analysis = _c.Analysis;
		if ( analysis is null )
			return;
		var skeleton = analysis.Asset.Skeleton;
		Gizmo.Draw.LineThickness = 1.5f;
		Gizmo.Draw.Color = Bones.WithAlpha( .85f );
		if ( _fpArmSegments.Count > 0 )
		{
			foreach ( var (parent, bone) in _fpArmSegments )
				if ( FirstPersonBone( parent ) is { } a && FirstPersonBone( bone ) is { } b )
					Gizmo.Draw.Line( a, b );
			return;
		}
		foreach ( var bone in analysis.ArmBones )
		{
			var parent = skeleton[bone].ParentIndex;
			if ( parent < 0 || !analysis.ArmBones.Contains( parent ) || !IsOwnArmDisplayBone( bone ) || !IsOwnArmDisplayBone( parent ) )
				continue;
			if ( FirstPersonBone( parent ) is { } a && FirstPersonBone( bone ) is { } b )
				Gizmo.Draw.Line( a, b );
		}
	}

	public static Color RegionColor( Core.Grip.GripRegionKind kind ) => kind switch
	{
		Core.Grip.GripRegionKind.PrimaryGrip => new Color( .3f, 1f, .4f ),
		Core.Grip.GripRegionKind.SecondaryGrip => new Color( .25f, .6f, 1f ),
		Core.Grip.GripRegionKind.Trigger => new Color( 1f, .85f, .2f ),
		Core.Grip.GripRegionKind.Support => new Color( 1f, .58f, .16f ),
		_ => new Color( 1f, .3f, .3f ),
	};

	/// <summary>Semantic regions as boxes on the weapon; the highlighted one is drawn stronger.</summary>
	private void DrawRegions( Transform weapon, Core.Analysis.WeaponAnalysis analysis )
	{
		foreach ( var region in Core.Grip.GripRegions.Of( analysis ) )
		{
			var highlighted = _c.HighlightRegion == region.Kind;
			if ( !_c.ShowRegions && !highlighted )
				continue;
			var c = region.Center.ToEngine();
			var h = region.HalfExtents.ToEngine();
			var color = RegionColor( region.Kind );
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = highlighted ? 2.5f : 1f;
			Gizmo.Draw.Color = color.WithAlpha( highlighted ? 1f : .55f );
			using ( Gizmo.Scope( $"region-{region.Kind}", weapon ) )
				Gizmo.Draw.LineBBox( new BBox( c - h, c + h ) );
			Gizmo.Draw.IgnoreDepth = false;
		}
	}

	private void DrawGrip( Transform weapon, GripSetup grip, Core.Analysis.GripCandidate measured, Color color, bool ring )
	{
		if ( grip is null || !grip.Enabled )
			return;
		var contact = weapon.PointToWorld( V.Of( grip.Contact ).ToEngine() );
		var normal = weapon.Rotation * V.Of( grip.Normal ).ToEngine();
		if ( _c.ShowContacts )
		{
			Gizmo.Draw.Color = color;
			Gizmo.Draw.SolidSphere( contact, 0.35f, 8, 8 );
			if ( ring && measured?.Surface is { } s )
			{
				Gizmo.Draw.LineThickness = 1.5f;
				Gizmo.Draw.Color = color.WithAlpha( .7f );
				var axis = weapon.Rotation * s.Axis.ToEngine();
				Gizmo.Draw.LineCircle( weapon.PointToWorld( s.Center.ToEngine() ), axis, s.Radius, 0, 360, 24 );
			}
		}
		if ( _c.ShowNormals )
		{
			Gizmo.Draw.LineThickness = 1.5f;
			Gizmo.Draw.Color = color;
			Gizmo.Draw.Arrow( contact, contact + normal * 3f, 0.8f, 0.35f );
		}
	}

	private void DrawPoint( Transform weapon, PointSetup point, Color color, float length, bool selected, bool hovered )
	{
		// First person: the point rides on its own bone (a slide, a bolt) as the animation moves it.
		if ( point is not null && OwnArmsView && _c.Analysis?.Asset.Skeleton.IndexOf( point.Bone ) is { } pointBone && FirstPersonBoneFrame( pointBone ) is { } frame )
			weapon = frame;
		if ( point is not null && (selected || hovered) )
		{
			// Selected (or about to be): a ring that faces the camera around the marker.
			var at = weapon.PointToWorld( V.Of( point.Canonical ).ToEngine() );
			Gizmo.Draw.LineThickness = selected ? 2.5f : 1.5f;
			Gizmo.Draw.Color = selected ? Color.White : color.WithAlpha( .8f );
			Gizmo.Draw.LineCircle( at, (Camera.WorldPosition - at).Normal, 0.9f, 0, 360, 24 );
		}
		if ( point is null || _c.Analysis is not { } analysis )
			return;
		var skeleton = analysis.Asset.Skeleton;
		var bone = skeleton.IndexOf( point.Bone );
		var rest = bone >= 0 ? skeleton.RestWorld[bone] : Core.Maths.XForm.Identity;
		// Only the direction is needed from the bone-local transform; the position is stored in canonical space too.
		var canonical = Core.Maths.XForm.Compose( rest, new Core.Maths.XForm( N.Vector3.Zero, V.Q( point.Rotation ) ) );
		var origin = weapon.PointToWorld( V.Of( point.Canonical ).ToEngine() );
		var direction = weapon.Rotation * N.Vector3.Transform( N.Vector3.UnitX, canonical.Rot ).ToEngine();
		Gizmo.Draw.LineThickness = 2f;
		Gizmo.Draw.Color = color;
		Gizmo.Draw.SolidSphere( origin, 0.3f, 6, 6 );
		Gizmo.Draw.Arrow( origin, origin + direction * length, 1.2f, 0.5f );
	}

	/// <summary>A quiet floor grid (one foot squares) fading out towards the edges, without colored axes.</summary>
	private static void DrawFloor()
	{
		const int cells = 12;
		const float step = 12f;
		const float extent = cells * step;
		Gizmo.Draw.LineThickness = 1f;
		for ( var i = -cells; i <= cells; i++ )
		{
			var fade = 1f - MathF.Abs( i ) / (cells + 1f);
			Gizmo.Draw.Color = Color.White.WithAlpha( (i == 0 ? .1f : .05f) * fade + .01f );
			var o = i * step;
			Gizmo.Draw.Line( new Vector3( o, -extent, 0 ), new Vector3( o, extent, 0 ) );
			Gizmo.Draw.Line( new Vector3( -extent, o, 0 ), new Vector3( extent, o, 0 ) );
		}
	}

	private static void Triad( Transform t, float size )
	{
		Gizmo.Draw.LineThickness = 2f;
		Gizmo.Draw.Color = Color.Red;
		Gizmo.Draw.Line( t.Position, t.Position + t.Rotation.Forward * size );
		Gizmo.Draw.Color = Color.Green;
		Gizmo.Draw.Line( t.Position, t.Position + t.Rotation.Left * size );
		Gizmo.Draw.Color = Color.Blue;
		Gizmo.Draw.Line( t.Position, t.Position + t.Rotation.Up * size );
	}

	private void DrawPickProbe( Transform weapon )
	{
		if ( !IsUnderMouse )
			return;
		var hit = RaycastWeapon( _hoverPosition );
		if ( hit is not { } h )
			return;
		var picking = _c.Pick;
		var color = picking switch
		{
			PickTarget.LeftGrip => LeftColor,
			PickTarget.Muzzle => Muzzle,
			PickTarget.Eject => Eject,
			_ => RightColor,
		};
		var p = weapon.PointToWorld( h.Point.ToEngine() );
		var n = weapon.Rotation * h.Normal.ToEngine();
		Gizmo.Draw.Color = color;
		Gizmo.Draw.SolidSphere( p, 0.3f, 8, 8 );
		Gizmo.Draw.LineThickness = 1.5f;
		Gizmo.Draw.Arrow( p, p + n * 3f, 0.8f, 0.35f );
		if ( picking is PickTarget.RightGrip or PickTarget.LeftGrip && _c.Analysis is { } analysis )
		{
			if ( N.Vector3.DistanceSquared( _probePoint, h.Point ) > 0.04f )
			{
				_probePoint = h.Point;
				var hint = picking == PickTarget.RightGrip ? N.Vector3.UnitZ : N.Vector3.UnitX;
				_probe = SurfaceProbe.FromPoint( analysis.WeaponBvh, h.Point + h.Normal * 0.05f, hint );
			}
			if ( _probe is { } s )
			{
				Gizmo.Draw.Color = color.WithAlpha( .8f );
				Gizmo.Draw.LineCircle( weapon.PointToWorld( s.Center.ToEngine() ), weapon.Rotation * s.Axis.ToEngine(), s.Radius, 0, 360, 28 );
				Gizmo.Draw.ScreenText( $"r {s.Radius:0.00} in", p, new Vector2( 12, 6 ), "Roboto", 12, TextFlag.LeftTop );
			}
		}
	}

	private void DrawHud()
	{
		var lines = new List<(string Text, Color Color)>();
		if ( _c.Solving )
			lines.Add( ("Fitting hands…", Theme.Yellow) );
		if ( _c.Pick != PickTarget.None )
			lines.Add( (ImporterController.PickHint( _c.Pick ), Theme.Blue) );
		var y = 10f;
		foreach ( var (text, color) in lines )
		{
			Gizmo.Draw.Color = color;
			Gizmo.Draw.ScreenText( text, new Vector2( 12, y ), "Roboto", 13, TextFlag.LeftTop );
			y += 18f;
		}
	}
}
