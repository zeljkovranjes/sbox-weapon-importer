namespace WeaponImporter;

/// <summary>
/// Holds a weapon in a character's third-person hands using baked data. The weapon follows the
/// character's animated hold bone, both arms reach weapon-space hand targets with a two-bone IK
/// that keeps the animation's own elbow bend, fingers take baked local rotations and the support hand
/// follows a per-action contact track. Actions start on rising edges of character animgraph bools
/// and play the matching sequence on the weapon renderer.
/// </summary>
[Title( "Weapon Hold" )]
[Category( "Weapons" )]
[Icon( "pan_tool" )]
public sealed class WeaponHold : Component
{
    private const string IdleRole = "idle";
    private const string FireRole = "fire";
    private const string AimedFireRole = "adsfire";
    private const string ReloadStartRole = "reloadstart";
    private const string InsertRole = "reloadinsert";
    private const string ReloadEndRole = "reloadend";
    private const float DefaultActionSeconds = 1f;
    private const float ReachGateSeconds = 0.35f;
    private const float ReachFadeDistance = 6f;
    private const float ActionBlendSeconds = 0.3f;

    private static readonly (string Role, string Trigger)[] DefaultTriggers =
    {
        ("fire", "b_attack"),
        ("reload", "b_reload"),
        ("draw", "b_deploy"),
    };

    /// <summary>Character renderer. When empty the first ancestor renderer whose model has <see cref="HoldBone"/> is used.</summary>
    [Property] public SkinnedModelRenderer Body { get; set; }

    /// <summary>Weapon renderer. When empty the renderer on this GameObject is used.</summary>
    [Property] public SkinnedModelRenderer Weapon { get; set; }

    /// <summary>
    /// Citizen holdtype the character uses with this weapon (1 pistol, 2 rifle, 3 shotgun, 4 item,
    /// 6 melee, 7 rpg): the hold the importer found the weapon fits best. -1 leaves it to the game.
    /// </summary>
    [Property] public int HoldType { get; set; } = -1;

    /// <summary>
    /// A grip profile made by the importer. When set, its values replace the grip properties
    /// below (hands, fingers, weapon placement, contacts, actions, hold type).
    /// </summary>
    [Property] public WeaponGripProfile Profile { get; set; }

    /// <summary>Where the right elbow points, weapon space (zero = follow the animation only).</summary>
    [Property] public Vector3 RightElbowHint { get; set; }

    /// <summary>Where the left elbow points, weapon space (zero = follow the animation only).</summary>
    [Property] public Vector3 LeftElbowHint { get; set; }

    /// <summary>How much the elbows lean toward their hints (0 = the animation decides alone).</summary>
    [Property, Range( 0, 1 )] public float ElbowHintWeight { get; set; } = 0.35f;

    /// <summary>Character bone the weapon is held by (animation pose).</summary>
    [Property] public string HoldBone { get; set; } = "hold_R";

    /// <summary>Weapon model transform relative to the hold bone, "px,py,pz,qx,qy,qz,qw".</summary>
    [Property] public string WeaponInHold { get; set; }

    /// <summary>hand_R bone transform in weapon-model space (7 floats).</summary>
    [Property] public string RightHand { get; set; }

    /// <summary>hand_L bone transform in weapon-model space (7 floats, "" = one-handed).</summary>
    [Property] public string LeftHand { get; set; }

    /// <summary>Right finger local rotations, "bone=qx,qy,qz,qw;bone=...".</summary>
    [Property] public string RightFingers { get; set; }

    /// <summary>Left finger local rotations, "bone=qx,qy,qz,qw;bone=...".</summary>
    [Property] public string LeftFingers { get; set; }

    /// <summary>Per-action hand contact tracks (JSON, role keys lowercase).</summary>
    [Property] public string Contacts { get; set; }

    /// <summary>Per-action duration, weapon sequence and trigger parameter (JSON, role keys lowercase).</summary>
    [Property] public string Actions { get; set; }

    /// <summary>
    /// Replacement character animations per action (JSON, role keys lowercase):
    /// <c>{"reload":{"model":"","sequence":"reload_rifle","blend":0.2}}</c>. The sequence plays over
    /// the upper body while the animgraph keeps the legs; "" model = the character's own model.
    /// </summary>
    [Property] public string CharacterActions { get; set; }

    /// <summary>Temporal smoothing of IK corrections (0 = none, 1 = 0.1 s half-life).</summary>
    [Property, Range( 0, 1 )] public float Smoothing { get; set; } = 0.35f;

    /// <summary>Inches past full reach before the support hand starts letting go (it fades out over the next 6 in).</summary>
    [Property] public float ReachSlack { get; set; } = 1.5f;

    /// <summary>Whether the left hand supports the weapon at all.</summary>
    [Property] public bool SupportHand { get; set; } = true;

    /// <summary>
    /// Seconds a changed grip (hand targets, weapon placement, finger pose) takes to ease in,
    /// so editing a grip while an animation plays never makes the hands or the weapon jump.
    /// </summary>
    [Property, Range( 0, 1 )] public float TransitionSeconds { get; set; } = 0.15f;

    /// <summary>
    /// How much of the contact correction is applied (1 = hands on the weapon, 0 = the original
    /// animation untouched; the weapon still follows the hold bone). The editor's Original /
    /// Corrected comparison drives this.
    /// </summary>
    [Property, Range( 0, 1 )] public float Correction { get; set; } = 1f;

    /// <summary>Current firing-hand IK weight (0 = its own animation, 1 = on the weapon).</summary>
    public float RightWeight { get; private set; }

    /// <summary>Current support-hand IK weight (0 = animated, 1 = on the weapon).</summary>
    public float LeftWeight { get; private set; }

    /// <summary>
    /// The player is aiming down the sights. Set it from your input; the first-person viewmodel
    /// raises the sights, holds them and lowers them again through its animgraph.
    /// </summary>
    [Property] public bool Aiming { get; set; }

    /// <summary>
    /// Shells a shell-by-shell reload loads (weapons with reload start / insert / end clips). Set it
    /// from your ammo before the reload starts; 0 skips the reload.
    /// </summary>
    [Property] public int ShellsToLoad { get; set; } = 4;

    /// <summary>
    /// The weapon is empty (set it from your ammo): reloads play the empty reload and the last
    /// shot plays the last-round fire, when the weapon has those animations (the animgraph's b_empty).
    /// </summary>
    [Property] public bool Empty { get; set; }

    /// <summary>
    /// The player holds the guard up (melee weapons, fists). Set it from your input; the
    /// first-person viewmodel raises, holds and lowers the guard through its animgraph (b_block).
    /// </summary>
    [Property] public bool Blocking { get; set; }

    /// <summary>
    /// The player keeps using the item (held use: drinking, healing). Set it from your input; the
    /// viewmodel plays the start, loops while it stays on and plays the end (b_use). For a one-off
    /// use call <c>Play( "use" )</c> instead.
    /// </summary>
    [Property] public bool Using { get; set; }

    /// <summary>
    /// Which clip the current attack plays when the weapon has several (1 = the first; left and
    /// right punches, a combo of slashes). Advances with every attack.
    /// </summary>
    public int AttackVariant { get; private set; } = 1;

    private readonly Dictionary<string, int> _nextVariant = new( StringComparer.OrdinalIgnoreCase );

    /// <summary>The item has a held use (start / loop / end clips), driven by <see cref="Using"/>.</summary>
    public bool HasHeldUse => HasClip( "useloop" ) || HasClip( "usestart" ) && HasClip( "useend" );

    /// <summary>A shell-by-shell reload is running.</summary>
    public bool ShellReloading => _shellReload;

    /// <summary>Shells still to insert in the running shell reload (the current one included).</summary>
    public int ShellsRemaining => _stopShells ? (_role == InsertRole ? Math.Min( 1, _shellsLeft ) : 0) : _shellsLeft;

    /// <summary>One shell went in (add it to your ammo).</summary>
    public event Action ShellInserted;

