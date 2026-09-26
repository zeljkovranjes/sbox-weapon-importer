using System.Text.Json.Nodes;
using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Setup;
using N = System.Numerics;

namespace WeaponImporter.Tool.UI;

public enum ViewportCamera { Orbit, Gameplay, Front, Side, FirstPerson }

/// <summary>
/// The live preview: the character with its own animgraph holding the weapon through the same
/// <see cref="WeaponHold"/> component the game uses (configured by <see cref="WeaponBaker.ConfigureHold"/>),
/// so what you see here is what the prefab does in game. Draws grips, IK targets, attachments
/// and the picking probe with gizmos.
/// </summary>
public sealed partial class WeaponViewport : SceneRenderingWidget
{
	private readonly ImporterController _c;

	private GameObject _characterObject;
	private GameObject _weaponObject;
	private SkinnedModelRenderer _body;
	private SkinnedModelRenderer _weapon;
	private WeaponHold _hold;
	internal WeaponHold Hold => _hold;
	internal SkinnedModelRenderer Body => _body;

	private string _characterPath;
	private WeaponAnalysis _shownAnalysis;
	private string _shownBaked;
	private bool _holdDirty = true;
	private readonly Dictionary<string, float> _seconds = new( StringComparer.OrdinalIgnoreCase );
	private AnimationRole _playingRole = AnimationRole.Unknown;
	private (int Bone, int Parent)[] _armBones = Array.Empty<(int, int)>();

	public ViewportCamera CameraMode { get; set; } = ViewportCamera.Orbit;

	public WeaponViewport( Widget parent, ImporterController controller ) : base( parent )
	{
		_c = controller;
		MinimumSize = new Vector2( 240, 200 );
		MouseTracking = true;
		FocusMode = FocusMode.Click;
		ToolTip = "Left drag: orbit · right/middle drag: pan · wheel: zoom · F: frame the weapon";

		Scene = Scene.CreateEditorScene();
		using ( Scene.Push() )
		{
			Camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			Camera.BackgroundColor = Theme.ControlBackground;
			Camera.ZNear = 1f;
			Camera.ZFar = 4096f;
			Camera.FieldOfView = 45f;
			Camera.Enabled = true;
		}
		var world = Scene.SceneWorld;
		new ScenePointLight( world, new Vector3( 120, 100, 120 ), 600, Color.White * 3.5f ).ShadowsEnabled = false;
		new ScenePointLight( world, new Vector3( -120, -100, 90 ), 600, Color.White * 2.0f ).ShadowsEnabled = false;
		new ScenePointLight( world, new Vector3( 40, -140, 160 ), 500, Color.White * 1.5f ).ShadowsEnabled = false;

		controller.Changed += MarkHoldDirty;
		controller.GripMoved += MarkHoldDirty;
		controller.SessionReplaced += OnSessionReplaced;
		controller.RoleSeconds = Seconds;
	}

	private void MarkHoldDirty() => _holdDirty = true;

	private void OnSessionReplaced()
	{
		_holdDirty = true;
		_frameIn = 6;
		_playingRole = AnimationRole.Unknown;
	}

	/// <summary>Seconds an action lasts in the preview (from the hold's action table).</summary>
	public float Seconds( AnimationRole role )
	{
		if ( OwnArmsView && WeaponClip( role ) is { Duration: > 0.01f } clip )
			return clip.Duration;
		if ( _seconds.TryGetValue( RoleKey( role ), out var s ) && s > 0.01f )
			return s;
		return role == AnimationRole.Idle ? 2f : 1f;
	}

	public static string RoleKey( AnimationRole role ) => role.ToString().ToLowerInvariant();

	// ------------------------------------------------------------------ scene sync

	private void Sync()
	{
		var session = _c.Session;
		if ( session?.Setup is null || session.Analysis is null )
		{
			if ( _characterObject.IsValid() ) _characterObject.Enabled = false;
			if ( _weaponObject.IsValid() ) _weaponObject.Enabled = false;
			return;
		}

		var characterPath = session.CharacterModel;
		if ( characterPath != _characterPath || !_body.IsValid() )
		{
			_characterPath = characterPath;
			_characterObject?.Destroy();
			var model = Model.Load( characterPath );
			_characterObject = new GameObject( true, "character" );
			_body = _characterObject.AddComponent<SkinnedModelRenderer>();
			CharacterLibrary.Setup( _body, model );
			_armBones = ArmBones( model );
			_holdDirty = true;
			_playingRole = AnimationRole.Unknown;
		}
		_characterObject.Enabled = !OwnArmsView;

		if ( !_weaponObject.IsValid() )
		{
			_weaponObject = new GameObject( true, "weapon" );
			_weapon = _weaponObject.AddComponent<SkinnedModelRenderer>();
			_weapon.UseAnimGraph = false;
			_weapon.PlayAnimationsInEditorScene = true;
			_hold = _weaponObject.AddComponent<WeaponHold>();
			_shownAnalysis = null;
			_holdDirty = true;
		}
		_weaponObject.Enabled = !OwnArmsView;
		// Arms holding nothing (fists): no third-person weapon to show.
		_weapon.Enabled = !session.Analysis.HandsOnly;

		EnsurePreviewMaterials( session.Analysis );
		if ( _shownAnalysis != session.Analysis || _shownBaked != _c.BakedModelPath || _shownMaterials != _materials )
		{
			_shownAnalysis = session.Analysis;
			_shownBaked = _c.BakedModelPath;
			_shownMaterials = _materials;
			Model weaponModel = null;
			if ( _shownBaked is not null )
			{
				weaponModel = Model.Load( _shownBaked );
				if ( weaponModel is null || weaponModel.IsError )
					weaponModel = null;
			}
			weaponModel ??= PreviewModels.BuildWeaponModel( session.Analysis, _materials );
			_weapon.Model = weaponModel;
			_holdDirty = true;
		}

		if ( _holdDirty )
		{
			_holdDirty = false;
			_hold.Body = _body;
			_hold.Weapon = _weapon;
			WeaponBaker.ConfigureHold( _hold, session );
			ReadSeconds( _hold.Actions );
		}
	}

