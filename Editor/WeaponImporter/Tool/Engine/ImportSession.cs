using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Ik;
using WeaponImporter.Core.Import;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Materials;
using WeaponImporter.Core.Setup;
using WeaponImporter.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.Tool;

/// <summary>
/// One weapon being imported: source, analysis, setup, solved grip and validation. The UI
/// observes it through <see cref="Changed"/>; every heavy step runs off the main thread.
/// </summary>
public sealed class ImportSession : IDisposable
{
    public string SourcePath { get; private set; } = "";
    public WeaponAsset Asset { get; private set; }
    public WeaponAnalysis Analysis { get; private set; }
    public WeaponSetup Setup { get; private set; }
    public GripSolution Grip { get; private set; }
    public CharacterRig Character { get; private set; }
    public CharacterPose ReferencePose { get; private set; }
    public List<CheckResult> Checks { get; private set; } = new();
    public string CharacterModel => CharacterLibrary.ModelPath( Setup?.Character );
    public string OutputFolder => string.IsNullOrEmpty( Setup?.OutputFolder ) ? $"weapons/{Setup?.Name ?? "weapon"}" : Setup.OutputFolder;
    public string SetupPath => $"{OutputFolder}/{Setup?.Name}.weapon.json";

    /// <summary>Raised on the main thread whenever anything above changed.</summary>
    public event Action Changed;

    /// <summary>
    /// Raised on every live re-fit while a slider is dragged (only the grip moved). Views that
    /// show the pose follow it; <see cref="Changed"/> comes once the edit settles.
    /// </summary>
    public event Action GripMoved;

    /// <summary>Short human-readable progress/status lines.</summary>
    public event Action<string> Status;

    /// <summary>Bumped on every change; lets views skip stale work.</summary>
    public int Revision { get; private set; }

    private CharacterPoser _poser;

    // Character poses sampled over the actions depend only on the character and weapon type,
    // never on the grip, so the action check reuses them instead of re-simulating the graph.
    private readonly Dictionary<(int, AnimationRole, float, float), CharacterPose> _actionPoses = new();
    private CancellationTokenSource _gripCancel;

    private void Notify()
    {
        Revision++;
        Changed?.Invoke();
    }

    private void Report( IProgress<string> progress, string message )
    {
        progress?.Report( message );
        Status?.Invoke( message );
    }

    // ------------------------------------------------------------------ loading

    /// <summary>Loads and analyzes a weapon file (FBX, GLB, glTF or VMDL), then runs auto setup.</summary>
    public static async Task<ImportSession> LoadAsync( string path, IProgress<string> progress = null, CancellationToken cancel = default )
    {
        var session = new ImportSession { SourcePath = path };
        await session.ReloadAsync( progress, cancel );
        return session;
    }

    public async Task ReloadAsync( IProgress<string> progress = null, CancellationToken cancel = default )
    {
        var path = SourcePath;
        var ext = System.IO.Path.GetExtension( path ).ToLowerInvariant();
        Report( progress, $"Reading {System.IO.Path.GetFileName( path )}" );

        WeaponAsset asset;
        if ( ext == ".vmdl" || ext == ".vmdl_c" )
        {
            await EditorThread.SwitchToMainThread();
            var rel = ToProjectRelative( path ) ?? path;
            asset = VmdlReader.Read( rel.Replace( ".vmdl_c", ".vmdl" ), WeaponLoader.SafeName( path ) );
        }
        else
        {
            asset = await Task.Run( () => WeaponLoader.Load( path ), cancel );
        }
        cancel.ThrowIfCancellationRequested();
        SourceAsset = asset;

        // Existing setup next to a previous import keeps every manual choice.
        await EditorThread.SwitchToMainThread();
        var previous = TryLoadSetup( $"weapons/{asset.Name}/{asset.Name}.weapon.json" );
        var options = previous is { OrientationManual: true } ? new AnalyzeOptions { Rotation = previous.ModelRotationQ, Scale = previous.Scale, ReferenceClip = previous.ReferenceClip } : new AnalyzeOptions();
        _analyzeOptions = options;

        // Attach Textures: images beside the model that belong to its materials.
        Report( progress, "Looking for textures" );
        var isVmdl = ext is ".vmdl" or ".vmdl_c";
        TextureMatches = isVmdl ? new List<TextureMatch>() : await Task.Run( () => TextureMatcher.Match( asset, path ), cancel );
        Asset = WithTextures( asset, previous ?? new WeaponSetup() );

        Report( progress, "Analyzing weapon" );
        var analysis = await AnalyzeAsync( Asset, options, cancel );

        Report( progress, "Configuring setup" );
        Setup = AutoSetup.Build( analysis, previous );
        Setup.Source = path;
        Setup.SourceHash = Hash( path );
        Notify();

        await PrepareCharacterAsync( progress, cancel );
        await ResolveGripAsync( progress, cancel );
    }

