using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

public sealed partial class WeaponViewport
{
	private float _yaw = 145f;
	private float _pitch = 12f;
	private float _distance = 70f;
	private Vector3 _target = new( 0, 0, 48 );
	private int _frameIn = 6; // frames until the first framing (the hold must have placed the weapon)
	private Vector2 _lastMouse;
	private Vector2 _pressAt;
	private bool _dragged;

	private Transform CharacterRoot => _characterObject.IsValid() ? _characterObject.WorldTransform : Transform.Zero;

	/// <summary>Orbit around the upper body, from the front right (the weapon side).</summary>
	public void FrameCharacter()
	{
		var root = CharacterRoot;
		var chest = root.PointToWorld( new Vector3( 4, 0, 50 ) );
		_target = _weaponObject.IsValid() && _c.Session?.Grip is not null ? Vector3.Lerp( chest, WeaponWorld.Position, 0.6f ) : chest;
		_yaw = 145f;
		_pitch = 10f;
		_distance = 84f;
	}

	/// <summary>Frame the weapon (F).</summary>
	public void FrameWeapon()
	{
		var analysis = _c.Analysis;
		if ( analysis is null || !_weaponObject.IsValid() )
		{
			FrameCharacter();
			return;
		}
		var size = analysis.WeaponBounds.Size;
		var center = analysis.WeaponBounds.Center;
		var radius = MathF.Max( 6f, new Vector3( size.X, size.Y, size.Z ).Length * 0.5f ) * 1.35f;
		if ( CameraMode != ViewportCamera.Orbit )
			SetCamera( ViewportCamera.Orbit );
		_target = WeaponWorld.PointToWorld( new Vector3( center.X, center.Y, center.Z ) );
		_distance = MathX.SphereCameraDistance( radius, Camera.FieldOfView );
	}

	public event Action CameraModeChanged;

	public void SetCamera( ViewportCamera mode )
	{
		if ( CameraMode == mode )
			return;
		CameraMode = mode;
		CameraModeChanged?.Invoke();
	}

	private void UpdateCamera()
	{
		if ( !Camera.IsValid() )
			return;
		var root = CharacterRoot;
		var forward = root.Rotation.Forward;
		var right = root.Rotation.Right;
		var up = Vector3.Up;
		var weapon = _weaponObject.IsValid() ? WeaponWorld.Position : root.PointToWorld( new Vector3( 12, -6, 50 ) );
		switch ( CameraMode )
		{
			case ViewportCamera.FirstPerson:
			{
				// Through the character's eyes, looking where it aims: its own arms hold the weapon.
				var eyes = _body.IsValid() ? _body.GetAttachment( "eyes", true ) : null;
				var eye = eyes?.Position ?? root.Position + up * 64f;
				// The third-person hold carries the weapon low; look down at it like a viewmodel
				// shows it, a little past the weapon so the barrel stays in frame.
				var look = weapon + forward * 16f;
				Camera.FieldOfView = 70f;
				Camera.ZNear = 3f; // the head sits around the camera
				Camera.WorldPosition = eye + forward * 1.5f;
				Camera.WorldRotation = Rotation.LookAt( (look - Camera.WorldPosition).Normal, Vector3.Up );
				return;
			}
			case ViewportCamera.Gameplay:
			{
				var eye = root.Position + forward * -46f + right * 18f + up * 68f;
				var look = root.Position + forward * 70f + right * 2f + up * 58f;
				Camera.FieldOfView = 60f;
				Camera.WorldPosition = eye;
				Camera.WorldRotation = Rotation.LookAt( (look - eye).Normal, Vector3.Up );
				return;
			}
			case ViewportCamera.Front:
			{
				var eye = weapon + forward * 62f + up * 4f;
				Camera.FieldOfView = 45f;
				Camera.WorldPosition = eye;
				Camera.WorldRotation = Rotation.LookAt( (weapon - eye).Normal, Vector3.Up );
				return;
			}
			case ViewportCamera.Side:
			{
				var eye = weapon + right * 58f + up * 3f;
				Camera.FieldOfView = 45f;
				Camera.WorldPosition = eye;
				Camera.WorldRotation = Rotation.LookAt( (weapon - eye).Normal, Vector3.Up );
				return;
			}
			default:
			{
				var rotation = Rotation.From( _pitch, _yaw, 0 );
				Camera.FieldOfView = 45f;
				Camera.WorldPosition = _target - rotation.Forward * _distance;
				Camera.WorldRotation = rotation;
				return;
			}
		}
	}

	/// <summary>Leaves a fixed camera for orbit at the same place, so dragging feels continuous.</summary>
	private void TakeOverCamera()
	{
		if ( CameraMode is ViewportCamera.Orbit or ViewportCamera.FirstPerson || !Camera.IsValid() )
			return;
		var rotation = Camera.WorldRotation;
		var angles = rotation.Angles();
		_pitch = angles.pitch;
		_yaw = angles.yaw;
		var weapon = _weaponObject.IsValid() ? WeaponWorld.Position : _target;
		_distance = MathF.Max( 10f, (weapon - Camera.WorldPosition).Length );
		_target = Camera.WorldPosition + rotation.Forward * _distance;
		SetCamera( ViewportCamera.Orbit );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		_lastMouse = e.LocalPosition;
		_pressAt = e.LocalPosition;
		_dragged = false;
		Focus();
		if ( e.LeftMouseButton )
			BeginGripDrag( e.LocalPosition );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		var delta = e.LocalPosition - _lastMouse;
		_lastMouse = e.LocalPosition;
		_hoverPosition = e.LocalPosition;
		if ( (e.LocalPosition - _pressAt).Length > 3 )
			_dragged = true;
		var buttons = e.ButtonState;
		if ( _dragGrip is not null )
		{
			if ( (buttons & MouseButtons.Left) != 0 )
				DragGrip( e.LocalPosition );
			else
				EndGripDrag();
			return;
		}
		// A grip contact under the cursor can be grabbed.
		if ( buttons == 0 )
			Cursor = HitGripContact( e.LocalPosition ) is not null ? CursorShape.OpenHand : CursorShape.Arrow;
		if ( (buttons & MouseButtons.Left) != 0 && _c.Pick == PickTarget.None )
		{
			TakeOverCamera();
			_yaw -= delta.x * 0.4f;
			_pitch = Math.Clamp( _pitch + delta.y * 0.3f, -85f, 85f );
		}
		else if ( (buttons & (MouseButtons.Right | MouseButtons.Middle)) != 0 )
		{
			TakeOverCamera();
			var rotation = Rotation.From( _pitch, _yaw, 0 );
			var scale = _distance * 0.0016f;
			_target += (rotation.Right * -delta.x + rotation.Up * delta.y) * scale;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( _dragGrip is not null )
		{
			EndGripDrag();
			return;
		}
		if ( e.LeftMouseButton && !_dragged )
			Click( e.LocalPosition );
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		TakeOverCamera();
		_distance = Math.Clamp( _distance * (e.Delta > 0 ? 0.88f : 1.12f), 4f, 600f );
		e.Accept();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.F )
		{
			FrameWeapon();
			e.Accepted = true;
			return;
		}
		if ( e.Key == KeyCode.Escape && _c.Pick != PickTarget.None )
		{
			_c.BeginPick( PickTarget.None );
			_c.SetStatus( "Picking cancelled.", Theme.TextLight );
			e.Accepted = true;
			return;
		}
		base.OnKeyPress( e );
	}
}