    /// <summary>Ends a shell reload after the shell being inserted (firing does this too).</summary>
    public void StopReload()
    {
        if ( _shellReload )
            _stopShells = true;
    }

    private bool _shellReload;
    private bool _stopShells;
    private int _shellsLeft;

    /// <summary>An action started (fire, reload, draw...), from the character's animgraph or <see cref="Play"/>.</summary>
    public event Action<string> ActionStarted;

    /// <summary>Current action role ("idle" when no action runs).</summary>
    public string CurrentRole => _role;

    /// <summary>Normalized time (0..1) of the current action.</summary>
    public float CurrentTime => _duration > 0f ? Math.Clamp( _elapsed / _duration, 0f, 1f ) : 0f;

    /// <summary>The weapon renderer the hold moves (<see cref="Weapon"/>, else the one on this object).</summary>
    public SkinnedModelRenderer WeaponRenderer => Weapon.IsValid() ? Weapon : Components.Get<SkinnedModelRenderer>( FindMode.EnabledInSelf );

    /// <summary>Weapon sequence of an action role ("" when it has none).</summary>
    public string SequenceFor( string role ) => role != null && _actions.TryGetValue( role, out var info ) ? info.Sequence : "";

    /// <summary>Inches the right wrist stayed short of its target on the last update.</summary>
    public float RightShortfall { get; private set; }

    /// <summary>Inches the left wrist stayed short of its target on the last update.</summary>
    public float LeftShortfall { get; private set; }

    // Action state.
    private string _role = IdleRole;
    private float _elapsed;

    // Contact weights cross-fade from what was applied when an action starts.
    private float _sinceAction = float.MaxValue;
    private float _leftApplied = 1f, _rightApplied = 1f;
    private float _leftFrom = 1f, _rightFrom = 1f;
    private float _duration = DefaultActionSeconds;
    private bool _paused;
    private bool _started;
    private bool _snap = true;
    private bool _cleanPose;
    private float _smoothTime;
    private float _reachGate = 1f;
    private double _lastTick = double.NaN;

    // Parsed data (re-parsed when the source strings change).
    private string _srcWeaponInHold, _srcRightHand, _srcLeftHand, _srcRightFingers, _srcLeftFingers, _srcContacts, _srcActions, _srcCharacterActions;
    private readonly Dictionary<string, OverlayInfo> _overlays = new( StringComparer.OrdinalIgnoreCase );
    private readonly CharacterOverlay _overlay = new();
    private OverlayInfo _activeOverlay;
    private readonly List<BoneCollection.Bone> _upperBones = new();

    // Pelvis-to-head chain (and the spine under the clavicles): pinned to their animation pose
    // whenever arm overrides force the skeleton to be evaluated again, which otherwise leans the
    // citizen/human spine back and tips the head up.
    private readonly List<BoneCollection.Bone> _postureBones = new();
    private bool _parsedOnce;
    private Transform _weaponInHold = global::Transform.Zero;
    private Transform _rightHand = global::Transform.Zero;
    private Transform _leftHand = global::Transform.Zero;
    private bool _hasRightHand, _hasLeftHand;

    // Grip transitions: what was shown when a baked transform changed, eased to the new value.
    private TransformBlend _weaponInHoldBlend, _rightHandBlend, _leftHandBlend;
    private Transform _leftHandNow = global::Transform.Zero;
    private readonly List<FingerRotation> _rightFingers = new();
    private readonly List<FingerRotation> _leftFingers = new();
    private readonly Dictionary<string, HoldContact[]> _contacts = new( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, ActionInfo> _actions = new( StringComparer.OrdinalIgnoreCase );
    private readonly List<TriggerBinding> _triggers = new();
    private readonly HashSet<string> _warned = new();

    // Bone caches.
    private SkinnedModelRenderer _autoBody;
    private SkinnedModelRenderer _cachedBody;
    private Model _cachedModel;
    private string _cachedHoldBone;
    private BoneCollection.Bone _holdBone;
    private ArmChain _rightChain, _leftChain;
    private bool _fingersDirty = true;

    private SkinnedModelRenderer _overrideOwner;
    private SkinnedModelRenderer _cachedWeapon;
    private Model _cachedWeaponModel;
    private readonly Dictionary<string, BoneCollection.Bone> _weaponBones = new( StringComparer.Ordinal );

    // Per-update scratch.
    private Transform _weaponWorldAtRead;
    private Func<HoldContact, Transform> _resolveContact;

    /// <summary>Starts an action now: plays its weapon sequence and runs its contact track.</summary>
    public void Play( string role )
    {
        RefreshData();
        _paused = false;
        _started = true;
        role = NormalizeRole( role );
        if ( role == "reload" && Empty && HasClip( "emptyreload" ) )
            role = "emptyreload";
        // A reload on a weapon with a shell-by-shell reload runs it (start, shells, end).
        if ( role is "reload" or "tacticalreload" or "emptyreload" && HasClip( InsertRole ) && !HasClip( role ) )
        {
            if ( ShellsToLoad <= 0 )
                return;
            role = HasClip( ReloadStartRole ) ? ReloadStartRole : InsertRole;
        }
        StartAction( role, ResolveWeapon( ResolveBody() ) );
    }

    /// <summary>
    /// Editor helper: pauses on an action at a normalized time (null/"" = idle). Poses shown after
    /// a scrub are exact (smoothing is bypassed) until <see cref="Play"/> is called.
    /// </summary>
    public void Scrub( string role, float normalizedTime )
    {
        RefreshData();
        var weapon = ResolveWeapon( ResolveBody() );
        role = NormalizeRole( role );
        float t = float.IsFinite( normalizedTime ) ? Math.Clamp( normalizedTime, 0f, 1f ) : 0f;

        _paused = true;
        _started = true;
        _snap = true;
        _role = role;
        _actions.TryGetValue( role, out var info );
        _duration = PlayWeaponSequence( weapon, info, role == IdleRole, t, paused: true );
        if ( _activeOverlay == null || !string.Equals( _activeOverlay.Role, role, StringComparison.OrdinalIgnoreCase ) )
            BeginOverlay( role, info );
        _elapsed = t * _duration;
    }

    /// <summary>
    /// Runs one full update: advances the action by <paramref name="deltaTime"/>, places the
    /// weapon and writes the arm bone overrides. Works in editor scenes when called manually.
    /// </summary>
    public void Apply( float deltaTime )
    {
        Run( float.IsFinite( deltaTime ) ? MathF.Max( 0f, deltaTime ) : 0f, advance: true, write: true );
    }

    /// <summary>
    /// Clears this frame's arm overrides so the next animation update evaluates the character's
    /// procedural helper bones (twist, elbow) cleanly; the arms keep those corrections when they
    /// are written. The component does this itself; call it before ticking the scene when driving
    /// the hold manually with <see cref="Apply"/>.
    /// </summary>
    public void BeginFrame()
    {
        if ( _overrideOwner.IsValid() && _overrideOwner.SceneModel != null )
            _overrideOwner.SceneModel.ClearBoneOverrides();
        _cleanPose = true;
    }

    /// <summary>
    /// Reads the action trigger parameters now. <see cref="WeaponHoldSystem"/> calls this every
    /// frame before the animgraph runs, because graphs such as the citizen's reset triggers
    /// (b_attack, b_reload) as soon as they consume them. Call it yourself after setting a
    /// parameter when driving the hold manually with <see cref="Apply"/>.
    /// </summary>
    public void ReadTriggers()
    {
        if ( _paused || !_started )
            return;
        var body = ResolveBody();
        if ( !body.IsValid() || body.Model == null )
            return;
        RefreshData();
        PollTriggers( body, ResolveWeapon( body ) );
    }

    /// <summary>Parses "px,py,pz,qx,qy,qz,qw" (invariant culture, quaternion normalized).</summary>
    public static bool TryParse( string s, out Transform t ) => HoldData.TryParseTransform( s, out t );

    /// <summary>Formats a transform as "px,py,pz,qx,qy,qz,qw" with 6 decimals (invariant culture).</summary>
    public static string Format( Transform t ) => HoldData.FormatTransform( t );

    /// <inheritdoc />
    protected override void OnEnabled()
    {
        _snap = true;
        _lastTick = double.NaN;
    }

    /// <inheritdoc />
    protected override void OnUpdate() => Tick();

    /// <inheritdoc />
    protected override void OnPreRender() => Tick();

    /// <inheritdoc />
    protected override void OnDisabled() => ClearOverrides();

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        ClearOverrides();
        _overlay.Dispose();
    }