    /// <summary>Re-runs the analysis with changed orientation/scale/reference options, keeping manual choices.</summary>
    public async Task ReanalyzeAsync( AnalyzeOptions options, IProgress<string> progress = null, CancellationToken cancel = default )
    {
        Report( progress, "Analyzing weapon" );
        _analyzeOptions = options;
        Asset = WithTextures( SourceAsset, Setup );
        var analysis = await AnalyzeAsync( Asset, options, cancel );
        Setup = AutoSetup.Build( analysis, Setup );
        Notify();
        await ResolveGripAsync( progress, cancel );
    }

    private AnalyzeOptions _analyzeOptions = new();

    /// <summary>Analyses off the main thread, with the grips of the file's own arms.</summary>
    private async Task<WeaponAnalysis> AnalyzeAsync( WeaponAsset asset, AnalyzeOptions options, CancellationToken cancel )
    {
        var (analysis, own) = await Task.Run( () =>
        {
            var a = WeaponAnalyzer.Analyze( asset, options, cancel );
            GripRegions.Of( a ); // cached for validation and the preview
            // The file's own first-person arms, when they hold the weapon, give the best grips.
            return (a, GripExtractor.ExtractOwn( a, OwnGripSource ));
        }, cancel );
        await EditorThread.SwitchToMainThread();
        OwnGrips = own;
        Analysis = analysis;
        return analysis;
    }

    // ------------------------------------------------------------------ textures

    /// <summary>The weapon as read from its file (before textures were attached).</summary>
    public WeaponAsset SourceAsset { get; private set; }

    /// <summary>Every material slot: textures the file links, and images found beside it.</summary>
    public List<TextureMatch> TextureMatches { get; private set; } = new();

    /// <summary>Whether a found (not linked) texture is used: the user's choice, else Attach Textures and confidence.</summary>
    public static bool Accepted( WeaponSetup setup, TextureMatch match )
    {
        if ( match.Linked )
            return true;
        if ( setup.TextureChoices.TryGetValue( match.Key, out var chosen ) )
            return string.Equals( chosen, match.Path, StringComparison.OrdinalIgnoreCase );
        return setup.AttachTextures && match.Confidence >= TextureMatcher.AutoAttach;
    }

    private WeaponAsset WithTextures( WeaponAsset source, WeaponSetup setup )
    {
        if ( source is null )
            return null;
        var chosen = TextureMatches.Where( m => !m.Linked && Accepted( setup, m ) )
            // One file per slot: the best accepted candidate.
            .GroupBy( m => m.Key ).Select( g => g.OrderByDescending( m => m.Confidence ).First() ).ToList();
        return chosen.Count == 0 ? source : TextureMatcher.Apply( source, chosen );
    }

    /// <summary>Re-applies texture choices (Attach Textures toggled, a proposal accepted or rejected).</summary>
    public Task ApplyTextureChoicesAsync( IProgress<string> progress = null, CancellationToken cancel = default )
        => ReanalyzeAsync( _analyzeOptions, progress, cancel );

    /// <summary>Runs the one-click setup again from scratch (manual choices are cleared).</summary>
    public async Task AutoSetupAsync( IProgress<string> progress = null, CancellationToken cancel = default )
    {
        var fresh = new WeaponSetup { Name = Setup?.Name ?? Asset.Name, Source = SourcePath, Character = Setup?.Character ?? "human", OutputFolder = Setup?.OutputFolder ?? "" };
        Setup = AutoSetup.Build( Analysis, fresh );
        Notify();
        await PrepareCharacterAsync( progress, cancel );
        await ResolveGripAsync( progress, cancel );
    }

    // ------------------------------------------------------------------ character + grip

    /// <summary>Loads the character, derives its hands and samples the holdtype reference pose.</summary>
    public async Task PrepareCharacterAsync( IProgress<string> progress = null, CancellationToken cancel = default )
    {
        await EditorThread.SwitchToMainThread();
        Report( progress, "Posing the character" );
        // The new character is checked before the current one is replaced, so a model that
        // can't be used leaves the session as it was.
        var poser = new CharacterPoser( CharacterModel );
        CharacterRig rig;
        try
        {
            rig = CharacterRig.From( poser.Skeleton ) ?? throw new InvalidOperationException( $"'{CharacterModel}' has no recognisable arms and hands." );
            await EditorThread.NextFrame( cancel );
            if ( !poser.Animates( rig, Setup.EffectiveHoldType ) )
                throw new InvalidOperationException( $"{CharacterLibrary.Label( Setup.Character )}'s animation graph leaves its arms in the bind pose, so there is no hold animation to fit the grip to." );
        }
        catch
        {
            poser.Dispose();
            throw;
        }
        _poser?.Dispose();
        _actionPoses.Clear();
        _poser = poser;
        Character = rig;
        // Each step drives the character's animgraph on the main thread: one frame per step
        // keeps the editor responsive (the first import pays for loading the graph).
        await EditorThread.NextFrame( cancel );
        await ChooseHoldAsync( progress, cancel );
        ReferencePose = _poser.Sample( Setup.EffectiveHoldType );
        await MeasureActionsAsync( cancel );
        await WarmActionPosesAsync( cancel );
        await SampleActionTracksAsync( cancel );
        Notify();
    }