	private void ReadSeconds( string actions )
	{
		_seconds.Clear();
		if ( string.IsNullOrEmpty( actions ) )
			return;
		try
		{
			if ( JsonNode.Parse( actions ) is not JsonObject obj )
				return;
			foreach ( var (role, node) in obj )
				if ( node?["seconds"] is JsonValue v && v.TryGetValue<double>( out var seconds ) )
					_seconds[role] = (float)seconds;
		}
		catch ( Exception )
		{
			// Durations fall back to defaults.
		}
	}

	private static (int, int)[] ArmBones( Model model )
	{
		if ( model is null || model.IsError )
			return Array.Empty<(int, int)>();
		var list = new List<(int, int)>();
		for ( var i = 0; i < model.BoneCount; i++ )
		{
			var name = model.GetBoneName( i ).ToLowerInvariant();
			var parent = model.GetBoneParent( i );
			if ( parent < 0 )
				continue;
			if ( name.Contains( "twist" ) || name.Contains( "helper" ) || name.Contains( "ik" ) || name.Contains( "hold" ) )
				continue;
			if ( name.Contains( "clavicle" ) || name.Contains( "arm" ) || name.Contains( "hand" ) || name.Contains( "finger" ) )
				list.Add( (i, parent) );
		}
		return list.ToArray();
	}

	// ------------------------------------------------------------------ frame

	protected override void PreFrame()
	{
		var dt = Math.Clamp( RealTime.Delta, 0f, 0.1f );
		try
		{
			Sync();
			var session = _c.Session;
			if ( session?.Setup is null || !_body.IsValid() || !_hold.IsValid() )
			{
				TickScene( dt );
				UpdateCamera();
				return;
			}

			if ( OwnArmsView )
			{
				_playingRole = AnimationRole.Unknown;
				TickFirstPerson( dt );
				DrawOverlays();
				return;
			}
			HideFirstPerson();

			var type = session.Setup.EffectiveHoldType;
			_hold.Correction = _c.ShowOriginal ? 0f : 1f;
			CharacterPoser.ApplyParameters( _body, type );
			var role = _c.Role;
			var key = RoleKey( role );
			var seconds = Seconds( role );
			if ( _c.Playing )
			{
				var scaled = dt * _c.Speed;
				var start = _c.RestartRequested || _playingRole != role;
				if ( !start )
				{
					_c.Time += scaled / seconds;
					if ( _c.Time >= 1f )
					{
						if ( _c.Loop || AnimationRoles.Loops( role ) )
						{
							_c.Time %= 1f;
							start = true;
						}
						else
						{
							_c.Time = 1f;
							_c.Playing = false;
						}
					}
				}
				_hold.BeginFrame();
				if ( start )
				{
					var resumeAt = _c.RestartRequested && _c.Time < 0.999f ? _c.Time : 0f;
					_c.RestartRequested = false;
					_playingRole = role;
					// Body and hold start the action together and resume at the same moment.
					RestartGraph( role, type );
					_hold.Play( key );
					_c.Time = resumeAt;
					if ( resumeAt > 0f )
					{
						AdvanceGraph( resumeAt * seconds );
						_hold.Apply( resumeAt * seconds );
					}
				}
				TickScene( scaled );
				_graphTime += scaled;
				if ( _c.Playing )
					_hold.Apply( scaled );
				else
				{
					_hold.Scrub( key, _c.Time );
					_hold.Apply( 0f );
				}
			}
			else
			{
				// Paused / scrubbing: the character shows the action at the scrubbed moment. The
				// animgraph is stepped forward, or replayed from the action start when going back.
				_playingRole = AnimationRole.Unknown;
				_hold.BeginFrame();
				var target = _c.Time * seconds;
				if ( AnimationRoles.GraphTrigger( role ) is not null )
				{
					if ( _graphRole != role || target < _graphTime - 1e-4f )
						RestartGraph( role, type );
					AdvanceGraph( target );
				}
				TickScene( 0f );
				_hold.Scrub( key, _c.Time );
				_hold.Apply( 0f );
			}

			if ( _frameIn > 0 && --_frameIn == 0 )
			{
				FrameCharacter();
			}
			UpdateCamera();
			DrawOverlays();
		}
		catch ( Exception e )
		{
			if ( !_warned )
			{
				_warned = true;
				Log.Warning( $"[weapon importer] preview: {e}" );
				_c.SetStatus( $"Preview error: {e.Message}", Theme.Red );
			}
		}
	}