    private void Tick()
    {
        double now = Time.Now;
        if ( _lastTick == now )
        {
            // Second call this frame (OnPreRender): solve on the fresh animation pose and write.
            Run( 0f, advance: false, write: true );
            return;
        }

        // First call (OnUpdate): advance and place the weapon, but leave the arms to the
        // animation update so its procedural helpers are evaluated cleanly.
        _lastTick = now;
        Run( float.IsFinite( Time.Delta ) ? MathF.Max( 0f, Time.Delta ) : 0f, advance: true, write: false );
    }

    private void Run( float dt, bool advance, bool write )
    {
        var body = ResolveBody();
        if ( body != _overrideOwner )
            ClearOverrides();

        if ( !body.IsValid() || body.Model == null || body.SceneModel == null )
            return;

        var weapon = ResolveWeapon( body );
        RefreshData();

        if ( !EnsureBones( body ) )
        {
            ClearOverrides();
            return;
        }

        if ( !_started )
        {
            _started = true;
            StartAction( IdleRole, weapon );
        }

        if ( HoldType >= 0 && advance && body.GetInt( "holdtype" ) != HoldType )
            body.Set( "holdtype", HoldType );

        if ( !_paused )
        {
            PollTriggers( body, weapon );
            if ( advance )
                AdvanceTime( dt, weapon );
        }

        var bodyTx = body.WorldTransform;
        UpdateOverlay( body );
        if ( !TryGetAnim( body, _holdBone, out var holdWorld ) )
            return;

        double now = RealTime.Now;
        var weaponInHold = _weaponInHoldBlend.Evaluate( _weaponInHold, now );
        var rightHand = _rightHandBlend.Evaluate( _rightHand, now );
        _leftHandNow = _leftHandBlend.Evaluate( _leftHand, now );
        float correction = Math.Clamp( Correction, 0f, 1f );

        var holdModel = bodyTx.ToLocal( holdWorld ).WithScale( 1f );
        var weaponModel = holdModel.ToWorld( weaponInHold ).WithScale( 1f );

        if ( weapon.IsValid() )
        {
            _weaponWorldAtRead = weapon.WorldTransform;
            weapon.WorldTransform = bodyTx.ToWorld( weaponModel );
        }

        var sceneModel = body.SceneModel;
        sceneModel.ClearBoneOverrides();
        _overrideOwner = body;
        _smoothTime += dt;
        if ( !write )
        {
            _cleanPose = true;
            return;
        }

        bool clean = _cleanPose;
        _cleanPose = false;
        bool snap = _snap || _paused || Smoothing <= 0f;
        float stepTime = _smoothTime;
        float alpha = snap ? 1f : ArmSolver.SmoothingFactor( Smoothing, stepTime );
        _smoothTime = 0f;

        // Replacement animation over the upper body (spine, neck, head, clavicles); the arms are
        // written below from the same blended pose.
        if ( _overlay.Active && _overlay.Weight > 1e-4f )
        {
            foreach ( var bone in _upperBones )
                if ( TryGetAnim( body, bone, out var w ) )
                    sceneModel.SetBoneOverride( bone.Index, bodyTx.ToLocal( w ) );
        }

        // Right hand: on the weapon unless the action's contact track releases it (the arm then
        // plays its own animation; the weapon still follows the animated hold bone).
        RightShortfall = 0f;
        RightWeight = 0f;
        if ( _rightChain != null && _hasRightHand )
        {
            _rightChain.ReadAnimation( this, body, bodyTx, clean );
            _rightChain.Now = now;
            _contacts.TryGetValue( _role, out var rightKeys );
            float rightContact = HoldData.EvaluateContacts( rightKeys, false, _elapsed, _duration, rightHand, null, out _ );
            rightContact = _rightApplied = CrossFade( _rightFrom, rightContact, snap );
            rightContact *= correction;
            RightWeight = rightContact;
            var target = weaponModel.ToWorld( rightHand );
            RightShortfall = _rightChain.Solve( target, alpha, snap, Hint( weaponModel, RightElbowHint ), ElbowHintWeight );
            if ( rightContact > 1e-4f )
                _rightChain.Write( sceneModel, rightContact );
        }

        // Left hand: contact track, reach release, smoothing.
        LeftShortfall = 0f;
        LeftWeight = 0f;
        if ( _leftChain != null && _hasLeftHand )
        {
            _leftChain.ReadAnimation( this, body, bodyTx, clean );
            _leftChain.Now = now;

            _contacts.TryGetValue( _role, out var keys );
            _resolveContact ??= ResolveContactTarget;
            float contactWeight = HoldData.EvaluateContacts( keys, true, _elapsed, _duration, _leftHandNow,
                keys != null ? _resolveContact : null, out var targetInWeapon );
            contactWeight = _leftApplied = CrossFade( _leftFrom, contactWeight, snap );

            var target = weaponModel.ToWorld( targetInWeapon );
            // Out of reach (a character gesture pulled the weapon away): let go gradually the
            // further the grip is, instead of snapping the hand back to its animation.
            float over = _leftChain.TargetDistance( target.Position ) - _leftChain.Reach - MathF.Max( 0f, ReachSlack );
            float fade = Math.Clamp( over / ReachFadeDistance, 0f, 1f );
            float gateTarget = SupportHand ? 1f - fade * fade * (3f - 2f * fade) : 0f;
            _reachGate = snap
                ? gateTarget
                : MoveTowards( _reachGate, gateTarget, stepTime / ReachGateSeconds );

            float weight = Math.Clamp( contactWeight * _reachGate * correction, 0f, 1f );
            LeftWeight = weight;

            LeftShortfall = _leftChain.Solve( target, alpha, snap, Hint( weaponModel, LeftElbowHint ), ElbowHintWeight );
            // Also when released: the character's animgraph attaches its left hand to the right
            // hand with its own IK rule, so a corrected right arm would drag the free left arm
            // along and it would snap back when the rule ends. Writing its animation pose keeps
            // the released arm exactly on its own animation.
            if ( weight > 1e-4f || RightWeight > 1e-4f )
                _leftChain.Write( sceneModel, weight );
        }

        // Overrides only take effect when the skeleton is evaluated; the animation update has
        // already run this frame, so evaluate again (no time advance) to show them now. That
        // evaluation bends the spine differently, so the posture chain keeps this frame's pose.
        if ( sceneModel.HasBoneOverrides() )
        {
            if ( !(_overlay.Active && _overlay.Weight > 1e-4f) )
                foreach ( var bone in _postureBones )
                    if ( TryGetAnim( body, bone, out var w ) )
                        sceneModel.SetBoneOverride( bone.Index, bodyTx.ToLocal( w ) );
            sceneModel.Update( 0f );
        }

        _snap = false;
    }

    /// <summary>
    /// Animation pose of a character bone in world space: the animgraph result, blended toward the
    /// action's replacement sequence on the upper body while one plays.
    /// </summary>
    private bool TryGetAnim( SkinnedModelRenderer body, BoneCollection.Bone bone, out Transform world )
    {
        if ( !body.TryGetBoneTransformAnimation( bone, out world ) )
            return false;
        if ( _overlay.Active && _overlay.Weight > 1e-4f && _upperSet.Contains( bone.Index ) && _overlay.TryGetWorld( bone.Name, out var overlay ) )
        {
            var w = _overlay.Weight;
            world = new Transform( Vector3.Lerp( world.Position, overlay.Position, w ), ArmSolver.Slerp( world.Rotation, overlay.Rotation, w ), world.Scale );
        }
        return true;
    }