    /// <summary>Every hold animation tried for this weapon with its fit (best first).</summary>
    public List<(int Hold, float Score)> HoldRanking { get; private set; } = new();

    /// <summary>
    /// Tries the character's hold animations that suit the weapon type: a quick grip on each,
    /// judged by how well the hands fit (grasp, wrist bend, reach). The best one is used unless
    /// the user picked a hold.
    /// </summary>
    private async Task ChooseHoldAsync( IProgress<string> progress, CancellationToken cancel )
    {
        HoldRanking = new List<(int, float)>();
        var candidates = GripSolver.CandidateHolds( Setup.Type );
        if ( Setup.HoldTypeManual || candidates.Length < 2 || Analysis?.Primary is null )
            return;
        Report( progress, "Trying hold animations" );
        var analysis = Analysis;
        var primary = AutoSetup.Candidate( analysis, Setup.Primary ) ?? analysis.Primary;
        var support = Setup.UseSupportHand && Setup.Support is not null ? AutoSetup.Candidate( analysis, Setup.Support ) : null;
        var character = Character;
        var options = Options() with { Fast = true };
        foreach ( var hold in candidates )
        {
            cancel.ThrowIfCancellationRequested();
            var pose = _poser.Sample( hold );
            var solution = await Task.Run( () => GripSolver.Solve( analysis, primary, support, character, pose, options, cancel ), cancel );
            await EditorThread.SwitchToMainThread();
            // The weapon type's usual hold wins ties.
            var score = GripSolver.FitScore( solution ) + (hold == candidates[0] ? 0.5f : 0f);
            HoldRanking.Add( (hold, score) );
        }
        HoldRanking.Sort( ( a, b ) => b.Score.CompareTo( a.Score ) );
        var best = HoldRanking[0].Hold;
        Setup.HoldType = best == WeaponTypes.HoldType( Setup.Type ) ? -1 : best;
    }

    /// <summary>The character's actions sampled once per character/type (support-hand planning input).</summary>
    public List<ActionTrack> ActionTracks { get; private set; } = new();

    /// <summary>Last support-hand plan (magazine grab, reload touches).</summary>
    public ActionContactPlan ContactPlan { get; private set; }

    /// <summary>Measures how long the character's own actions run (per trigger) for this holdtype.</summary>
    private async Task MeasureActionsAsync( CancellationToken cancel )
    {
        var key = $"{CharacterModel}|{Setup.EffectiveHoldType}";
        if ( Setup.ActionSecondsKey == key && Setup.ActionSeconds.Count > 0 )
            return;
        var measured = new Dictionary<AnimationRole, float>();
        var byTrigger = new Dictionary<string, float>();
        foreach ( var role in AnimationRoles.All )
        {
            if ( AnimationRoles.GraphTrigger( role ) is not { } trigger )
                continue;
            if ( !byTrigger.TryGetValue( trigger, out var seconds ) )
            {
                // Median of three: one measurement can be a frame off (random idle layers).
                var samples = new List<float>();
                for ( var i = 0; i < 3; i++ )
                {
                    await EditorThread.NextFrame( cancel );
                    if ( _poser is null )
                        return;
                    samples.Add( _poser.MeasureAction( Setup.EffectiveHoldType, trigger ) );
                }
                samples.Sort();
                byTrigger[trigger] = seconds = samples[1];
            }
            if ( seconds > 0.05f )
                measured[role] = MathF.Round( seconds, 3 );
        }
        // Replaced whole: playback never times an action from a half-measured set.
        Setup.ActionSeconds = measured;
        Setup.ActionSecondsKey = key;
    }

