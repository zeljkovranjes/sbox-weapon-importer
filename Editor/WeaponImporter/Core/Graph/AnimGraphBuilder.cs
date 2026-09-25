#nullable enable annotations

namespace WeaponImporter.Core.Graph;

/// <summary>Parameter value types used in animgraph conditions.</summary>
public enum ParamType
{
    Bool = 1,
    Enum = 2,
    Float = 4,
}

/// <summary>Comparison operators of <c>CParameterAnimCondition</c> / <c>CTimeCondition</c>.</summary>
public enum CompareOp
{
    Equal = 0,
    NotEqual = 1,
    Greater = 2,
    GreaterOrEqual = 3,
    Less = 4,
    LessOrEqual = 5,
}

/// <summary>
/// Low-level builder for animgraph2 KV3 documents. IDs are derived from stable names so the same
/// weapon always produces byte-identical graphs (deterministic reimports).
/// </summary>
public sealed class AnimGraphBuilder
{
    public const string Header =
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:animgraph2:version{0f7898b8-5471-45c4-9867-cd9c46bcfdb5} -->";

    public const long NoOutput = 4294967295L;

    private readonly GraphKvArray _nodes = new();
    private readonly GraphKvArray _parameters = new();
    private readonly GraphKvArray _tags = new();
    private readonly HashSet<long> _usedIds = new();
    private readonly Dictionary<string, long> _paramIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _tagIds = new(StringComparer.Ordinal);
    private readonly string _salt;

    public AnimGraphBuilder(string salt) => _salt = salt;