    private readonly HashSet<int> _upperSet = new();

    private void UpdateOverlay( SkinnedModelRenderer body )
    {
        if ( !_overlay.Active || _activeOverlay == null )
            return;
        float blend = MathF.Max( 1e-3f, _activeOverlay.Blend );
        float fadeIn = _elapsed / blend;
        float fadeOut = (_duration - _elapsed) / blend;
        float w = Math.Clamp( MathF.Min( fadeIn, fadeOut ), 0f, 1f );
        w = w * w * (3f - 2f * w);
        if ( _paused )
            w = _elapsed <= 0f || _elapsed >= _duration ? w : 1f;
        float seconds = _overlay.Duration > 0f && _duration > 0f ? _elapsed / _duration * _overlay.Duration : _elapsed;
        _overlay.Update( body, seconds, w );
    }

    private void AdvanceTime( float dt, SkinnedModelRenderer weapon )
    {
        _elapsed += dt;
        if ( _sinceAction < float.MaxValue )
            _sinceAction += dt;
        if ( _role == IdleRole )
        {
            if ( _duration > 0f && _elapsed >= _duration )
                _elapsed %= _duration;
            return;
        }

        if ( _elapsed < _duration )
            return;
        // Shell-by-shell reload: start -> one insert per shell -> end.
        if ( _role == ReloadStartRole )
        {
            if ( _shellsLeft > 0 && !_stopShells )
                StartAction( InsertRole, weapon );
            else
                FinishShells( weapon );
            return;
        }
        if ( _role == InsertRole )
        {
            _shellsLeft = Math.Max( 0, _shellsLeft - 1 );
            ShellInserted?.Invoke();
            if ( _shellsLeft > 0 && !_stopShells )
                StartAction( InsertRole, weapon );
            else
                FinishShells( weapon );
            return;
        }
        StartAction( IdleRole, weapon );
    }

    private void FinishShells( SkinnedModelRenderer weapon )
    {
        _shellsLeft = 0;
        StartAction( HasClip( ReloadEndRole ) ? ReloadEndRole : IdleRole, weapon );
    }

    private bool HasClip( string role ) => _actions.TryGetValue( role, out var a ) && !string.IsNullOrEmpty( a.Sequence );

    /// <summary>The action with this turn's clip: an action with variants plays them one after another.</summary>
    private ActionInfo NextVariant( string role, ActionInfo info )
    {
        if ( info == null || info.Variants.Count == 0 )
        {
            AttackVariant = 1;
            return info;
        }
        _nextVariant.TryGetValue( role, out var index );
        var count = info.Variants.Count + 1;
        index %= count;
        _nextVariant[role] = index + 1;
        AttackVariant = index + 1;
        if ( index == 0 )
            return info;
        return new ActionInfo { Role = info.Role, Seconds = 0f, Sequence = info.Variants[index - 1], Trigger = info.Trigger };
    }

    private void StartAction( string role, SkinnedModelRenderer weapon )
    {
        ShellReloadState( role );
        _role = role;
        _elapsed = 0f;
        _sinceAction = 0f;
        _leftFrom = _leftApplied;
        _rightFrom = _rightApplied;
        _actions.TryGetValue( role, out var info );
        info = NextVariant( role, info );
        _duration = PlayWeaponSequence( weapon, info, role == IdleRole, 0f, paused: false );
        BeginOverlay( role, info );
        if ( role != IdleRole )
            ActionStarted?.Invoke( role );
    }

    /// <summary>
    /// Shell reload bookkeeping as actions change, and the character's own shell reload: the
    /// citizen/human graph plays its loading loop while b_reloading is on, one shell per
    /// b_reloading_insert.
    /// </summary>
    private void ShellReloadState( string role )
    {
        var body = ResolveBody();
        bool shellPart = role is ReloadStartRole or InsertRole;
        if ( shellPart && !_shellReload )
        {
            _shellReload = true;
            _stopShells = false;
            _shellsLeft = Math.Max( 0, ShellsToLoad );
            if ( body.IsValid() )
                body.Set( "b_reloading", true );
        }
        if ( role == InsertRole && body.IsValid() )
            body.Set( "b_reloading_insert", true );
        if ( !shellPart && _shellReload )
        {
            _shellReload = false;
            if ( body.IsValid() )
                body.Set( "b_reloading", false );
        }
    }

    private void BeginOverlay( string role, ActionInfo info )
    {
        _activeOverlay = null;
        _overlay.End();
        if ( role == IdleRole || !_overlays.TryGetValue( role, out var overlay ) )
            return;
        var body = ResolveBody();
        if ( !body.IsValid() )
            return;
        if ( !_overlay.Begin( body, overlay, out var error ) )
        {
            if ( error != null )
                WarnOnce( $"overlay:{role}:{error}", $"Weapon Hold: character animation for '{role}' ignored ({error})." );
            return;
        }
        _activeOverlay = overlay;
        // Without an explicit duration the replacement animation decides how long the action lasts.
        if ( (info == null || info.Seconds <= 0f) && _overlay.Duration > 0f )
            _duration = _overlay.Duration;
    }

    /// <summary>Plays the action's weapon sequence and returns the action duration in seconds.</summary>
    private float PlayWeaponSequence( SkinnedModelRenderer weapon, ActionInfo info, bool loop, float normalized, bool paused )
    {
        float seconds = info != null && info.Seconds > 0f ? info.Seconds : 0f;
        float sequenceSeconds = 0f;

        if ( weapon.IsValid() && info != null && !string.IsNullOrEmpty( info.Sequence ) )
        {
            weapon.UseAnimGraph = false;
            var sequence = weapon.Sequence;
            // The model is the authority: the renderer's own sequence list lags a frame behind a
            // model change. A model without any animations (a static prop) has nothing to play.
            var model = weapon.Model;
            var animated = model != null && !model.IsError && model.AnimationCount > 0;
            if ( animated && !model.AnimationNames.Contains( info.Sequence ) )
            {
                WarnOnce( $"seq:{info.Sequence}", $"Weapon Hold: weapon model has no sequence '{info.Sequence}' (action '{info.Role}')." );
            }
            else if ( animated )
            {
                if ( sequence.Name != info.Sequence )
                    sequence.Name = info.Sequence;
                sequence.Looping = loop;
                sequenceSeconds = sequence.Duration;
                sequence.Time = normalized * sequenceSeconds;
                sequence.PlaybackRate = paused
                    ? 0f
                    : (!loop && seconds > 0f && sequenceSeconds > 0f ? sequenceSeconds / seconds : 1f);
            }
        }

        if ( seconds > 0f )
            return seconds;
        return sequenceSeconds > 0f ? sequenceSeconds : DefaultActionSeconds;
    }

    private void PollTriggers( SkinnedModelRenderer body, SkinnedModelRenderer weapon )
    {
        TriggerBinding best = null;
        for ( int i = 0; i < _triggers.Count; i++ )
        {
            var binding = _triggers[i];
            bool value = body.GetBool( binding.Parameter );
            if ( value && !binding.Previous )
            {
                binding.Role = ChooseRole( binding );
                binding.Seconds = _actions.TryGetValue( binding.Role, out var chosen ) && chosen.Seconds > 0f ? chosen.Seconds : DefaultActionSeconds;
                if ( best == null || binding.Seconds > best.Seconds )
                    best = binding;
            }
            binding.Previous = value;
        }

        if ( best == null )
            return;

        // During a shell reload: another reload is ignored; firing ends it after this shell.
        if ( _shellReload )
        {
            if ( best.Role is FireRole or AimedFireRole )
                StopReload();
            return;
        }
        if ( string.IsNullOrEmpty( best.Role ) )
            return;

        // A shorter action does not cut a longer one that is still running (fire during reload).
        if ( _role != IdleRole && _role != best.Role )
        {
            float remaining = _duration - _elapsed;
            if ( remaining > 0f && best.Seconds < remaining )
                return;
        }

        StartAction( best.Role, weapon );
    }