    private async Task SampleActionTracksAsync( CancellationToken cancel )
    {
        var tracks = new List<ActionTrack>();
        if ( _poser is not null && Character?.Left is not null )
        {
            foreach ( var role in ActionTiming.ReloadRoles )
            {
                var seconds = ActionTiming.Seconds( Setup, Analysis?.Asset, role );
                // Same trigger and length: reuse the samples.
                var same = tracks.FirstOrDefault( t => MathF.Abs( t.Seconds - seconds ) < 1e-3f );
                if ( same is null )
                    await EditorThread.NextFrame( cancel );
                if ( _poser is null )
                    break;
                var samples = same?.Samples ?? _poser.SampleTrack( Setup.EffectiveHoldType, AnimationRoles.GraphTrigger( role ), seconds, 49 );
                tracks.Add( new ActionTrack( role, seconds, samples ) );
            }
        }
        // Published whole: nothing sees a half-sampled set between frames.
        ActionTracks = tracks;
    }

    /// <summary>A character pose sampled at an action's normalized time (for per-animation checks).</summary>
    public CharacterPose SampleAction( AnimationRole role, float normalizedTime, float seconds )
    {
        if ( _poser is null )
            return ReferencePose;
        var key = (Setup.EffectiveHoldType, role, normalizedTime, seconds);
        if ( _actionPoses.TryGetValue( key, out var cached ) )
            return cached;
        var trigger = AnimationRoles.GraphTrigger( role );
        var pose = _poser.Sample( Setup.EffectiveHoldType, 0.6f + normalizedTime * seconds, trigger, 0.6f );
        _actionPoses[key] = pose;
        return pose;
    }

    /// <summary>
    /// Solves the hands against the geometry (cancels a previous solve still running). A quick
    /// pass is shown immediately (<see cref="GripMoved"/>), then the full-quality solve replaces
    /// it. <paramref name="edited"/>: only that hand changed, so the other keeps its pose during
    /// the quick pass (and the right hand keeps it entirely when only the left changed).
    /// </summary>
    public async Task ResolveGripAsync( IProgress<string> progress = null, CancellationToken cancel = default, Side? edited = null, bool quickOnly = false )
    {
        if ( Analysis is null || Character is null || ReferencePose is null )
            return;
        _gripCancel?.Cancel();
        _gripCancel = CancellationTokenSource.CreateLinkedTokenSource( cancel );
        var token = _gripCancel.Token;

        Report( progress, "Fitting hands to the weapon" );
        var analysis = Analysis;
        var setup = Setup;
        var primary = AutoSetup.Candidate( analysis, setup.Primary ) ?? analysis.Primary;
        var support = setup.UseSupportHand && setup.Support is not null ? AutoSetup.Candidate( analysis, setup.Support ) : null;
        if ( primary is null )
        {
            Grip = null;
            Revalidate();
            Notify();
            return;
        }
        var options = Options();
        var character = Character;
        var reference = ReferencePose;
        var previous = Grip;

        GripSolution solution;
        ActionContactPlan plan;
        // Settle against the last full-quality grip (at first the one saved with the setup), not
        // the quick preview below.
        _settledGrip ??= Setup.Baked;
        try
        {
            // 1. Quick pass, on screen in a frame or two.
            var quick = options with
            {
                Fast = true,
                RightOverride = options.RightOverride ?? (edited == Side.Left ? previous?.Right : null),
                LeftOverride = options.LeftOverride ?? (edited == Side.Right ? previous?.Left : null),
            };
            var fast = await Task.Run( () => GripSolver.Solve( analysis, primary, support, character, reference, quick, token ), token );
            await EditorThread.SwitchToMainThread();
            if ( token.IsCancellationRequested )
                return;
            Grip = fast;
            Setup.Baked = Bake( fast );
            GripMoved?.Invoke();
            // While a grip is dragged only quick fits run; the full fit follows on release.
            if ( quickOnly )
                return;

            // 2. Full quality. A left-hand edit never changes the right hand.
            var full = options with { RightOverride = options.RightOverride ?? (edited == Side.Left ? previous?.Right : null) };
            Report( progress, "Refining the grip" );
            var tracks = ActionTracks;
            (solution, plan) = await Task.Run( () =>
            {
                var s = GripSolver.Solve( analysis, primary, support, character, reference, full, token );
                // Support hand over the actions (magazine during reloads), from the same grip.
                var p = ActionContacts.Plan( analysis, setup, character, s, tracks, full, token );
                return (s, p);
            }, token );
        }
        catch ( OperationCanceledException )
        {
            return;
        }
        await EditorThread.SwitchToMainThread();
        if ( token.IsCancellationRequested )
            return;
        Grip = solution;
        ContactPlan = plan;
        ActionContacts.Apply( Setup, plan );
        Setup.Baked = _settledGrip = Bake( solution );
        Revalidate();
        Notify();
        try
        {
            await SweepActionsAsync( progress, token );
        }
        catch ( OperationCanceledException )
        {
            return;
        }
        Report( progress, "Ready" );
    }