    /// <summary>Stable 31-bit id from a name (FNV-1a), unique within this graph.</summary>
    public long IdFor(string name)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in _salt + "/" + name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            long id = (hash & 0x7FFFFFFF) | 0x10000000;
            while (!_usedIds.Add(id))
                id = (id * 31 + 7) & 0x7FFFFFFF;
            return id;
        }
    }

    public IReadOnlyDictionary<string, long> Parameters => _paramIds;

    public long BoolParam(string name, bool autoReset, bool defaultValue = false)
    {
        if (_paramIds.TryGetValue(name, out var existing))
            return existing;
        var id = IdFor("param:" + name);
        _paramIds[name] = id;
        _parameters.Items.Add(GraphKv.Obj(
            ("_class", "CBoolAnimParameter"),
            ("m_name", name),
            ("m_id", GraphKv.Id(id)),
            ("m_previewButton", "ANIMPARAM_BUTTON_NONE"),
            ("m_bUseMostRecentValue", false),
            ("m_bAutoReset", autoReset),
            ("m_bDefaultValue", defaultValue)));
        return id;
    }

    public long FloatParam(string name, float min, float max, float defaultValue = 0f)
    {
        if (_paramIds.TryGetValue(name, out var existing))
            return existing;
        var id = IdFor("param:" + name);
        _paramIds[name] = id;
        _parameters.Items.Add(GraphKv.Obj(
            ("_class", "CFloatAnimParameter"),
            ("m_name", name),
            ("m_id", GraphKv.Id(id)),
            ("m_previewButton", "ANIMPARAM_BUTTON_NONE"),
            ("m_bUseMostRecentValue", false),
            ("m_bAutoReset", false),
            ("m_fDefaultValue", (double)defaultValue),
            ("m_fMinValue", (double)min),
            ("m_fMaxValue", (double)max)));
        return id;
    }

    public long EnumParam(string name, IReadOnlyList<string> options, int defaultValue = 0)
    {
        if (_paramIds.TryGetValue(name, out var existing))
            return existing;
        var id = IdFor("param:" + name);
        _paramIds[name] = id;
        _parameters.Items.Add(GraphKv.Obj(
            ("_class", "CEnumAnimParameter"),
            ("m_name", name),
            ("m_id", GraphKv.Id(id)),
            ("m_previewButton", "ANIMPARAM_BUTTON_NONE"),
            ("m_bUseMostRecentValue", false),
            ("m_bAutoReset", false),
            ("m_defaultValue", defaultValue),
            ("m_enumOptions", GraphKv.Arr(options.Cast<object?>()))));
        return id;
    }

    /// <summary>An event tag (dispatched to <c>OnAnimTagEvent</c>) or a string tag (state marker).</summary>
    public long Tag(string name, bool eventTag)
    {
        if (_tagIds.TryGetValue(name, out var existing))
            return existing;
        var id = IdFor("tag:" + name);
        _tagIds[name] = id;
        _tags.Items.Add(GraphKv.Obj(
            ("_class", eventTag ? "CEventAnimTag" : "CStringAnimTag"),
            ("m_name", name),
            ("m_tagID", GraphKv.Id(id))));
        return id;
    }

    public static GraphKvObject Connection(long nodeId) => GraphKv.Obj(("m_nodeID", GraphKv.Id(nodeId)), ("m_outputID", GraphKv.Id(NoOutput)));

    public static GraphKvObject NoConnection() => Connection(NoOutput);

    private GraphKvObject AddNode(long id, GraphKvObject value)
    {
        _nodes.Items.Add(GraphKv.Obj(("key", GraphKv.Id(id)), ("value", value)));
        return value;
    }

    private static GraphKvObject NodeBase(string cls, string name, long id, float x, float y)
        => GraphKv.Obj(
            ("_class", cls),
            ("m_sName", name),
            ("m_vecPosition", GraphKv.Arr((double)x, (double)y)),
            ("m_nNodeID", GraphKv.Id(id)),
            ("m_sNote", ""));

    /// <summary>A sequence player node; tag spans are (tag id, start cycle, duration cycle).</summary>
    public long Sequence(string label, string sequence, bool loop, float x, float y, float playbackSpeed = 1f,
        IEnumerable<(long Tag, float Start, float Duration)>? tagSpans = null)
    {
        var id = IdFor("seq:" + label);
        var node = NodeBase("CSequenceAnimNode", label, id, x, y);
        var spans = new GraphKvArray();
        foreach (var (tag, start, duration) in tagSpans ?? Enumerable.Empty<(long, float, float)>())
        {
            spans.Items.Add(GraphKv.Obj(
                ("_class", "CAnimTagSpan"),
                ("m_id", GraphKv.Id(tag)),
                ("m_fStartCycle", Math.Round((double)Math.Clamp(start, 0f, 1f), 6)),
                ("m_fDuration", Math.Round((double)Math.Clamp(duration, 0f, 1f), 6))));
        }
        node["m_tagSpans"] = spans;
        node["m_sequenceName"] = new GraphKvString(sequence);
        node["m_playbackSpeed"] = new GraphKvDouble(playbackSpeed);
        node["m_bLoop"] = new GraphKvBool(loop);
        AddNode(id, node);
        return id;
    }

    /// <summary>Scales a child node's playback speed by a float parameter.</summary>
    public long SpeedScale(string label, long input, long speedParam, float x, float y)
    {
        var id = IdFor("speed:" + label);
        var node = NodeBase("CSpeedScaleAnimNode", label, id, x, y);
        node["m_inputConnection"] = Connection(input);
        node["m_param"] = GraphKv.Id(speedParam);
        AddNode(id, node);
        return id;
    }

    public long Root(long input, float x, float y)
    {
        var id = IdFor("root");
        var node = NodeBase("CRootAnimNode", "Unnamed", id, x, y);
        node["m_inputConnection"] = Connection(input);
        AddNode(id, node);
        return id;
    }

    /// <summary>Adds a state machine node built by <paramref name="machine"/>.</summary>
    public long StateMachine(string label, StateMachineBuilder machine, float x, float y)
    {
        var id = IdFor("sm:" + label);
        var node = NodeBase("CStateMachineAnimNode", label, id, x, y);
        node["m_states"] = machine.Build();
        node["m_bBlockWaningTags"] = new GraphKvBool(false);
        node["m_bLockStateWhenWaning"] = new GraphKvBool(false);
        AddNode(id, node);
        return id;
    }

    public string Serialize(IEnumerable<string> previewModels, string? cameraBone)
    {
        var root = GraphKv.Obj(
            ("_class", "CAnimationGraph"),
            ("m_nodeManager", GraphKv.Obj(("_class", "CAnimNodeManager"), ("m_nodes", _nodes))),
            ("m_pParameterList", GraphKv.Obj(("_class", "CAnimParameterList"), ("m_Parameters", _parameters))),
            ("m_pTagManager", GraphKv.Obj(("_class", "CAnimTagManager"), ("m_tags", _tags))),
            ("m_pMovementManager", GraphKv.Obj(
                ("_class", "CAnimMovementManager"),
                ("m_MotorList", GraphKv.Obj(("_class", "CAnimMotorList"), ("m_motors", new GraphKvArray()))),
                ("m_MovementSettings", GraphKv.Obj(("_class", "CAnimMovementSettings"), ("m_bShouldCalculateSlope", false))))),
            ("m_pSettingsManager", GraphKv.Obj(
                ("_class", "CAnimGraphSettingsManager"),
                ("m_settingsGroups", GraphKv.Arr(
                    GraphKv.Obj(("_class", "CAnimGraphGeneralSettings"), ("m_iGridSnap", 16)),
                    GraphKv.Obj(("_class", "CAnimGraphNetworkSettings")))))),
            ("m_pActivityValuesList", GraphKv.Obj(("_class", "CActivityValueList"), ("m_activities", new GraphKvArray()))),
            ("m_previewModels", GraphKv.Arr(previewModels.Cast<object?>())),
            ("m_boneMergeModels", new GraphKvArray()),
            ("m_cameraSettings", GraphKv.Obj(
                ("m_flFov", 70.0),
                ("m_sLockBoneName", cameraBone ?? ""),
                ("m_bLockCamera", cameraBone is not null),
                ("m_bViewModelCamera", false))));
        return GraphKv.Serialize(Header, root);
    }
}

/// <summary>Builds the states and transitions of one state machine node.</summary>
public sealed class StateMachineBuilder
{
    private readonly AnimGraphBuilder _graph;
    private readonly List<State> _states = new();

    public StateMachineBuilder(AnimGraphBuilder graph) => _graph = graph;