	private bool _warned;

	// The preview scene's own clock (monotonic, also advanced by graph replays) and the action
	// the character's animgraph is playing with its time since the trigger.
	private double _sceneClock = 1000;
	private AnimationRole _graphRole = AnimationRole.Unknown;
	private float _graphTime;

	/// <summary>Seconds since the action trigger on the character (diagnostics and the gate).</summary>
	internal float GraphTime => _graphTime;
	internal int GraphRestarts, GraphCatchUps;

	/// <summary>Seconds of idle before a replayed action, so any earlier action has finished.</summary>
	private const float ReplaySettle = 2f;
	private const float ReplayStep = 1f / 30f;

	private void TickScene( float dt )
	{
		_sceneClock += dt;
		Scene.EditorTick( (float)_sceneClock, dt );
	}

	/// <summary>Starts <paramref name="role"/> on the character from a settled holdtype idle.</summary>
	private void RestartGraph( AnimationRole role, int type )
	{
		GraphRestarts++;
		_body.SceneModel?.ResetAnimParameters();
		CharacterPoser.ApplyParameters( _body, type );
		for ( var t = 0f; t < ReplaySettle; t += ReplayStep )
			TickScene( ReplayStep );
		if ( AnimationRoles.GraphTrigger( role ) is { } trigger )
			_body.Set( trigger, true );
		_graphRole = role;
		_graphTime = 0f;
	}

	/// <summary>Steps the character's animgraph until the action has run <paramref name="seconds"/>.</summary>
	private void AdvanceGraph( float seconds )
	{
		if ( _graphTime < seconds - 1e-4f )
			GraphCatchUps++;
		while ( _graphTime < seconds - 1e-4f )
		{
			var step = MathF.Min( ReplayStep, seconds - _graphTime );
			TickScene( step );
			_graphTime += step;
		}
	}

	/// <summary>Weapon model world transform (model space == canonical space).</summary>
	// Textured preview materials of the current analysis (null while they load: plain surface).
	private Material[] _materials;
	private Material[] _shownMaterials;
	private Core.Analysis.WeaponAnalysis _materialsFor;

	/// <summary>Starts building the textured materials once per analysis (attached textures included).</summary>
	private void EnsurePreviewMaterials( Core.Analysis.WeaponAnalysis analysis )
	{
		if ( analysis is null || ReferenceEquals( analysis, _materialsFor ) )
			return;
		_materialsFor = analysis;
		_materials = null;
		var materials = analysis.Asset.Mesh.Materials;
		if ( materials.Count > 0 )
			_ = LoadPreviewMaterialsAsync( analysis, materials );
	}

	private async Task LoadPreviewMaterialsAsync( Core.Analysis.WeaponAnalysis analysis, IReadOnlyList<Core.Geometry.MaterialInfo> materials )
	{
		try
		{
			var built = await PreviewMaterials.BuildAsync( materials );
			if ( ReferenceEquals( analysis, _materialsFor ) && this.IsValid() )
				_materials = built;
		}
		catch ( Exception e ) when ( e is not OperationCanceledException )
		{
			Log.Warning( $"[weapon importer] preview textures could not be built: {e.Message}" );
		}
	}

	/// <summary>Textured materials are shown (tests).</summary>
	public bool PreviewTextured => _materials is not null && ReferenceEquals( _shownMaterials, _materials );

	public Transform WeaponWorld => OwnArmsView ? FirstPersonWeaponWorld : _weaponObject.IsValid() ? _weaponObject.WorldTransform : Transform.Zero;

	/// <summary>Renders the viewport (with overlays) to a PNG, for the gate and for thumbnails.</summary>
	public byte[] RenderPng( int width, int height )
	{
		if ( !Camera.IsValid() )
			return null;
		var bitmap = new Bitmap( width, height );
		using ( Scene.Push() )
		using ( GizmoInstance.Push() )
		{
			DrawOverlays();
			Camera.RenderToBitmap( bitmap );
		}
		return bitmap.ToPng();
	}

	public override void OnDestroyed()
	{
		_c.Changed -= MarkHoldDirty;
		_c.GripMoved -= MarkHoldDirty;
		_c.SessionReplaced -= OnSessionReplaced;
		base.OnDestroyed();
		Scene?.Destroy();
		Scene = null;
	}
}