    public const string OwnGripSource = "This weapon's first-person animation";

    /// <summary>Grips captured from the weapon file's own arms (empty without first-person arms).</summary>
    public List<GripPreset> OwnGrips { get; private set; } = new();

    /// <summary>Every grip the hands can take, best sources first (own, project, built-in).</summary>
    public List<GripPreset> Grips => GripLibraryStore.All( OwnGrips.Concat( LearnedGrips ) );

    /// <summary>GrabNet grips of this weapon (from its setup), ranked below its own animation's grips.</summary>
    private IEnumerable<GripPreset> LearnedGrips => (Setup?.LearnedGrips ?? new List<GripPreset>()).Select( p =>
    {
        p.Priority = GrabNetGrips.Priority;
        return p;
    } );

    /// <summary>Writes the weapon the way GrabNet expects it (OBJ, metres, canonical frame).</summary>
    public void ExportForGrabNet( string path )
    {
        if ( Analysis is null )
            throw new InvalidOperationException( "Import a weapon first." );
        System.IO.File.WriteAllText( path, GrabNetGrips.ExportObj( Analysis ) );
    }

    /// <summary>
    /// Adds the grasps of a GrabNet result file (21 keypoints per hand, in the exported frame) as
    /// grips of this weapon. Grasps with the same name are replaced, so re-reading a file is safe.
    /// </summary>
    public async Task<(int Added, List<string> Skipped)> AddLearnedGraspsAsync( string path, CancellationToken cancel = default )
    {
        var analysis = Analysis ?? throw new InvalidOperationException( "Import a weapon first." );
        var source = $"{GrabNetGrips.Source} · {System.IO.Path.GetFileNameWithoutExtension( path )}";
        var (presets, skipped) = await Task.Run( () =>
        {
            var grasps = GrabNetGrips.Parse( System.IO.File.ReadAllText( path ) );
            var list = GrabNetGrips.ToPresets( analysis, grasps, source, out var skip );
            return (list, skip);
        }, cancel );
        await EditorThread.SwitchToMainThread();
        Setup.LearnedGrips ??= new List<GripPreset>();
        Setup.LearnedGrips.RemoveAll( p => presets.Any( n => n.Name == p.Name ) );
        Setup.LearnedGrips.AddRange( presets );
        Notify();
        return (presets.Count, skipped);
    }

    private GripOptions Options() => new()
    {
        Backend = new ChoiceGripGenerator( Grips, Setup.Primary?.Preset, Setup.Support?.Preset ),
        UseSupportHand = Setup.UseSupportHand,
        IndexOnTrigger = Setup.IndexOnTrigger,
        WristPreference = Setup.Ik.WristPreference,
        WeaponOffset = Setup.Ik.WeaponOffset,
        RightOverride = Setup.Primary?.Pose?.ToPose(),
        LeftOverride = Setup.Support?.Pose?.ToPose(),
    };

    /// <summary>
    /// Live re-fit for fine-tune and finger edits: keeps the solved hand placements, re-places
    /// the weapon and re-runs the arm IK on the main thread (well under a millisecond). Returns
    /// false when there is no grip yet, in which case a full <see cref="ResolveGripAsync"/> is needed.
    /// </summary>
    public bool Refit( Side? editedHand = null, HandPose editedPose = null )
    {
        if ( Grip is null || Analysis is null || Character is null || ReferencePose is null )
            return false;
        if ( editedHand is { } hand && editedPose is not null )
        {
            var grip = hand == Side.Right ? Setup.Primary : Setup.Support;
            if ( grip is null || (hand == Side.Left && Grip.Left is null) )
                return false;
            grip.Pose = HandPoseSetup.From( editedPose );
            grip.Manual = true;
        }
        _gripCancel?.Cancel();
        Grip = GripSolver.Refit( Analysis, Grip, Character, ReferencePose, Options(),
            editedHand == Side.Right ? editedPose : null, editedHand == Side.Left ? editedPose : null );
        Setup.Baked = _settledGrip = Bake( Grip );
        GripMoved?.Invoke();
        return true;
    }

    /// <summary>After live edits settle: re-check the actions and validation, then notify everyone.</summary>
    public async Task SettleAsync( IProgress<string> progress = null, CancellationToken cancel = default )
    {
        Revalidate();
        Notify();
        await ReplanContactsAsync( cancel );
        await SweepActionsAsync( progress, cancel );
    }