    private Transform ResolveContactTarget( HoldContact key )
    {
        var target = _leftHandNow;
        if ( !string.IsNullOrEmpty( key.Follow ) && TryGetWeaponBone( key.Follow, out var boneInModel, out var restInModel ) )
            target = boneInModel.ToWorld( restInModel.ToLocal( _leftHandNow ) );
        return HoldData.ApplyOffset( key, target );
    }

    private bool TryGetWeaponBone( string name, out Transform current, out Transform rest )
    {
        current = global::Transform.Zero;
        rest = global::Transform.Zero;

        var weapon = _cachedWeapon;
        if ( !weapon.IsValid() || weapon.Model == null )
            return false;

        if ( weapon.Model != _cachedWeaponModel )
        {
            _weaponBones.Clear();
            _cachedWeaponModel = weapon.Model;
        }

        if ( !_weaponBones.TryGetValue( name, out var bone ) )
        {
            bone = weapon.Model.Bones.GetBone( name );
            _weaponBones[name] = bone;
            if ( bone == null )
                WarnOnce( $"wbone:{name}", $"Weapon Hold: weapon model has no bone '{name}' to follow." );
        }

        if ( bone == null || !weapon.TryGetBoneTransformAnimation( bone, out var world ) )
            return false;

        current = _weaponWorldAtRead.ToLocal( world ).WithScale( 1f );
        rest = weapon.Model.GetBoneTransform( bone.Index ).WithScale( 1f );
        return true;
    }

    private SkinnedModelRenderer ResolveBody()
    {
        if ( Body.IsValid() )
            return Body;

        var hold = HoldBone ?? "";
        if ( _autoBody.IsValid() && _autoBody.Model != null && _autoBody.Model.Bones.HasBone( hold ) )
            return _autoBody;

        _autoBody = null;
        for ( var go = GameObject; go.IsValid(); go = go.Parent )
        {
            foreach ( var renderer in go.Components.GetAll<SkinnedModelRenderer>( FindMode.EnabledInSelf ) )
            {
                if ( renderer == Weapon || renderer.Model == null )
                    continue;
                if ( renderer.Model.Bones.HasBone( hold ) )
                {
                    _autoBody = renderer;
                    return renderer;
                }
            }
        }

        return null;
    }

    private SkinnedModelRenderer ResolveWeapon( SkinnedModelRenderer body )
    {
        var weapon = Weapon.IsValid() ? Weapon : Components.Get<SkinnedModelRenderer>( FindMode.EnabledInSelf );
        if ( weapon == body )
            weapon = null;
        _cachedWeapon = weapon;
        return weapon;
    }

    private bool EnsureBones( SkinnedModelRenderer body )
    {
        var model = body.Model;
        if ( body == _cachedBody && model == _cachedModel && HoldBone == _cachedHoldBone )
        {
            if ( _fingersDirty )
                MapFingers( _fingersBlend );
            return _holdBone != null;
        }

        _cachedBody = body;
        _cachedModel = model;
        _cachedHoldBone = HoldBone;
        _snap = true;

        var bones = model.Bones;
        _holdBone = string.IsNullOrEmpty( HoldBone ) ? null : bones.GetBone( HoldBone );
        if ( _holdBone == null )
            WarnOnce( $"hold:{model.Name}:{HoldBone}", $"Weapon Hold: character model '{model.Name}' has no hold bone '{HoldBone}'." );

        _rightChain = ArmChain.Build( bones, "R" );
        _leftChain = ArmChain.Build( bones, "L" );
        if ( _rightChain == null )
            WarnOnce( $"armR:{model.Name}", $"Weapon Hold: character model '{model.Name}' lacks arm_upper_R/arm_lower_R/hand_R." );
        if ( _leftChain == null )
            WarnOnce( $"armL:{model.Name}", $"Weapon Hold: character model '{model.Name}' lacks arm_upper_L/arm_lower_L/hand_L." );

        // Upper body for replacement animations: spine_0 and below, minus the arm subtrees.
        _upperBones.Clear();
        _upperSet.Clear();
        var spine = bones.GetBone( "spine_0" ) ?? bones.GetBone( "spine_1" );
        if ( spine != null )
        {
            var arms = new HashSet<int>();
            _rightChain?.Collect( arms );
            _leftChain?.Collect( arms );
            var stack = new Stack<BoneCollection.Bone>();
            stack.Push( spine );
            while ( stack.Count > 0 )
            {
                var bone = stack.Pop();
                _upperSet.Add( bone.Index );
                if ( !arms.Contains( bone.Index ) )
                    _upperBones.Add( bone );
                if ( bone.Children != null )
                    foreach ( var child in bone.Children )
                        stack.Push( child );
            }
        }

        // Posture chain: every ancestor of the head and of the arms (not the root, not the arms).
        _postureBones.Clear();
        var posture = new HashSet<int>();
        var armBones = new HashSet<int>();
        _rightChain?.Collect( armBones );
        _leftChain?.Collect( armBones );
        void Climb( BoneCollection.Bone bone )
        {
            for ( var b = bone; b != null && b.Parent != null; b = b.Parent )
                if ( !armBones.Contains( b.Index ) && posture.Add( b.Index ) )
                    _postureBones.Add( b );
        }
        if ( bones.GetBone( "head" ) is { } head )
            Climb( head );
        foreach ( var name in new[] { "arm_upper_R", "arm_upper_L" } )
            if ( bones.GetBone( name )?.Parent is { } shoulder )
                Climb( shoulder );

        MapFingers();
        return _holdBone != null;
    }

    private void MapFingers( bool blend = false )
    {
        _fingersDirty = false;
        _fingersBlend = false;
        double now = RealTime.Now;
        _rightChain?.MapFingers( _rightFingers, blend, now, TransitionSeconds );
        _leftChain?.MapFingers( _leftFingers, blend, now, TransitionSeconds );
    }

    private bool _fingersBlend;

    /// <summary>An elbow hint (weapon space) in character model space, or null when unset.</summary>
    private static Vector3? Hint( Transform weaponModel, Vector3 hint )
        => hint.LengthSquared > 1e-6f ? weaponModel.PointToWorld( hint ) : null;

    /// <summary>Copies an assigned profile's values over the grip properties.</summary>
    private void ApplyProfile()
    {
        var p = Profile;
        if ( p == null )
            return;
        WeaponInHold = p.WeaponOffset;
        HoldBone = string.IsNullOrEmpty( p.HoldBone ) ? HoldBone : p.HoldBone;
        var rightPrimary = !string.Equals( p.PrimaryHand, "L", StringComparison.OrdinalIgnoreCase );
        RightHand = rightPrimary ? p.PrimaryGripTransform : p.SecondaryGripTransform;
        LeftHand = rightPrimary ? p.SecondaryGripTransform : p.PrimaryGripTransform;
        RightFingers = rightPrimary ? p.PrimaryFingerPose : p.SecondaryFingerPose;
        LeftFingers = rightPrimary ? p.SecondaryFingerPose : p.PrimaryFingerPose;
        RightElbowHint = rightPrimary ? p.PrimaryElbowHint : p.SecondaryElbowHint;
        LeftElbowHint = rightPrimary ? p.SecondaryElbowHint : p.PrimaryElbowHint;
        SupportHand = p.TwoHanded;
        Contacts = p.Contacts;
        Actions = p.Actions;
        CharacterActions = p.CharacterActions;
        HoldType = p.HoldType;
        Smoothing = p.Smoothing;
        ReachSlack = p.ReachSlack;
        TransitionSeconds = p.TransitionSeconds;
    }