    public sealed class State
    {
        public required string Name { get; init; }
        public required long Id { get; init; }
        public long? Input { get; init; }
        public bool Start { get; set; }
        public bool AlwaysEvaluate { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public List<GraphKvObject> Transitions { get; } = new();
        public List<long> Tags { get; } = new();
    }

    public IReadOnlyList<State> States => _states;

    public State Add(string name, long? input, float x, float y, bool start = false, bool alwaysEvaluate = false)
    {
        var state = new State
        {
            Name = name,
            Id = _graph.IdFor("state:" + name),
            Input = input,
            Start = start,
            AlwaysEvaluate = alwaysEvaluate,
            X = x,
            Y = y,
        };
        _states.Add(state);
        return state;
    }

    public State? Find(string name) => _states.FirstOrDefault(s => s.Name == name);

    /// <summary>Adds a transition; conditions are ANDed.</summary>
    public void Transition(State from, State to, float blend, bool reset, params GraphKvObject[] conditions)
    {
        from.Transitions.Add(GraphKv.Obj(
            ("_class", "CAnimStateTransition"),
            ("m_conditions", GraphKv.Arr(conditions.Cast<object?>())),
            ("m_blendDuration", Math.Round((double)blend, 4)),
            ("m_destState", GraphKv.Id(to.Id)),
            ("m_bReset", reset),
            ("m_resetCycleOption", "Beginning"),
            ("m_flFixedCycleValue", 0.0),
            ("m_bBlendCycle", false),
            ("m_blendCurve", GraphKv.Obj(("m_vControlPoint1", GraphKv.Arr(0.5, 0.0)), ("m_vControlPoint2", GraphKv.Arr(0.5, 1.0)))),
            ("m_bForceFootPlant", false),
            ("m_bDisabled", false),
            ("m_bRandomTimeBetween", false),
            ("m_flRandomTimeStart", 0.0),
            ("m_flRandomTimeEnd", 0.0)));
    }

    public static GraphKvObject Bool(long param, bool value)
        => GraphKv.Obj(
            ("_class", "CParameterAnimCondition"),
            ("m_comparisonOp", (int)CompareOp.Equal),
            ("m_paramID", GraphKv.Id(param)),
            ("m_comparisonValue", GraphKv.Obj(("m_nType", (int)ParamType.Bool), ("m_data", value))));

    public static GraphKvObject Enum(long param, CompareOp op, int value)
        => GraphKv.Obj(
            ("_class", "CParameterAnimCondition"),
            ("m_comparisonOp", (int)op),
            ("m_paramID", GraphKv.Id(param)),
            ("m_comparisonValue", GraphKv.Obj(("m_nType", (int)ParamType.Enum), ("m_data", value))));

    public static GraphKvObject Float(long param, CompareOp op, float value)
        => GraphKv.Obj(
            ("_class", "CParameterAnimCondition"),
            ("m_comparisonOp", (int)op),
            ("m_paramID", GraphKv.Id(param)),
            ("m_comparisonValue", GraphKv.Obj(("m_nType", (int)ParamType.Float), ("m_data", Math.Round((double)value, 4)))));

    public static GraphKvObject Finished(bool almost = true)
        => GraphKv.Obj(
            ("_class", "CFinishedCondition"),
            ("m_comparisonOp", 0),
            ("m_option", almost ? "FinishedConditionOption_OnAlmostFinished" : "FinishedConditionOption_OnFinished"),
            ("m_bIsFinished", true));

    /// <summary>Time spent in the current state compared against seconds.</summary>
    public static GraphKvObject Time(float seconds)
        => GraphKv.Obj(("_class", "CTimeCondition"), ("m_comparisonOp", (int)CompareOp.GreaterOrEqual), ("m_comparisonValue", Math.Round((double)seconds, 4)));

    public static GraphKvObject TagActive(long tag, bool active)
        => GraphKv.Obj(("_class", "CTagCondition"), ("m_comparisonOp", 0), ("m_tagID", GraphKv.Id(tag)), ("m_comparisonValue", active));

    internal GraphKvArray Build()
    {
        var states = new GraphKvArray();
        foreach (var state in _states)
        {
            states.Items.Add(GraphKv.Obj(
                ("_class", "CAnimState"),
                ("m_transitions", GraphKv.Arr(state.Transitions.Cast<object?>())),
                ("m_tags", GraphKv.Arr(state.Tags.Select(t => (object?)GraphKv.Id(t)))),
                ("m_tagBehaviors", GraphKv.Arr(state.Tags.Select(_ => (object?)0))),
                ("m_name", state.Name),
                ("m_inputConnection", state.Input is { } input ? AnimGraphBuilder.Connection(input) : AnimGraphBuilder.NoConnection()),
                ("m_stateID", GraphKv.Id(state.Id)),
                ("m_position", GraphKv.Arr((double)state.X, (double)state.Y)),
                ("m_bIsStartState", state.Start),
                ("m_bIsEndtState", false),
                ("m_bIsPassthrough", false),
                ("m_bIsRootMotionExclusive", false),
                ("m_bAlwaysEvaluate", state.AlwaysEvaluate)));
        }
        return states;
    }
}