    /// <summary>
    /// Slides a hand's grasp to another spot on the weapon, continuously: the wrist keeps its
    /// place relative to the local surface frame, the fingers close again on the new surface and
    /// the weapon is re-placed (no search, well under a millisecond or two). Used while a grip
    /// contact is dragged in the viewport; returns false when there is no grip to slide.
    /// </summary>
    public float? SlideGrip( Side hand, N.Vector3 point, N.Vector3 normal )
    {
        if ( Grip is null || Analysis is null || Character is null )
            return null;
        var current = hand == Side.Right ? Grip.Right : Grip.Left;
        var request = hand == Side.Right ? Grip.RightRequest : Grip.LeftRequest;
        var grip = hand == Side.Right ? Setup.Primary : Setup.Support;
        if ( current is null || request is null || grip is null )
            return null;
        if ( GripSolver.Slide( Analysis.WeaponBvh, request, current, point, normal ) is not { } slid )
            return null;
        var (pose, moved) = slid;
        grip.Contact = V.A( moved.Surface.Contact );
        grip.Normal = V.A( moved.Surface.Normal );
        grip.Axis = V.A( moved.Surface.Axis );
        grip.Manual = true;
        // Keep the slid request so the next step continues from this spot.
        Grip = new GripSolution
        {
            WeaponInHold = Grip.WeaponInHold,
            Right = Grip.Right,
            Left = Grip.Left,
            RightQuality = Grip.RightQuality,
            LeftQuality = Grip.LeftQuality,
            RightRequest = hand == Side.Right ? moved : Grip.RightRequest,
            LeftRequest = hand == Side.Left ? moved : Grip.LeftRequest,
            Posed = Grip.Posed,
            RightSource = Grip.RightSource,
            LeftSource = Grip.LeftSource,
        };
        if ( !Refit( hand, pose ) )
            return null;
        // How far the grasp still is from the dragged spot (it moves a little each step).
        return N.Vector3.Distance( moved.Surface.Contact, point );
    }

    /// <summary>Re-plans the support hand over the actions for the current grip (after edits).</summary>
    public async Task ReplanContactsAsync( CancellationToken cancel = default )
    {
        if ( Grip is null || Analysis is null || Character is null )
            return;
        var (analysis, setup, character, grip, tracks, options) = (Analysis, Setup, Character, Grip, ActionTracks, Options());
        var plan = await Task.Run( () => ActionContacts.Plan( analysis, setup, character, grip, tracks, options, cancel ), cancel );
        await EditorThread.SwitchToMainThread();
        if ( cancel.IsCancellationRequested || !ReferenceEquals( grip, Grip ) )
            return;
        ContactPlan = plan;
        ActionContacts.Apply( Setup, plan );
        GripMoved?.Invoke();
    }

    // Last full-quality baked grip: sampling noise below BakedGrip's tolerances keeps its values.
    private BakedGrip _settledGrip;

    /// <summary>The solved grip in the form <c>WeaponHold</c> consumes (model space == canonical space).</summary>
    public BakedGrip Bake( GripSolution s )
    {
        var baked = new BakedGrip
        {
            Character = CharacterModel,
            HoldBone = Character.Right.Skeleton[Character.HoldBone].Name,
            WeaponInHold = V.A( s.WeaponInHold ),
            RightHand = V.A( s.Right.Wrist ),
            LeftHand = s.Left is null ? null : V.A( s.Left.Wrist ),
            Quality = s.RightQuality.ToString(),
        };
        // Elbows where the fitted reference pose has them (weapon space): runtime IK leans toward them.
        var weaponWorld = GripSolver.WeaponWorld( s.Posed, Character, s.WeaponInHold );
        baked.RightElbow = V.A( XForm.ToLocal( weaponWorld, s.Posed.World[Character.Right.LowerArm] ).Pos );
        if ( s.Left is not null && Character.Left is { } leftRig )
            baked.LeftElbow = V.A( XForm.ToLocal( weaponWorld, s.Posed.World[leftRig.LowerArm] ).Pos );
        void Fingers( HandRig rig, HandPose pose, Dictionary<string, float[]> into )
        {
            foreach ( var finger in rig.Fingers )
                for ( var j = 0; j < finger.Joints.Length; j++ )
                    into[rig.Skeleton[finger.Joints[j]].Name] = V.A( pose.LocalRotation( rig, finger, j ) );
        }
        Fingers( Character.Right, s.Right, baked.RightFingers );
        if ( s.Left is not null && Character.Left is not null )
            Fingers( Character.Left, s.Left, baked.LeftFingers );
        return baked.SettledOn( _settledGrip );
    }

    // ------------------------------------------------------------------ edits

