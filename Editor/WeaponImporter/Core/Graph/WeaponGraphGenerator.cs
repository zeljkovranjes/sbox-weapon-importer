#nullable enable annotations

using WeaponImporter.Core.Analysis;

namespace WeaponImporter.Core.Graph;

/// <summary>One weapon clip the graph can play: its role, compiled sequence name, looping and length.</summary>
public sealed record GraphClip(AnimationRole Role, string Sequence, bool Loop, float Seconds);

/// <summary>Parameter names of generated weapon graphs (Facepunch first-person conventions where they exist).</summary>
public static class WeaponParams
{
    public const string Attack = "b_attack";
    public const string AttackDry = "b_attack_dry";
    public const string Empty = "b_empty";
    public const string Reload = "b_reload";
    public const string DeploySkip = "b_deploy_skip";
    public const string Holster = "b_holster";
    public const string Inspect = "b_inspect";
    public const string Sprint = "b_sprint";
    public const string MoveBob = "move_bob";
    public const string Ironsights = "ironsights";
    public const string FireMode = "b_firemode";
    public const string Pump = "b_pump";
    public const string Melee = "b_melee";
    public const string Jump = "b_jump";
    public const string SpeedReload = "speed_reload";
    public const string SpeedDeploy = "speed_deploy";
    public const string SpeedIronsights = "speed_ironsights";

    /// <summary>Event tag names (graph tags, dispatched to OnAnimTagEvent).</summary>
    public const string TagHolsterFinished = "holster_finished";
    public const string TagReloadIncrement = "reload_increment";
    public const string TagAttackDiscouraged = "attack_discouraged";
}

/// <summary>What the generated graph supports, for the review screen and docs.</summary>
public sealed record GraphSummary(IReadOnlyList<string> States, IReadOnlyList<string> Parameters, IReadOnlyList<string> Tags);

/// <summary>
/// Generates a first-person weapon AnimGraph: one state machine whose states exist only for the
/// clips the weapon actually has, driven by the Facepunch parameter names (b_attack, b_reload,
/// b_empty, b_holster, b_deploy_skip, b_sprint, ironsights) so existing first-person weapon code
/// works unchanged. Ported from the original importer's DefaultGraphGenerator; the graph logic
/// is unchanged, only the input is now a flat clip list.
/// </summary>
public static class WeaponGraphGenerator
{
    private const float QuickBlend = 0.05f;
    private const float Blend = 0.15f;
    private const float LocomotionBlend = 0.25f;

    /// <summary>Default cycles when the clip carries no holster_complete / shell_insert event.</summary>
    private const float HolsterFinishCycle = 0.9f;
    private const float InsertCycle = 0.55f;

    /// <summary>
    /// The original importer's role set. v3 roles map onto a subset of these (see <see cref="SlotFor"/>);
    /// the shell-reload / fire-mode / jump branches are kept so the graph logic stays identical.
    /// </summary>
    private enum Slot
    {
        Idle, Fire, FireLast, FireAds, DryFire, Reload, ReloadEmpty, ReloadStart, ReloadInsert, ReloadEnd,
        Deploy, Holster, Inspect, Walk, Sprint, AdsIn, AdsIdle, AdsOut, FireMode, Pump, Bolt, Melee, Jump,
    }

    private static Slot? SlotFor(AnimationRole role) => role switch
    {
        AnimationRole.Idle => Slot.Idle,
        AnimationRole.Fire => Slot.Fire,
        AnimationRole.FireEmpty => Slot.FireLast,       // attack while empty (b_attack + b_empty)
        AnimationRole.Reload => Slot.Reload,
        AnimationRole.TacticalReload => Slot.Reload,    // used only when there is no plain reload
        AnimationRole.EmptyReload => Slot.ReloadEmpty,
        AnimationRole.Draw => Slot.Deploy,
        AnimationRole.Holster => Slot.Holster,
        AnimationRole.Inspect => Slot.Inspect,
        AnimationRole.Sprint => Slot.Sprint,
        AnimationRole.Walk => Slot.Walk,
        // ADS raises the sights: the graph's "Aim" state plays it and holds its last frame.
        AnimationRole.Ads => Slot.AdsIn,
        AnimationRole.AdsOut => Slot.AdsOut,
        AnimationRole.AdsIdle => Slot.AdsIdle,
        AnimationRole.AdsFire => Slot.FireAds,
        AnimationRole.Melee => Slot.Melee,
        AnimationRole.Bolt => Slot.Bolt,
        _ => null,                                      // Jam / Unjam / Unknown: no graph state
    };

