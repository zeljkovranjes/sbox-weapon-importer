using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Maths;
using CoreClip = WeaponImporter.Core.Rig.Clip;
using CorePose = WeaponImporter.Core.Rig.Pose;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// View-only first-person preview: the imported file's own animation (weapon and, when the file
/// has them, its first-person arms) exactly as authored, before anything is compiled. The mesh is
/// rigidly skinned to each triangle's dominant bone: one render piece per bone, moved every frame
/// by boneWorld(frame) · restWorld⁻¹, so nothing is uploaded per frame.
/// </summary>
public sealed partial class WeaponViewport
{
	/// <summary>Keeps huge files responsive (the Uzi pack is 42k triangles / 1072 bones).</summary>
	private const int MaxFirstPersonTriangles = 120_000;

	private GameObject _fpRoot;
	private readonly List<(int Bone, GameObject Piece)> _fpPieces = new();
	private WeaponAnalysis _fpAnalysis;
	private XForm[] _fpInverseRest;
	private int _fpCameraBone = -1;
	private Material[] _fpMaterials;
	private int _fpFrame = -1;
	private CoreClip _fpClip;

	// The bone most of the weapon's triangles follow, and the pose shown this frame (canonical
	// space, which is also the first-person scene's world space).
	private int _fpWeaponBone = -1;
	private XForm[] _fpWorld;

	/// <summary>The weapon (canonical space) in the first-person scene as its animation moves it.</summary>
	public Transform FirstPersonWeaponWorld => _fpWorld is { } world && _fpWeaponBone >= 0 && _fpWeaponBone < world.Length
		? XForm.Compose( world[_fpWeaponBone], _fpInverseRest[_fpWeaponBone] ).ToEngine()
		: Transform.Zero;

	/// <summary>
	/// How a weapon part's bone has moved the canonical weapon this frame: the bone itself when it
	/// is the weapon's bone or one of its children (a slide, a bolt), else the weapon's bone (a
	/// point parented to the rig root must still follow the weapon). Null outside first person.
	/// </summary>
	public Transform? FirstPersonBoneFrame( int bone )
	{
		if ( _fpWorld is not { } world || _fpWeaponBone < 0 )
			return null;
		var skeleton = _fpAnalysis.Asset.Skeleton;
		var follow = _fpWeaponBone;
		for ( var b = bone; b >= 0 && b < skeleton.Count; b = skeleton[b].ParentIndex )
			if ( b == _fpWeaponBone )
			{
				follow = bone;
				break;
			}
		return XForm.Compose( world[follow], _fpInverseRest[follow] ).ToEngine();
	}

	// The file's own arms as anatomical segments (upper arm, forearm, finger joints).
	private readonly List<(int Parent, int Bone)> _fpArmSegments = new();