    /// <summary>Sets a grip from a point the user clicked on the weapon (canonical space).</summary>
    public async Task PickGripAsync( Side hand, N.Vector3 point, N.Vector3? normal = null, bool quickOnly = false )
    {
        var surface = Core.Geometry.SurfaceProbe.FromPoint( Analysis.WeaponBvh, point + (normal ?? N.Vector3.Zero) * 0.05f, hand == Side.Right ? N.Vector3.UnitZ : N.Vector3.UnitX );
        if ( surface is null )
            return;
        var existing = hand == Side.Right ? Setup.Primary : Setup.Support;
        var style = existing?.Style ?? (hand == Side.Right ? GripStyle.Wrap : GripStyle.Cradle);
        var grip = new GripCandidate { Surface = surface, Style = style, Confidence = 1f, Reason = "picked on the weapon", Manual = true };
        var setup = AutoSetup.Grip( grip );
        setup.Manual = true;
        if ( hand == Side.Right )
            Setup.Primary = setup;
        else
        {
            Setup.Support = setup;
            Setup.UseSupportHand = true;
        }
        Notify();
        await ResolveGripAsync( edited: hand, quickOnly: quickOnly );
    }

    /// <summary>Clears manual grip edits for one hand and lets the importer place it again.</summary>
    public async Task AutoFitAsync( Side? hand )
    {
        if ( hand is null or Side.Right )
        {
            if ( Analysis.Primary is { } p )
                Setup.Primary = AutoSetup.Grip( p );
        }
        if ( hand is null or Side.Left )
        {
            Setup.Support = Analysis.Support is { } s ? AutoSetup.Grip( s ) : null;
            Setup.UseSupportHand = Setup.Support is not null;
        }
        Notify();
        await ResolveGripAsync( edited: hand );
    }

    /// <summary>Stores a hand pose edited in the viewport.</summary>
    public async Task SetHandPoseAsync( Side hand, HandPose pose )
    {
        var grip = hand == Side.Right ? Setup.Primary : Setup.Support;
        if ( grip is null )
            return;
        grip.Pose = HandPoseSetup.From( pose );
        grip.Manual = true;
        Notify();
        await ResolveGripAsync();
    }

    public void MarkChanged() => Notify();

    private static readonly AnimationRole[] CheckedRoles = { AnimationRole.Fire, AnimationRole.Reload, AnimationRole.Draw };

    /// <summary>The checked actions with the length they run for (same timing as the game).</summary>
    private IEnumerable<(AnimationRole Role, float Seconds)> SweepRoles
        => CheckedRoles.Select( r => (r, ActionTiming.Seconds( Setup, Analysis?.Asset, r )) );
    private static readonly float[] SweepTimes = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

    /// <summary>
    /// Samples the character over the checked actions up front (during loading), so the action
    /// check that follows every edit is pure maths and never stalls the editor.
    /// </summary>
    /// <summary>Samples the poses the action sweep checks, one frame per action.</summary>
    private async Task WarmActionPosesAsync( CancellationToken cancel )
    {
        foreach ( var (role, seconds) in SweepRoles )
        {
            await EditorThread.NextFrame( cancel );
            if ( _poser is null )
                return;
            foreach ( var t in SweepTimes )
                SampleAction( role, t, seconds );
        }
    }

    /// <summary>Last results of <see cref="SweepActionsAsync"/>.</summary>
    public List<(AnimationRole Role, float Time, string Message)> ActionIssues { get; private set; } = new();

    /// <summary>
    /// Replays the grip over the character's own actions (fire, reload, draw) at several moments:
    /// where the support hand should be locked it must reach, and the firing wrist must not twist
    /// far from the animation. Results feed validation.
    /// </summary>
    public async Task SweepActionsAsync( IProgress<string> progress = null, CancellationToken cancel = default )
    {
        await EditorThread.SwitchToMainThread();
        if ( Grip is null || Character is null || _poser is null )
            return;
        Report( progress, "Checking hands over the animations" );
        var issues = new List<(AnimationRole, float, string)>();
        foreach ( var (role, seconds) in SweepRoles )
        {
            Setup.Contacts.TryGetValue( role, out var track );
            foreach ( var t in SweepTimes )
            {
                cancel.ThrowIfCancellationRequested();
                var pose = SampleAction( role, t, seconds ).Clone();
                var leftWeight = Setup.UseSupportHand && Grip.Left is not null ? track?.Weight( Side.Left, t, seconds ) ?? 1f : 0f;
                var rightWeight = track?.Weight( Side.Right, t, seconds ) ?? 1f;
                var (right, left, rightCorr, _) = GripSolver.Apply( pose, Character, Grip.WeaponInHold, Grip.Right, leftWeight > 0.5f ? Grip.Left : null, rightWeight, leftWeight );
                if ( leftWeight > 0.5f && !left.Reached && left.Shortfall > Setup.Ik.ReachSlack )
                    issues.Add( (role, t, $"Left hand is {left.Shortfall:0.0} in short of the grip") );
                var bend = rightWeight > 0.5f ? GripSolver.WristBend( pose, Character.Right ) : 0f;
                // Transient action frames get headroom; only lasting problems are reported below.
                if ( bend > GripSolver.MaxWristBend + 20f )
                    issues.Add( (role, t, $"Right wrist bends {bend:0}° against the forearm") );
                await EditorThread.Delay( 1, cancel );
            }
            await EditorThread.Delay( 1, cancel );
        }
        // A problem is real when it lasts: at least two sampled moments of the same action.
        ActionIssues = issues.GroupBy( i => (i.Item1, i.Item3.Split( ' ' )[0] + i.Item3.Split( ' ' )[1]) )
            .Where( g => g.Count() >= 2 )
            .SelectMany( g => g )
            .ToList();
        Revalidate();
        Notify();
    }

