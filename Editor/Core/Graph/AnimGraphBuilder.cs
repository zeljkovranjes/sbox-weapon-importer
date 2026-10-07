#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Graph;

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