    private void RefreshData()
    {
        ApplyProfile();
        if ( _parsedOnce
            && _srcWeaponInHold == WeaponInHold && _srcRightHand == RightHand && _srcLeftHand == LeftHand
            && _srcRightFingers == RightFingers && _srcLeftFingers == LeftFingers
            && _srcContacts == Contacts && _srcActions == Actions && _srcCharacterActions == CharacterActions )
        {
            return;
        }

        bool firstParse = !_parsedOnce;
        _parsedOnce = true;

        // A changed grip eases in from what is on screen (no jump while an animation plays).
        double now = RealTime.Now;
        if ( firstParse || _srcWeaponInHold != WeaponInHold )
        {
            _srcWeaponInHold = WeaponInHold;
            var previous = _weaponInHold;
            if ( !ParseTransformProperty( WeaponInHold, nameof( WeaponInHold ), out _weaponInHold ) )
                _weaponInHold = global::Transform.Zero;
            _weaponInHoldBlend.Changed( previous, !firstParse, now, TransitionSeconds );
        }

        if ( firstParse || _srcRightHand != RightHand )
        {
            _srcRightHand = RightHand;
            var previous = _rightHand;
            bool had = _hasRightHand;
            _hasRightHand = ParseTransformProperty( RightHand, nameof( RightHand ), out _rightHand );
            _rightHandBlend.Changed( previous, !firstParse && had && _hasRightHand, now, TransitionSeconds );
        }

        if ( firstParse || _srcLeftHand != LeftHand )
        {
            _srcLeftHand = LeftHand;
            var previous = _leftHand;
            bool had = _hasLeftHand;
            _hasLeftHand = ParseTransformProperty( LeftHand, nameof( LeftHand ), out _leftHand );
            _leftHandBlend.Changed( previous, !firstParse && had && _hasLeftHand, now, TransitionSeconds );
        }

        if ( firstParse || _srcRightFingers != RightFingers )
        {
            _srcRightFingers = RightFingers;
            if ( !HoldData.TryParseFingers( RightFingers, _rightFingers, out var error ) )
                WarnOnce( "RightFingers:" + RightFingers, $"Weapon Hold: RightFingers ignored ({error})." );
            _fingersDirty = true;
            _fingersBlend = !firstParse;
        }

        if ( firstParse || _srcLeftFingers != LeftFingers )
        {
            _srcLeftFingers = LeftFingers;
            if ( !HoldData.TryParseFingers( LeftFingers, _leftFingers, out var error ) )
                WarnOnce( "LeftFingers:" + LeftFingers, $"Weapon Hold: LeftFingers ignored ({error})." );
            _fingersDirty = true;
            _fingersBlend = !firstParse;
        }

        bool rebuildTriggers = false;
        if ( firstParse || _srcContacts != Contacts )
        {
            _srcContacts = Contacts;
            if ( !HoldData.TryParseContacts( Contacts, _contacts, out var error ) )
                WarnOnce( "Contacts:" + Contacts, $"Weapon Hold: Contacts ignored ({error})." );
            rebuildTriggers = true;
        }

        if ( firstParse || _srcActions != Actions )
        {
            _srcActions = Actions;
            if ( !HoldData.TryParseActions( Actions, _actions, out var error ) )
                WarnOnce( "Actions:" + Actions, $"Weapon Hold: Actions ignored ({error})." );
            rebuildTriggers = true;
        }

        if ( firstParse || _srcCharacterActions != CharacterActions )
        {
            _srcCharacterActions = CharacterActions;
            if ( !CharacterOverlay.TryParse( CharacterActions, _overlays, out var error ) )
                WarnOnce( "CharacterActions:" + CharacterActions, $"Weapon Hold: CharacterActions ignored ({error})." );
            _activeOverlay = null;
            _overlay.End();
        }

        if ( rebuildTriggers )
            RebuildTriggers();
    }

    private void RebuildTriggers()
    {
        _triggers.Clear();
        foreach ( var info in _actions.Values )
        {
            if ( info.Role == IdleRole || string.IsNullOrWhiteSpace( info.Trigger ) )
                continue;
            AddTrigger( info.Role, info.Trigger.Trim(), info.Seconds > 0f ? info.Seconds : DefaultActionSeconds );
        }

        // Roles with a contact track but no action entry still react to the usual parameters.
        foreach ( var (role, trigger) in DefaultTriggers )
        {
            if ( !_actions.ContainsKey( role ) && _contacts.ContainsKey( role ) )
                AddTrigger( role, trigger, DefaultActionSeconds );
        }
    }

    /// <summary>
    /// One binding per animgraph parameter (one rising edge), remembering every action that uses
    /// it: fire and aimed fire share b_attack, the reloads share b_reload.
    /// </summary>
    private void AddTrigger( string role, string parameter, float seconds )
    {
        for ( int i = 0; i < _triggers.Count; i++ )
        {
            if ( !string.Equals( _triggers[i].Parameter, parameter, StringComparison.Ordinal ) )
                continue;
            if ( !_triggers[i].Roles.Contains( role ) )
                _triggers[i].Roles.Add( role );
            return;
        }

        _triggers.Add( new TriggerBinding { Role = role, Parameter = parameter, Seconds = seconds, Roles = { role } } );
    }

    /// <summary>
    /// The action a rising edge starts: aimed fire while aiming (when the weapon has that clip),
    /// otherwise the plain action for the parameter (fire, reload, draw), otherwise the first one.
    /// </summary>
    private string ChooseRole( TriggerBinding binding )
    {
        // Shell-by-shell reload when the weapon has an insert clip (and shells to load).
        if ( binding.Roles.Contains( InsertRole ) && HasClip( InsertRole ) )
            return ShellsToLoad <= 0 ? "" : HasClip( ReloadStartRole ) ? ReloadStartRole : InsertRole;
        // Empty: the empty reload / last-round fire when the weapon has them.
        if ( Empty && binding.Roles.Contains( "emptyreload" ) && HasClip( "emptyreload" ) )
            return "emptyreload";
        if ( Empty && binding.Roles.Contains( "fireempty" ) && HasClip( "fireempty" ) )
            return "fireempty";
        if ( binding.Roles.Count == 1 )
            return binding.Roles[0];
        if ( Aiming && binding.Roles.Contains( AimedFireRole ) && HasClip( AimedFireRole ) )
            return AimedFireRole;
        foreach ( var plain in PlainRoles )
            if ( binding.Roles.Contains( plain ) )
                return plain;
        return binding.Roles.FirstOrDefault( r => r != AimedFireRole ) ?? binding.Roles[0];
    }

    private static readonly string[] PlainRoles = { FireRole, "reload", "draw" };