    public void Revalidate( IReadOnlyDictionary<string, IReadOnlyList<string>> compileErrors = null, IReadOnlyCollection<string> compiledSequences = null )
    {
        if ( Setup is null )
            return;
        Checks = Validation.Run( new ValidationInput
        {
            Setup = Setup,
            Analysis = Analysis,
            Grip = Grip,
            CompileErrors = compileErrors,
            CompiledSequences = compiledSequences,
            ActionIssues = ActionIssues,
            Changed = () => { _ = ResolveGripAsync(); Notify(); },
        } );
        if ( SetupLoadProblem is { } problem )
            Checks.Insert( 0, new CheckResult { Name = "Previous setup", Severity = CheckSeverity.Warning, Message = problem } );
    }

    public List<string> ApplyTemplate( string setupJsonPath, TemplateParts parts = TemplateParts.All )
    {
        var template = WeaponSetup.FromJson( System.IO.File.ReadAllText( setupJsonPath ) );
        var notes = TemplateAdapter.Apply( Setup, Analysis, template, parts );
        Notify();
        _ = ResolveGripAsync();
        return notes;
    }

    // ------------------------------------------------------------------ persistence

    public void Save()
    {
        if ( Setup is null )
            return;
        Setup.OutputFolder = OutputFolder;
        AssetCompiler.WriteText( SetupPath, Setup.ToJson() );
    }

    public Task<BakeResult> BakeAsync( IProgress<string> progress = null, CancellationToken cancel = default )
        => WeaponBaker.BakeAsync( this, progress, cancel );

    /// <summary>Why the previous setup next to the weapon could not be used (shown in Check), or null.</summary>
    public string SetupLoadProblem { get; private set; }

    /// <summary>
    /// The setup saved by a previous import. An unreadable one is kept as <c>.bak</c> (the next
    /// bake overwrites the original) and reported, so manual choices never disappear silently.
    /// </summary>
    private WeaponSetup TryLoadSetup( string relative )
    {
        SetupLoadProblem = null;
        var abs = AssetCompiler.Absolute( relative );
        if ( !System.IO.File.Exists( abs ) )
            return null;
        try
        {
            return WeaponSetup.FromJson( System.IO.File.ReadAllText( abs ) );
        }
        catch ( Exception e ) when ( e is not OperationCanceledException )
        {
            var backup = abs + ".bak";
            try
            {
                System.IO.File.Copy( abs, backup, true );
            }
            catch ( Exception copy ) when ( copy is System.IO.IOException or UnauthorizedAccessException )
            {
                backup = null;
            }
            SetupLoadProblem = $"{relative} could not be read ({e.Message}); the weapon was set up from scratch." + (backup is null ? "" : $" The old file is kept as {System.IO.Path.GetFileName( backup )}.");
            Log.Warning( $"[weapon importer] {SetupLoadProblem}" );
            return null;
        }
    }

    private static string Hash( string path )
    {
        try
        {
            using var stream = System.IO.File.OpenRead( path );
            var hash = System.Security.Cryptography.SHA256.HashData( stream );
            return Convert.ToHexString( hash, 0, 12 );
        }
        catch ( Exception )
        {
            return "";
        }
    }

    /// <summary>Project-relative path of a file inside the assets folder, or null.</summary>
    public static string ToProjectRelative( string absolute )
    {
        var root = AssetCompiler.AssetsRoot;
        if ( string.IsNullOrEmpty( root ) )
            return null;
        var full = System.IO.Path.GetFullPath( absolute );
        var r = System.IO.Path.GetFullPath( root ).TrimEnd( '\\', '/' ) + System.IO.Path.DirectorySeparatorChar;
        return full.StartsWith( r, StringComparison.OrdinalIgnoreCase ) ? full[r.Length..].Replace( '\\', '/' ) : null;
    }

    public void Dispose()
    {
        _gripCancel?.Cancel();
        _poser?.Dispose();
        _poser = null;
    }
}
