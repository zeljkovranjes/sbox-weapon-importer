#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Graph;

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