    private bool ParseTransformProperty( string value, string property, out Transform t )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            t = global::Transform.Zero;
            return false;
        }

        if ( HoldData.TryParseTransform( value, out t ) )
            return true;

        WarnOnce( property + ":" + value, $"Weapon Hold: {property} '{value}' is not 'px,py,pz,qx,qy,qz,qw'; ignored." );
        t = global::Transform.Zero;
        return false;
    }

    private void WarnOnce( string key, string message )
    {
        if ( _warned.Add( key ) )
            Log.Warning( message );
    }

    private void ClearOverrides()
    {
        if ( _overrideOwner.IsValid() && _overrideOwner.SceneModel != null )
            _overrideOwner.SceneModel.ClearBoneOverrides();
        _overrideOwner = null;
        _snap = true;
    }

    private static string NormalizeRole( string role )
    {
        return string.IsNullOrWhiteSpace( role ) ? IdleRole : role.Trim().ToLowerInvariant();
    }

    /// <summary>Eases from the weight applied when the action started to the action's own weight.</summary>
    private float CrossFade( float from, float to, bool snap )
    {
        if ( snap || _sinceAction >= ActionBlendSeconds )
            return to;
        return from + (to - from) * HoldData.SmoothStep( _sinceAction / ActionBlendSeconds );
    }

    private static float MoveTowards( float value, float target, float maxDelta )
    {
        if ( maxDelta <= 0f )
            return value;
        return value < target ? MathF.Min( target, value + maxDelta ) : MathF.Max( target, value - maxDelta );
    }

    /// <summary>Eases a baked transform from the value shown when it changed to its new value.</summary>
    private struct TransformBlend
    {
        private Transform _from;
        private double _start;
        private float _seconds;
        private bool _active;

        /// <summary>Records a change; <paramref name="previous"/> is the old baked value.</summary>
        public void Changed( Transform previous, bool blend, double now, float seconds )
        {
            if ( !blend || seconds <= 1e-4f )
            {
                _active = false;
                return;
            }
            // Continue from what is on screen (a change during a running transition starts from it).
            _from = _active ? Evaluate( previous, now ) : previous;
            _start = now;
            _seconds = seconds;
            _active = true;
        }

        public Transform Evaluate( Transform target, double now )
        {
            if ( !_active )
                return target;
            float t = (float)((now - _start) / _seconds);
            if ( t >= 1f )
            {
                _active = false;
                return target;
            }
            return HoldData.Lerp( _from, target, HoldData.SmoothStep( t ) );
        }
    }

    private sealed class TriggerBinding
    {
        public string Role;
        public readonly List<string> Roles = new();
        public string Parameter;
        public float Seconds;
        public bool Previous;
    }

    /// <summary>
    /// One arm subtree (arm_upper_X and every descendant, parents before children) with its
    /// per-frame animation pose, the smoothed IK correction and the posed result.
    /// </summary>
    private sealed class ArmChain
    {
        private const int UpperSlot = 0;

        private BoneCollection.Bone[] _bones;
        private int[] _parent;
        private int _lowerSlot, _handSlot;
        private Transform[] _anim;
        private Transform[] _local;
        private Transform[] _pose;
        private bool[] _hasFinger;
        private Rotation[] _finger;

        // Finger pose transitions: the rotation shown when the pose changed (none = from the
        // animation), eased to the new pose.
        private bool[] _hasFingerFrom;
        private Rotation[] _fingerFrom;
        private double _fingerStart = double.NegativeInfinity;
        private float _fingerSeconds;

        /// <summary>Clock (RealTime.Now) of the current update, for transitions.</summary>
        public double Now;

        // Upper-arm twist helpers: how much of the correction's twist each one cancels (1 at the
        // shoulder, fading down the arm) so the shoulder skin doesn't wring against the chest.
        private float[] _twistCancel;

        // The clavicle takes part of a large arm swing, like a real shoulder, so the deltoid isn't
        // pinched against the chest when the arm is pulled onto the weapon.
        private const float ClavicleShare = 0.3f;
        private const float ClavicleMaxDegrees = 20f;
        private const float ClavicleEngageDegrees = 6f;
        private const float ClavicleFullDegrees = 24f;
        private BoneCollection.Bone _clavicle;
        private Transform _clavicleAnim;
        private Rotation _clavicleDelta = Rotation.Identity;

        // Local transforms of the non-joint bones (twist and elbow helpers, fingers, attachments)
        // from the last clean animation update, which includes the model's procedural
        // constraints. Overridden bones lose those constraints, so the chain re-applies them.
        private Transform[] _final;
        private bool[] _hasFinal;
        private Vector3 _upperAxis = Vector3.Forward;

        // Smoothed local-frame corrections (post-multiplied onto the FK rotation).
        private Rotation _upperDelta = Rotation.Identity;
        private Rotation _lowerDelta = Rotation.Identity;
        private Rotation _handDelta = Rotation.Identity;
        private bool _hasHistory;

        public float Reach { get; private set; }

        public static ArmChain Build( BoneCollection bones, string side )
        {
            var upper = bones.GetBone( "arm_upper_" + side );
            var lower = bones.GetBone( "arm_lower_" + side );
            var hand = bones.GetBone( "hand_" + side );
            if ( upper == null || lower == null || hand == null )
                return null;

            var list = new List<BoneCollection.Bone>();
            var parents = new List<int>();
            var stack = new Stack<(BoneCollection.Bone Bone, int Parent)>();
            stack.Push( (upper, -1) );
            while ( stack.Count > 0 )
            {
                var (bone, parent) = stack.Pop();
                int slot = list.Count;
                list.Add( bone );
                parents.Add( parent );
                var children = bone.Children;
                if ( children == null )
                    continue;
                for ( int i = children.Count - 1; i >= 0; i-- )
                    stack.Push( (children[i], slot) );
            }

            var chain = new ArmChain
            {
                _bones = list.ToArray(),
                _parent = parents.ToArray(),
                _lowerSlot = list.FindIndex( b => b.Index == lower.Index ),
                _handSlot = list.FindIndex( b => b.Index == hand.Index ),
            };

            if ( chain._lowerSlot < 0 || chain._handSlot < 0 )
                return null;

            int n = list.Count;
            chain._anim = new Transform[n];
            chain._local = new Transform[n];
            chain._pose = new Transform[n];
            chain._hasFinger = new bool[n];
            chain._finger = new Rotation[n];
            chain._hasFingerFrom = new bool[n];
            chain._fingerFrom = new Rotation[n];
            chain._twistCancel = new float[n];
            var clavicle = upper.Parent;
            if ( clavicle != null && (clavicle.Name.Contains( "clav", StringComparison.OrdinalIgnoreCase ) || clavicle.Name.Contains( "shoulder", StringComparison.OrdinalIgnoreCase )) )
                chain._clavicle = clavicle;
            chain._final = new Transform[n];
            chain._hasFinal = new bool[n];

            // Bone.LocalTransform is the model-space rest pose; work in the upper arm's frame.
            var upperRest = upper.LocalTransform;
            var toElbow = upperRest.ToLocal( lower.LocalTransform ).Position;
            float upperLength = toElbow.Length;
            if ( upperLength > 1e-3f )
            {
                chain._upperAxis = toElbow / upperLength;
                for ( int i = 0; i < n; i++ )
                {
                    if ( chain._parent[i] != UpperSlot || !list[i].Name.Contains( "twist", StringComparison.OrdinalIgnoreCase ) )
                        continue;
                    // Half at the shoulder, easing off down the arm (the citizen's own constraints
                    // take -0.5 and -0.1 of the arm's twist).
                    var offset = upperRest.ToLocal( list[i].LocalTransform ).Position;
                    float along = 1f - Math.Clamp( Vector3.Dot( offset, chain._upperAxis ) / upperLength, 0f, 1f );
                    chain._twistCancel[i] = 0.5f * along * along;
                }
            }
            return chain;
        }

        public void MapFingers( List<FingerRotation> fingers, bool blend, double now, float seconds )
        {
            // What is shown right now becomes the starting point of the new pose.
            float progress = FingerProgress( now );
            for ( int i = 0; i < _bones.Length; i++ )
            {
                if ( blend && _hasFinger[i] )
                {
                    _fingerFrom[i] = _hasFingerFrom[i] && progress < 1f ? ArmSolver.Slerp( _fingerFrom[i], _finger[i], progress ) : _finger[i];
                    _hasFingerFrom[i] = true;
                }
                else
                {
                    _hasFingerFrom[i] = false;
                }
            }
            _fingerStart = blend ? now : double.NegativeInfinity;
            _fingerSeconds = seconds;

            for ( int i = 0; i < _bones.Length; i++ )
            {
                _hasFinger[i] = false;
                if ( i == UpperSlot || i == _lowerSlot || i == _handSlot )
                    continue;
                for ( int f = 0; f < fingers.Count; f++ )
                {
                    if ( string.Equals( fingers[f].Bone, _bones[i].Name, StringComparison.Ordinal ) )
                    {
                        _hasFinger[i] = true;
                        _finger[i] = fingers[f].Rotation;
                        break;
                    }
                }
            }
        }

        private float FingerProgress( double now )
        {
            if ( _fingerSeconds <= 1e-4f || double.IsNegativeInfinity( _fingerStart ) )
                return 1f;
            return HoldData.SmoothStep( (float)((now - _fingerStart) / _fingerSeconds) );
        }

        /// <summary>Bone indices of the subtree.</summary>
        public void Collect( HashSet<int> into )
        {
            foreach ( var b in _bones )
                into.Add( b.Index );
            if ( _clavicle != null )
                into.Add( _clavicle.Index );
        }

        /// <summary>Reads the animation-only pose of the subtree into character model space.</summary>
        public void ReadAnimation( WeaponHold owner, SkinnedModelRenderer body, Transform bodyTx, bool clean )
        {
            if ( _clavicle != null )
                _clavicleAnim = owner.TryGetAnim( body, _clavicle, out var clavicleWorld ) ? bodyTx.ToLocal( clavicleWorld ) : global::Transform.Zero;

            if ( clean )
            {
                for ( int i = 0; i < _bones.Length; i++ )
                {
                    int parent = _parent[i];
                    if ( parent < 0 || i == _lowerSlot || i == _handSlot )
                        continue;
                    if ( body.TryGetBoneTransform( _bones[i], out var child ) && body.TryGetBoneTransform( _bones[parent], out var parentFinal ) )
                    {
                        _final[i] = parentFinal.ToLocal( child ).WithScale( 1f );
                        _hasFinal[i] = true;
                    }
                }
            }

            for ( int i = 0; i < _bones.Length; i++ )
            {
                if ( owner.TryGetAnim( body, _bones[i], out var world ) )
                    _anim[i] = bodyTx.ToLocal( world );
                else
                    _anim[i] = _parent[i] >= 0 ? _anim[_parent[i]].ToWorld( _bones[_parent[i]].LocalTransform.ToLocal( _bones[i].LocalTransform ) ) : global::Transform.Zero;

                if ( _parent[i] >= 0 )
                    _local[i] = _hasFinal[i] ? _final[i] : _anim[_parent[i]].ToLocal( _anim[i] );
            }

            float a = (_anim[_lowerSlot].Position - _anim[UpperSlot].Position).Length;
            float b = (_anim[_handSlot].Position - _anim[_lowerSlot].Position).Length;
            Reach = a + b;
        }

        public float TargetDistance( Vector3 target ) => (target - _anim[UpperSlot].Position).Length;

        /// <summary>Solves towards the model-space target and updates the smoothed correction. Returns the shortfall.</summary>
        public float Solve( Transform target, float alpha, bool snap, Vector3? elbowHint = null, float hintWeight = 0f )
        {
            var clavicleDelta = ClavicleSwing( target.Position );
            var pivot = _clavicleAnim.Position;
            Vector3 Moved( Vector3 p ) => pivot + clavicleDelta * (p - pivot);

            var upperAnim = clavicleDelta * _anim[UpperSlot].Rotation;
            var lowerAnim = clavicleDelta * _anim[_lowerSlot].Rotation;
            var handAnim = clavicleDelta * _anim[_handSlot].Rotation;

            var solution = ArmSolver.Solve( Moved( _anim[UpperSlot].Position ), Moved( _anim[_lowerSlot].Position ), Moved( _anim[_handSlot].Position ), target.Position, elbowHint, hintWeight );
            var q1 = solution.UpperDelta;
            var q2 = solution.LowerDelta;

            // Express the corrections in each bone's own FK frame so they ride along with the body.
            var upperDelta = upperAnim.Inverse * q1 * upperAnim;
            var lowerFk = q1 * lowerAnim;
            var lowerDelta = lowerFk.Inverse * q2 * lowerFk;
            var handFk = q2 * q1 * handAnim;
            var handDelta = handFk.Inverse * target.Rotation;

            if ( snap || !_hasHistory || alpha >= 1f )
            {
                _upperDelta = upperDelta;
                _lowerDelta = lowerDelta;
                _handDelta = handDelta;
                _clavicleDelta = clavicleDelta;
                _hasHistory = true;
            }
            else if ( alpha > 0f )
            {
                _upperDelta = ArmSolver.Slerp( _upperDelta, upperDelta, alpha );
                _lowerDelta = ArmSolver.Slerp( _lowerDelta, lowerDelta, alpha );
                _handDelta = ArmSolver.Slerp( _handDelta, handDelta, alpha );
                _clavicleDelta = ArmSolver.Slerp( _clavicleDelta, clavicleDelta, alpha );
            }

            return solution.Shortfall;
        }

        /// <summary>Share of the wrist's swing (animated wrist -> target, about the clavicle) the clavicle takes.</summary>
        private Rotation ClavicleSwing( Vector3 target )
        {
            if ( _clavicle == null )
                return Rotation.Identity;
            var pivot = _clavicleAnim.Position;
            var swing = ArmSolver.FromTo( _anim[_handSlot].Position - pivot, target - pivot );
            float angle = MathF.Acos( Math.Clamp( MathF.Abs( swing.w ), 0f, 1f ) ) * 2f * 180f / MathF.PI;
            if ( angle < 1e-3f )
                return Rotation.Identity;
            // Hierarchical: small corrections stay in the elbow and wrist; the clavicle joins in
            // only once the arm has to swing a lot, and never takes more than its share.
            float engage = HoldData.SmoothStep( (angle - ClavicleEngageDegrees) / (ClavicleFullDegrees - ClavicleEngageDegrees) );
            if ( engage <= 0f )
                return Rotation.Identity;
            float share = MathF.Min( angle * ClavicleShare * engage, ClavicleMaxDegrees ) / angle;
            return ArmSolver.Weighted( swing, share );
        }

        /// <summary>Forward kinematics with the weighted correction; writes an override for every bone of the subtree.</summary>
        public void Write( SceneModel sceneModel, float weight )
        {
            var upperDelta = ArmSolver.Weighted( _upperDelta, weight );
            var lowerDelta = ArmSolver.Weighted( _lowerDelta, weight );
            var handDelta = ArmSolver.Weighted( _handDelta, weight );
            var upperTwistInverse = ArmSolver.Twist( upperDelta, _upperAxis ).Inverse;
            var clavicleDelta = ArmSolver.Weighted( _clavicleDelta, weight );
            if ( _clavicle != null )
                sceneModel.SetBoneOverride( _clavicle.Index, new Transform( _clavicleAnim.Position, clavicleDelta * _clavicleAnim.Rotation ) );

            for ( int i = 0; i < _bones.Length; i++ )
            {
                Transform pose;
                int parent = _parent[i];
                if ( parent < 0 )
                {
                    pose = _anim[i];
                    if ( _clavicle != null )
                        pose = new Transform( _clavicleAnim.Position + clavicleDelta * (pose.Position - _clavicleAnim.Position), clavicleDelta * pose.Rotation );
                }
                else if ( _hasFinger[i] )
                {
                    var local = _local[i];
                    var finger = _finger[i];
                    float progress = FingerProgress( Now );
                    if ( progress < 1f )
                        finger = ArmSolver.Slerp( _hasFingerFrom[i] ? _fingerFrom[i] : local.Rotation, finger, progress );
                    local.Rotation = ArmSolver.Slerp( local.Rotation, finger, weight );
                    pose = _pose[parent].ToWorld( local );
                }
                else if ( _twistCancel[i] > 0f )
                {
                    var local = _local[i];
                    local.Rotation = ArmSolver.Slerp( Rotation.Identity, upperTwistInverse, _twistCancel[i] ) * local.Rotation;
                    pose = _pose[parent].ToWorld( local );
                }
                else
                {
                    pose = _pose[parent].ToWorld( _local[i] );
                }

                if ( i == UpperSlot )
                    pose.Rotation = pose.Rotation * upperDelta;
                else if ( i == _lowerSlot )
                    pose.Rotation = pose.Rotation * lowerDelta;
                else if ( i == _handSlot )
                    pose.Rotation = pose.Rotation * handDelta;

                _pose[i] = pose;
                sceneModel.SetBoneOverride( _bones[i].Index, pose );
            }
        }
    }
}