    /// <summary>
    /// Builds the graph text. Throws when there is no idle clip (the graph is built around it).
    /// <paramref name="cameraBone"/> is the engine bone name the editor preview camera locks to;
    /// <paramref name="previewModel"/> is the vmdl shown in the AnimGraph editor.
    /// <paramref name="fireCyclesAction"/>: when false, the bolt clip plays after every shot (bolt-action);
    /// when true (default) it only plays on b_pump, since a v3 "Bolt / Charge" clip is often a charging handle.
    /// </summary>
    public static string Generate(string weaponName, IReadOnlyList<GraphClip> clips, string? cameraBone, out GraphSummary summary,
        string? previewModel = null, bool fireCyclesAction = true)
    {
        if (clips is null)
            throw new ArgumentNullException(nameof(clips));

        // First clip per slot wins; a plain reload beats a tactical one.
        var bySlot = new Dictionary<Slot, GraphClip>();
        foreach (var clip in clips.OrderBy(c => c?.Role ==AnimationRole.TacticalReload ? 1 : 0))
        {
            if (clip is null || string.IsNullOrEmpty(clip.Sequence) || SlotFor(clip.Role) is not { } slot)
                continue;
            bySlot.TryAdd(slot, clip);
        }

        var graph = new AnimGraphBuilder(weaponName);
        var sm = new StateMachineBuilder(graph);
        var states = new List<string>();

        bool Has(Slot role) => bySlot.ContainsKey(role);
        GraphClip Seq(Slot role) => bySlot[role];

        if (!Has(Slot.Idle))
            throw new InvalidOperationException("The weapon has no idle animation to build the graph around.");

        // ---------------------------------------------------------------- tags
        var busy = graph.Tag("Busy", eventTag: false);
        var aiming = Has(Slot.AdsIdle) || Has(Slot.AdsIn) ? graph.Tag("Aiming", eventTag: false) : 0;
        var holstering = Has(Slot.Holster) ? graph.Tag("Holstering", eventTag: false) : 0;
        var discouraged = graph.Tag(WeaponParams.TagAttackDiscouraged, eventTag: true);
        var holsterFinished = Has(Slot.Holster) ? graph.Tag(WeaponParams.TagHolsterFinished, eventTag: true) : 0;
        var shellReload = Has(Slot.ReloadInsert);
        var reloadIncrement = shellReload ? graph.Tag(WeaponParams.TagReloadIncrement, eventTag: true) : 0;

        // ---------------------------------------------------------------- params
        var pAttack = Has(Slot.Fire) || Has(Slot.FireAds) || Has(Slot.Melee) && !Has(Slot.Fire)
            ? graph.BoolParam(WeaponParams.Attack, autoReset: true) : 0;
        var pDry = Has(Slot.DryFire) ? graph.BoolParam(WeaponParams.AttackDry, autoReset: true) : 0;
        var pEmpty = Has(Slot.ReloadEmpty) || Has(Slot.FireLast) ? graph.BoolParam(WeaponParams.Empty, autoReset: false) : 0;
        var hasReload = Has(Slot.Reload) || Has(Slot.ReloadEmpty) || shellReload;
        // Shell-by-shell reloads keep b_reload held (Facepunch shotgun convention); magazine reloads auto-reset.
        var pReload = hasReload ? graph.BoolParam(WeaponParams.Reload, autoReset: !shellReload) : 0;
        var pSpeedReload = hasReload ? graph.FloatParam(WeaponParams.SpeedReload, 0.1f, 4f, 1f) : 0;
        var pSkip = Has(Slot.Deploy) ? graph.BoolParam(WeaponParams.DeploySkip, autoReset: false) : 0;
        var pSpeedDeploy = Has(Slot.Deploy) ? graph.FloatParam(WeaponParams.SpeedDeploy, 0.1f, 4f, 1f) : 0;
        var pHolster = Has(Slot.Holster) ? graph.BoolParam(WeaponParams.Holster, autoReset: false) : 0;
        var pInspect = Has(Slot.Inspect) ? graph.BoolParam(WeaponParams.Inspect, autoReset: true) : 0;
        var pSprint = Has(Slot.Sprint) ? graph.BoolParam(WeaponParams.Sprint, autoReset: false) : 0;
        var pBob = Has(Slot.Walk) ? graph.FloatParam(WeaponParams.MoveBob, 0f, 1f, 0f) : 0;
        var hasAds = Has(Slot.AdsIdle) || Has(Slot.AdsIn);
        var pIron = hasAds ? graph.EnumParam(WeaponParams.Ironsights, new[] { "None", "Normal" }) : 0;
        var pSpeedIron = hasAds ? graph.FloatParam(WeaponParams.SpeedIronsights, 0.1f, 4f, 1f) : 0;
        var pMode = Has(Slot.FireMode) ? graph.BoolParam(WeaponParams.FireMode, autoReset: true) : 0;
        var pPump = Has(Slot.Pump) || Has(Slot.Bolt) ? graph.BoolParam(WeaponParams.Pump, autoReset: true) : 0;
        var pMelee = Has(Slot.Melee) && Has(Slot.Fire) ? graph.BoolParam(WeaponParams.Melee, autoReset: true) : 0;
        var pJump = Has(Slot.Jump) ? graph.BoolParam(WeaponParams.Jump, autoReset: true) : 0;

        // ---------------------------------------------------------------- nodes / states
        float y = 0;
        long Node(Slot role, long speedParam = 0, params (long Tag, float Start, float Duration)[] spans)
        {
            var sequence = Seq(role);
            var node = graph.Sequence(sequence.Sequence, sequence.Sequence, sequence.Loop, -600, y, 1f, spans);
            y += 96;
            return speedParam != 0 ? graph.SpeedScale(sequence.Sequence + " speed", node, speedParam, -380, y - 96) : node;
        }

        float column = 0;
        StateMachineBuilder.State State(string name, long input, bool start = false, params long[] tags)
        {
            var state = sm.Add(name, input, 200 + (column % 4) * 220, 80 + (float)Math.Floor(column / 4) * 140, start);
            column++;
            state.Tags.AddRange(tags.Where(t => t != 0));
            states.Add(name);
            return state;
        }

        var idle = State("Idle", Node(Slot.Idle), start: !Has(Slot.Deploy));
        var any = sm.Add("Any", null, 40, -80, start: false, alwaysEvaluate: true);

        StateMachineBuilder.State? deploy = null;
        if (Has(Slot.Deploy))
        {
            deploy = State("Deploy", Node(Slot.Deploy, pSpeedDeploy, (discouraged, 0f, 0.9f)), start: true, busy);
            sm.Transition(deploy, idle, Blend, false, StateMachineBuilder.Finished());
            sm.Transition(deploy, idle, QuickBlend, false, StateMachineBuilder.Bool(pSkip, true));
        }

        if (Has(Slot.Holster))
        {
            var holster = State("Holster", Node(Slot.Holster, 0, (holsterFinished, HolsterFinishCycle, 0.02f), (discouraged, 0f, 1f)), false, busy, holstering);
            sm.Transition(any, holster, Blend, true, StateMachineBuilder.Bool(pHolster, true), StateMachineBuilder.TagActive(holstering, false));
            sm.Transition(holster, deploy ?? idle, Blend, true, StateMachineBuilder.Bool(pHolster, false));
        }

        // Aiming first: while aimed, attack plays the aimed fire.
        StateMachineBuilder.State? adsIdle = null;
        if (hasAds)
        {
            var adsIdleRole = Has(Slot.AdsIdle) ? Slot.AdsIdle : Slot.AdsIn;
            adsIdle = State("Aim", Node(adsIdleRole == Slot.AdsIn ? Slot.AdsIn : Slot.AdsIdle), false, aiming);
            if (Has(Slot.AdsIn) && adsIdleRole == Slot.AdsIdle)
            {
                var aimIn = State("Aim In", Node(Slot.AdsIn, pSpeedIron), false, aiming);
                sm.Transition(idle, aimIn, QuickBlend, true, StateMachineBuilder.Enum(pIron, CompareOp.NotEqual, 0));
                sm.Transition(aimIn, adsIdle, QuickBlend, false, StateMachineBuilder.Finished());
                sm.Transition(aimIn, idle, Blend, false, StateMachineBuilder.Enum(pIron, CompareOp.Equal, 0));
            }
            else
            {
                sm.Transition(idle, adsIdle, Blend, true, StateMachineBuilder.Enum(pIron, CompareOp.NotEqual, 0));
            }
            if (Has(Slot.AdsOut))
            {
                var aimOut = State("Aim Out", Node(Slot.AdsOut, pSpeedIron));
                sm.Transition(adsIdle, aimOut, QuickBlend, true, StateMachineBuilder.Enum(pIron, CompareOp.Equal, 0));
                sm.Transition(aimOut, idle, Blend, false, StateMachineBuilder.Finished());
                sm.Transition(aimOut, adsIdle, QuickBlend, true, StateMachineBuilder.Enum(pIron, CompareOp.NotEqual, 0));
            }
            else
            {
                sm.Transition(adsIdle, idle, Blend, false, StateMachineBuilder.Enum(pIron, CompareOp.Equal, 0));
            }

            if (Has(Slot.FireAds) && pAttack != 0)
            {
                var fireAds = State("Fire Aimed", Node(Slot.FireAds), false, aiming);
                sm.Transition(any, fireAds, 0f, true, StateMachineBuilder.Bool(pAttack, true), StateMachineBuilder.TagActive(aiming, true), StateMachineBuilder.TagActive(busy, false));
                sm.Transition(fireAds, adsIdle, Blend, false, StateMachineBuilder.Finished());
            }
        }

        // Reloads before fire so a reload request wins over a held trigger.
        if (hasReload && !shellReload)
        {
            if (Has(Slot.ReloadEmpty))
            {
                var empty = State("Reload Empty", Node(Slot.ReloadEmpty, pSpeedReload, (discouraged, 0f, 0.9f)), false, busy);
                sm.Transition(any, empty, Blend, true, StateMachineBuilder.Bool(pReload, true), StateMachineBuilder.Bool(pEmpty, true), StateMachineBuilder.TagActive(busy, false));
                sm.Transition(empty, idle, Blend, false, StateMachineBuilder.Finished());
            }
            var reloadRole = Has(Slot.Reload) ? Slot.Reload : Slot.ReloadEmpty;
            var reload = State("Reload", Node(reloadRole, pSpeedReload, (discouraged, 0f, 0.9f)), false, busy);
            sm.Transition(any, reload, Blend, true, StateMachineBuilder.Bool(pReload, true), StateMachineBuilder.TagActive(busy, false));
            sm.Transition(reload, idle, Blend, false, StateMachineBuilder.Finished());
        }
        else if (shellReload)
        {
            var insertSeq = Seq(Slot.ReloadInsert);
            StateMachineBuilder.State? start = Has(Slot.ReloadStart)
                ? State("Reload Start", Node(Slot.ReloadStart, pSpeedReload), false, busy) : null;
            // Two insert states ping-pong so every shell restarts the insert animation.
            var insertA = State("Reload Insert A", graph.Sequence("reload_insert_a", insertSeq.Sequence, false, -600, y, 1f, new[] { (reloadIncrement, InsertCycle, 0.02f) }), false, busy);
            y += 96;
            var insertB = State("Reload Insert B", graph.Sequence("reload_insert_b", insertSeq.Sequence, false, -600, y, 1f, new[] { (reloadIncrement, InsertCycle, 0.02f) }), false, busy);
            y += 96;
            StateMachineBuilder.State? end = Has(Slot.ReloadEnd)
                ? State("Reload End", Node(Slot.ReloadEnd, pSpeedReload), false, busy) : null;

            var entry = start ?? insertA;
            sm.Transition(any, entry, Blend, true, StateMachineBuilder.Bool(pReload, true), StateMachineBuilder.TagActive(busy, false));
            if (start is not null)
            {
                sm.Transition(start, insertA, QuickBlend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pReload, true));
                sm.Transition(start, end ?? idle, Blend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pReload, false));
            }
            sm.Transition(insertA, insertB, QuickBlend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pReload, true));
            sm.Transition(insertB, insertA, QuickBlend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pReload, true));
            sm.Transition(insertA, end ?? idle, Blend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pReload, false));
            sm.Transition(insertB, end ?? idle, Blend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pReload, false));
            if (end is not null)
                sm.Transition(end, idle, Blend, false, StateMachineBuilder.Finished());
            if (Has(Slot.Reload) || Has(Slot.ReloadEmpty))
            {
                // A full-magazine style reload also exists: play it when the gun is empty.
                var fullRole = Has(Slot.ReloadEmpty) ? Slot.ReloadEmpty : Slot.Reload;
                if (pEmpty != 0)
                {
                    var full = State("Reload Full", Node(fullRole, pSpeedReload), false, busy);
                    sm.Transition(any, full, Blend, true, StateMachineBuilder.Bool(pReload, true), StateMachineBuilder.Bool(pEmpty, true), StateMachineBuilder.TagActive(busy, false));
                    sm.Transition(full, idle, Blend, false, StateMachineBuilder.Finished());
                }
            }
        }

        if (pDry != 0)
        {
            var dry = State("Dry Fire", Node(Slot.DryFire));
            sm.Transition(any, dry, 0f, true, StateMachineBuilder.Bool(pDry, true), StateMachineBuilder.TagActive(busy, false));
            sm.Transition(dry, idle, Blend, false, StateMachineBuilder.Finished());
        }

        if (Has(Slot.Fire) && pAttack != 0)
        {
            // Aimed without an aimed-fire clip: a hip-fire clip would drop the sights, so shots
            // keep the aimed pose (the viewmodel adds its own kick). Hip fire only when not aimed.
            var hipOnly = adsIdle is not null && !Has(Slot.FireAds);
            var fire = State("Fire", Node(Slot.Fire));
            if (Has(Slot.FireLast) && pEmpty != 0)
            {
                var last = State("Fire Last", Node(Slot.FireLast));
                if (hipOnly)
                    sm.Transition(any, last, 0f, true, StateMachineBuilder.Bool(pAttack, true), StateMachineBuilder.Bool(pEmpty, true), StateMachineBuilder.TagActive(busy, false), StateMachineBuilder.TagActive(aiming, false));
                else
                    sm.Transition(any, last, 0f, true, StateMachineBuilder.Bool(pAttack, true), StateMachineBuilder.Bool(pEmpty, true), StateMachineBuilder.TagActive(busy, false));
                sm.Transition(last, idle, Blend, false, StateMachineBuilder.Finished());
            }
            if (hipOnly)
                sm.Transition(any, fire, 0f, true, StateMachineBuilder.Bool(pAttack, true), StateMachineBuilder.TagActive(busy, false), StateMachineBuilder.TagActive(aiming, false));
            else
                sm.Transition(any, fire, 0f, true, StateMachineBuilder.Bool(pAttack, true), StateMachineBuilder.TagActive(busy, false));

            // Bolt/pump actions chain after the shot when the fire clip does not cycle the action itself.
            var actionRole = Has(Slot.Pump) ? Slot.Pump : Has(Slot.Bolt) ? Slot.Bolt : (Slot?)null;
            if (actionRole is { } role)
            {
                var action = State(role == Slot.Pump ? "Pump" : "Bolt", Node(role), false, busy);
                sm.Transition(any, action, Blend, true, StateMachineBuilder.Bool(pPump, true), StateMachineBuilder.TagActive(busy, false));
                sm.Transition(action, idle, Blend, false, StateMachineBuilder.Finished());
                if (!fireCyclesAction)
                {
                    if (pEmpty != 0)
                        sm.Transition(fire, action, QuickBlend, true, StateMachineBuilder.Finished(), StateMachineBuilder.Bool(pEmpty, false));
                    else
                        sm.Transition(fire, action, QuickBlend, true, StateMachineBuilder.Finished());
                }
            }
            if (adsIdle is not null)
                sm.Transition(fire, adsIdle, Blend, false, StateMachineBuilder.Finished(), StateMachineBuilder.Enum(pIron, CompareOp.NotEqual, 0));
            sm.Transition(fire, idle, Blend, false, StateMachineBuilder.Finished());
        }
        else if (Has(Slot.Melee) && pAttack != 0)
        {
            var swing = State("Melee", Node(Slot.Melee));
            sm.Transition(any, swing, 0f, true, StateMachineBuilder.Bool(pAttack, true), StateMachineBuilder.TagActive(busy, false));
            sm.Transition(swing, idle, Blend, false, StateMachineBuilder.Finished());
        }

        if (pMelee != 0)
        {
            var melee = State("Melee", Node(Slot.Melee), false, busy);
            sm.Transition(any, melee, QuickBlend, true, StateMachineBuilder.Bool(pMelee, true), StateMachineBuilder.TagActive(busy, false));
            sm.Transition(melee, idle, Blend, false, StateMachineBuilder.Finished());
        }

        if (pInspect != 0)
        {
            var inspect = State("Inspect", Node(Slot.Inspect), false, busy);
            sm.Transition(any, inspect, Blend, true, StateMachineBuilder.Bool(pInspect, true), StateMachineBuilder.TagActive(busy, false));
            sm.Transition(inspect, idle, Blend, false, StateMachineBuilder.Finished());
            // Inspecting is cancelled by any action.
            if (pAttack != 0) sm.Transition(inspect, idle, QuickBlend, false, StateMachineBuilder.Bool(pAttack, true));
        }

        if (pMode != 0)
        {
            var mode = State("Fire Mode", Node(Slot.FireMode), false, busy);
            sm.Transition(any, mode, Blend, true, StateMachineBuilder.Bool(pMode, true), StateMachineBuilder.TagActive(busy, false));
            sm.Transition(mode, idle, Blend, false, StateMachineBuilder.Finished());
        }

        if (pJump != 0)
        {
            var jump = State("Jump", Node(Slot.Jump));
            sm.Transition(idle, jump, QuickBlend, true, StateMachineBuilder.Bool(pJump, true));
            sm.Transition(jump, idle, Blend, false, StateMachineBuilder.Finished());
        }

        // Locomotion: walk on move_bob, sprint on b_sprint.
        StateMachineBuilder.State? walk = null;
        if (pBob != 0)
        {
            walk = State("Walk", Node(Slot.Walk));
            sm.Transition(idle, walk, LocomotionBlend, false, StateMachineBuilder.Float(pBob, CompareOp.Greater, 0.1f));
            sm.Transition(walk, idle, LocomotionBlend, false, StateMachineBuilder.Float(pBob, CompareOp.LessOrEqual, 0.1f));
            if (adsIdle is not null)
                sm.Transition(walk, idle, QuickBlend, false, StateMachineBuilder.Enum(pIron, CompareOp.NotEqual, 0));
        }
        if (pSprint != 0)
        {
            var sprint = State("Sprint", Node(Slot.Sprint), false, discouraged);
            sm.Transition(idle, sprint, LocomotionBlend, false, StateMachineBuilder.Bool(pSprint, true));
            if (walk is not null)
                sm.Transition(walk, sprint, LocomotionBlend, false, StateMachineBuilder.Bool(pSprint, true));
            if (adsIdle is not null)
                sm.Transition(adsIdle, sprint, LocomotionBlend, false, StateMachineBuilder.Bool(pSprint, true));
            sm.Transition(sprint, walk ?? idle, LocomotionBlend, false, StateMachineBuilder.Bool(pSprint, false));
        }

        var machine = graph.StateMachine("Weapon", sm, -100, 0);
        graph.Root(machine, 200, 0);

        summary = new GraphSummary(
            states,
            graph.Parameters.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            new[] { WeaponParams.TagAttackDiscouraged, WeaponParams.TagHolsterFinished, WeaponParams.TagReloadIncrement }
                .Where(t => t switch
                {
                    WeaponParams.TagHolsterFinished => holsterFinished != 0,
                    WeaponParams.TagReloadIncrement => reloadIncrement != 0,
                    _ => true,
                }).ToList());

        return graph.Serialize(previewModel is null ? Array.Empty<string>() : new[] { previewModel }, cameraBone);
    }
}