	/// <summary>Arm bones worth drawing in first person: forearms, hands and fingers (no body, no camera).</summary>
	private bool IsOwnArmDisplayBone( int bone )
	{
		var skeleton = _fpAnalysis?.Asset.Skeleton;
		return skeleton is not null && bone >= 0 && bone < skeleton.Count && !IsBodyBone( skeleton, bone )
			&& !skeleton[bone].Name.Contains( "camera", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>A bone of the imported file in the first-person scene this frame.</summary>
	public Vector3? FirstPersonBone( int bone ) => _fpWorld is { } world && bone >= 0 && bone < world.Length ? world[bone].Pos.ToEngine() : null;

	public bool FirstPerson => CameraMode == ViewportCamera.FirstPerson;

	/// <summary>
	/// First person with the file's own arms (its first-person rig plays as authored). Weapons without
	/// arms show first person through the character's eyes instead: its arms, the fitted grip and the
	/// same correction as third person.
	/// </summary>
	public bool OwnArmsView => FirstPerson && (_c.Analysis?.ArmBones.Count ?? 0) > 0;

	/// <summary>The weapon's own clip for a role (or the role it falls back to), or null.</summary>
	public CoreClip WeaponClip( AnimationRole role )
	{
		var setup = _c.Setup;
		var asset = _c.Analysis?.Asset;
		if ( setup is null || asset is null )
			return null;
		for ( AnimationRole? r = role; r is { } current; r = AnimationRoles.Fallback( current ) )
			if ( setup.WeaponAnimations.TryGetValue( current, out var b ) && !string.IsNullOrEmpty( b.Clip ) && asset.FindClip( b.Clip ) is { FrameCount: > 0 } clip )
				return clip;
		return null;
	}

	private void EnsureFirstPersonPieces()
	{
		var analysis = _c.Analysis;
		if ( analysis is null || ReferenceEquals( analysis, _fpAnalysis ) && ReferenceEquals( _fpMaterials, _materials ) && _fpRoot.IsValid() )
			return;
		_fpAnalysis = analysis;
		_fpMaterials = _materials;
		_fpRoot?.Destroy();
		_fpPieces.Clear();
		_fpRoot = new GameObject( true, "first person" );

		var asset = analysis.Asset;
		var mesh = asset.Mesh;
		var skeleton = asset.Skeleton;
		var byBone = WeaponImporter.Core.Rig.ViewmodelParts.TrianglesByBone( analysis, Math.Max( 1, (mesh.TriangleCount + MaxFirstPersonTriangles - 1) / MaxFirstPersonTriangles ) );
		foreach ( var (bone, triangles) in byBone )
		{
			var piece = new GameObject( _fpRoot, true, skeleton.Count > 0 ? skeleton[bone].Name : "mesh" );
			var renderer = piece.AddComponent<ModelRenderer>();
			renderer.Model = PreviewModels.Build( mesh, triangles, _materials );
			_fpPieces.Add( (bone, piece) );
		}

		_fpInverseRest = new XForm[skeleton.Count];
		for ( var i = 0; i < skeleton.Count; i++ )
			_fpInverseRest[i] = skeleton.RestWorld[i].Inverse();
		_fpCameraBone = WeaponImporter.Core.Generation.FirstPersonRig.FindCamera( analysis );
		// Without a camera bone, the eye the baked viewmodel uses (the file's origin, the character's eyes...).
		var rig = analysis.ArmBones.Count > 0 ? WeaponImporter.Core.Generation.FirstPersonRig.Build( analysis ) : null;
		_fpRigEye = _fpCameraBone < 0 ? rig?.Eye.ToEngine() : null;
		// The camera's viewing axes as the baked viewmodel uses them.
		_fpCameraAxes = rig is not null && _fpCameraBone >= 0 ? rig.CameraAxes.ToEngine() : null;
		_fpFrame = -1;
		_fpClip = null;
		_fpWorld = null;
		_fpArmSegments.Clear();
		foreach ( var side in new[] { WeaponImporter.Core.Hands.Side.Right, WeaponImporter.Core.Hands.Side.Left } )
		{
			if ( analysis.ArmBones.Count == 0 || WeaponImporter.Core.Hands.HandRig.Build( skeleton, side ) is not { } arm || !analysis.ArmBones.Contains( arm.Hand ) )
				continue;
			_fpArmSegments.Add( (arm.UpperArm, arm.LowerArm) );
			_fpArmSegments.Add( (arm.LowerArm, arm.Hand) );
			foreach ( var finger in arm.Fingers )
			{
				var previous = arm.Hand;
				foreach ( var joint in finger.Joints )
				{
					_fpArmSegments.Add( (previous, joint) );
					previous = joint;
				}
			}
		}
		_fpWeaponBone = analysis.WeaponTriangles.Length == 0 || skeleton.Count == 0 ? -1
			: analysis.WeaponTriangles.GroupBy( t => Math.Clamp( mesh.TriangleBone( t ), 0, skeleton.Count - 1 ) ).MaxBy( g => g.Count() )!.Key;
	}

	private static bool IsBodyBone( WeaponImporter.Core.Rig.Skeleton skeleton, int bone ) => WeaponImporter.Core.Rig.ViewmodelParts.IsBodyBone( skeleton, bone );

	/// <summary>One first-person frame: advance time, pose the pieces, place the camera.</summary>
	private void TickFirstPerson( float dt )
	{
		EnsureFirstPersonPieces();
		if ( _fpRoot.IsValid() )
			_fpRoot.Enabled = true;
		var role = _c.Role;
		var clip = WeaponClip( role );
		if ( _c.Playing )
		{
			if ( _c.RestartRequested )
			{
				_c.RestartRequested = false;
				_c.Time = _c.Time >= 0.999f ? 0f : _c.Time;
			}
			else
			{
				_c.Time += dt * _c.Speed / Seconds( role );
				if ( _c.Time >= 1f )
				{
					if ( _c.Loop || AnimationRoles.Loops( role ) )
						_c.Time %= 1f;
					else
					{
						_c.Time = 1f;
						_c.Playing = false;
					}
				}
			}
		}
		TickScene( dt );

		var skeleton = _fpAnalysis?.Asset.Skeleton;
		if ( skeleton is null || skeleton.Count == 0 )
			return;
		var frame = clip is null ? -1 : Math.Clamp( (int)MathF.Round( _c.Time * (clip.FrameCount - 1) ), 0, clip.FrameCount - 1 );
		if ( frame != _fpFrame || !ReferenceEquals( clip, _fpClip ) )
		{
			_fpFrame = frame;
			_fpClip = clip;
			var world = clip is null ? skeleton.RestWorld.ToArray() : new CorePose( clip.Frames[frame] ).ToWorld( skeleton );
			_fpWorld = world;
			foreach ( var (bone, piece) in _fpPieces )
				if ( piece.IsValid() )
					piece.WorldTransform = XForm.Compose( world[bone], _fpInverseRest[bone] ).ToEngine();
			_fpEye = _fpCameraBone >= 0 ? world[_fpCameraBone].ToEngine() : (Transform?)null;
		}
		UpdateFirstPersonCamera();
	}

	private Transform? _fpEye;
	private Transform? _fpRigEye;
	private Rotation? _fpCameraAxes;

	private void UpdateFirstPersonCamera()
	{
		var analysis = _fpAnalysis;
		if ( analysis is null || !Camera.IsValid() )
			return;
		var b = analysis.WeaponBounds;
		var center = new Vector3( b.Center.X, b.Center.Y, b.Center.Z );
		var length = MathF.Max( 4f, b.Size.X );
		Vector3 eye, look;
		var up = Vector3.Up;
		if ( _fpEye is { } cameraBone )
		{
			// The rig's own camera, looking the way it was authored. Exporters disagree on the
			// camera's axes: forward is the bone axis pointing most at the weapon, up the one
			// closest to world up. Aim at the weapon when no axis fits.
			eye = cameraBone.Position;
			if ( _fpCameraAxes is { } fix )
			{
				// The baked viewmodel's view: the camera bone turned by its axis fix.
				var view = cameraBone.Rotation * fix;
				look = eye + view.Forward * 10f;
				up = view.Up;
				goto placed;
			}
			var toWeapon = (center - eye).Normal;
			var axes = new[] { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down }
				.Select( a => cameraBone.Rotation * a ).ToArray();
			var forward = axes.OrderByDescending( a => Vector3.Dot( a, toWeapon ) ).First();
			if ( Vector3.Dot( forward, toWeapon ) > 0.6f )
			{
				up = axes.Where( a => MathF.Abs( Vector3.Dot( a, forward ) ) < 0.5f ).OrderByDescending( a => Vector3.Dot( a, Vector3.Up ) ).First();
				look = eye + forward * 10f;
			}
			else
			{
				look = center;
			}
		}
		else if ( _fpRigEye is { } rigEye )
		{
			eye = rigEye.Position;
			look = eye + rigEye.Rotation.Forward * 10f;
		}
		else
		{
			// No camera in the file (weapon only): a steady three-quarter view from behind the
			// right shoulder, far enough that moving parts stay in frame.
			var radius = MathF.Max( length, MathF.Max( b.Size.Y, b.Size.Z ) ) * 0.75f;
			var distance = MathX.SphereCameraDistance( radius, 60f );
			var dir = new Vector3( -0.72f, -0.55f, 0.42f ).Normal;
			eye = center + dir * distance;
			look = center;
		}
		placed:
		Camera.FieldOfView = 60f;
		Camera.ZNear = _fpEye is null ? 0.5f : 2f; // clip the arm stubs right at a rig camera
		Camera.WorldPosition = eye;
		Camera.WorldRotation = Rotation.LookAt( (look - eye).Normal, up );
	}

	private void HideFirstPerson()
	{
		if ( _fpRoot.IsValid() )
			_fpRoot.Enabled = false;
		if ( Camera.IsValid() )
			Camera.ZNear = 1f;
	}

	private void DrawFirstPersonHud()
	{
		var clip = WeaponClip( _c.Role );
		Gizmo.Draw.Color = Theme.TextLight;
		var text = clip is null
			? $"First person · {AnimationRoles.Label( _c.Role )} has no weapon clip (bind pose)"
			: $"First person · {clip.Name} · as imported, view only";
		Gizmo.Draw.ScreenText( text, new Vector2( 12, 10 ), "Roboto", 12, TextFlag.LeftTop );
	}
}
