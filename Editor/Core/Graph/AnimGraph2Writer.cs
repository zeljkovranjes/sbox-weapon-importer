#nullable enable annotations

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeaponImporter.EditorTools.Core.Graph;

/// <summary>
/// Writes a generated weapon graph (the document <see cref="WeaponGraphGenerator.GenerateDocument"/> builds) as an
/// AnimGraph2 <c>.ag</c> graph with the same parameters, states and transitions. AnimGraph2 has no state tags, so
/// tag conditions are settled per source state (a transition that needs a state not to be busy only leaves states
/// that aren't), and the "Any" state's transitions become transitions from every state they apply to, checked
/// before the state's own. Identifiers come from the model path and the build order, so the same weapon always
/// produces the same file.
/// </summary>
public static class AnimGraph2Writer
{
    private const string AnyStateName = "Any";
    private const string ResultOutput = "Result";
    private const string ParameterOutput = "Value";
    private const float NearEqualEpsilon = 0.0001f;

    /// <summary>
    /// The <c>.ag</c> JSON for <paramref name="graph"/>, animating <paramref name="model"/> (a vmdl) on
    /// <paramref name="skeleton"/> (a <c>.skel</c>). <paramref name="animations"/> maps each sequence the graph plays
    /// to its <c>.anim</c>. Throws when a sequence has no animation or the graph uses something AnimGraph2 can't express.
    /// </summary>
    public static string Write(GraphKvObject graph, string model, string skeleton, IReadOnlyDictionary<string, string> animations)
    {
        if (graph is null)
            throw new ArgumentNullException(nameof(graph));
        return new Conversion(graph, model, animations).Write(skeleton);
    }

    private sealed class Conversion
    {
        private readonly Dictionary<long, GraphKvObject> _nodes = new();
        private readonly Dictionary<long, (string Name, string Type, string[] Options)> _parameters = new();
        private readonly Dictionary<long, string> _parameterIds = new();
        private readonly IReadOnlyDictionary<string, string> _animations;
        private readonly GraphKvObject _graph;
        private readonly string _model;
        private int _nextId;

        public Conversion(GraphKvObject graph, string model, IReadOnlyDictionary<string, string> animations)
        {
            _graph = graph;
            _model = model ?? "";
            _animations = animations ?? throw new ArgumentNullException(nameof(animations));
            foreach (var entry in Array(Obj(graph, "m_nodeManager"), "m_nodes"))
                _nodes[Id(entry, "key")] = Obj(entry, "value");
        }

        public string Write(string skeleton)
        {
            var root = _nodes.Values.FirstOrDefault(n => Str(n, "_class") == "CRootAnimNode")
                ?? throw new InvalidOperationException("The weapon graph has no root node.");
            var machine = Input(root, "m_inputConnection");
            if (Str(machine, "_class") != "CStateMachineAnimNode")
                throw new InvalidOperationException($"The weapon graph's root plays a {Str(machine, "_class")}, not a state machine.");

            var document = new JsonObject
            {
                ["Version"] = 1,
                ["Model"] = _model,
                ["Skeleton"] = skeleton ?? "",
                ["parameters"] = Parameters(),
            };
            var stateMachine = StateMachine(machine);
            document["nodes"] = new JsonArray(PoseResult(Ref(stateMachine, ResultOutput), "320,0"), stateMachine);
            return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        // ------------------------------------------------------------------ parameters

        private JsonArray Parameters()
        {
            var list = new JsonArray();
            foreach (var parameter in Array(Obj(_graph, "m_pParameterList"), "m_Parameters"))
            {
                var name = Str(parameter, "m_name");
                var identifier = NewId();
                var json = new JsonObject { ["Identifier"] = identifier, ["Name"] = name };
                string type;
                string[] options = System.Array.Empty<string>();
                switch (Str(parameter, "_class"))
                {
                    case "CBoolAnimParameter":
                        type = "Bool";
                        json["RuntimeDefaultBool"] = Bool(parameter, "m_bDefaultValue");
                        break;
                    case "CFloatAnimParameter":
                        type = "Float";
                        json["RuntimeDefaultFloat"] = Number(parameter, "m_fDefaultValue");
                        json["Min"] = Number(parameter, "m_fMinValue");
                        json["Max"] = Number(parameter, "m_fMaxValue");
                        break;
                    case "CEnumAnimParameter":
                        // An AnimGraph1 enum is an ID parameter whose options keep the enum's order (game code sets it by index).
                        type = "ID";
                        options = Values(parameter, "m_enumOptions").Select(o => ((GraphKvString)o).Value).ToArray();
                        json["ExpectedValues"] = new JsonArray(options.Select(o => (JsonNode)o).ToArray());
                        json["RuntimeDefaultID"] = options.ElementAtOrDefault((int)Long(parameter, "m_defaultValue")) ?? "";
                        break;
                    default:
                        throw new InvalidOperationException($"Parameter '{name}' is a {Str(parameter, "_class")}, which the AnimGraph2 writer doesn't support.");
                }
                json["Type"] = type;
                var id = Id(parameter, "m_id");
                _parameters[id] = (name, type, options);
                _parameterIds[id] = identifier;
                list.Add(json);
            }
            return list;
        }

        // ------------------------------------------------------------------ state machine

        private JsonObject StateMachine(GraphKvObject machine)
        {
            var source = Array(machine, "m_states").ToList();
            var any = source.Where(s => Bool(s, "m_bAlwaysEvaluate") && NoInput(s)).ToList();
            var states = source.Except(any).ToList();
            if (states.Count == 0)
                throw new InvalidOperationException("The weapon graph's state machine has no states.");
            var names = states.ToDictionary(s => Id(s, "m_stateID"), s => Str(s, "m_name"));

            var stateList = new JsonArray();
            var conduits = new Dictionary<(string From, string To), JsonArray>();
            foreach (var state in states)
            {
                var name = Str(state, "m_name");
                var tags = Array(state, "m_tags").Select(t => ((GraphKvLong)t["m_id"]).Value).ToHashSet();
                var order = new JsonArray();
                // The "Any" state's transitions apply from every state and are checked first, as interrupts.
                var routes = any.SelectMany(a => Array(a, "m_transitions")).Concat(Array(state, "m_transitions"));
                foreach (var transition in routes)
                {
                    var target = names.GetValueOrDefault(Id(transition, "m_destState"))
                        ?? throw new InvalidOperationException($"A transition from '{name}' leads to a state the graph doesn't have.");
                    // Re-entering the same state only makes sense when the transition restarts it (repeated attacks).
                    if (target == name && !Bool(transition, "m_bReset"))
                        continue;
                    if (!AppliesFrom(transition, tags))
                        continue;
                    if (!conduits.TryGetValue((name, target), out var nodes))
                        conduits[(name, target)] = nodes = new JsonArray();
                    order.Add(Transition(nodes, transition, $"{name} to {target}"));
                }
                stateList.Add(State(state, name, order));
            }

            return Node("StateMachineNode", "0,0",
                ("States", stateList),
                ("Conduits", new JsonArray(conduits.Select(c => (JsonNode)new JsonObject
                {
                    ["From"] = c.Key.From,
                    ["To"] = c.Key.To,
                    ["Graph"] = SubGraph(c.Value, "TransitionConduit"),
                }).ToArray())),
                ("DefaultState", Str(states.FirstOrDefault(s => Bool(s, "m_bIsStartState")) ?? states[0], "m_name")));
        }

        private JsonObject State(GraphKvObject state, string name, JsonArray transitionOrder)
        {
            var nodes = new JsonArray();
            var pose = Pose(nodes, Input(state, "m_inputConnection"));
            nodes.Insert(0, PoseResult(pose, "640,0"));
            var position = Values(state, "m_position").Select(v => ((GraphKvDouble)v).Value).ToArray();
            return new JsonObject
            {
                ["Name"] = name,
                ["Type"] = "BlendTree",
                ["Events"] = new JsonArray(),
                ["TimedEvents"] = new JsonArray(),
                ["TransitionOrder"] = transitionOrder,
                ["Position"] = Position(position[0], position[1]),
                ["Graph"] = SubGraph(nodes, null),
            };
        }

        // The pose a state plays: a clip, optionally sped up by a float parameter.
        private JsonObject Pose(JsonArray nodes, GraphKvObject node)
        {
            switch (Str(node, "_class"))
            {
                case "CSequenceAnimNode":
                    var sequence = Str(node, "m_sequenceName");
                    if (!_animations.TryGetValue(sequence, out var animation))
                        throw new InvalidOperationException($"Sequence '{sequence}' has no AnimGraph2 animation (.anim).");
                    var clip = Node("AnimationClipNode", "0,0",
                        ("Loop", Bool(node, "m_bLoop")),
                        ("SampleRootMotion", false),
                        ("DefaultVariationData", new JsonObject { ["AnimationName"] = animation, ["SpeedMultiplier"] = Number(node, "m_playbackSpeed"), ["StartSyncEventOffset"] = 0 }),
                        ("Overrides", new JsonArray()));
                    nodes.Add(clip);
                    return Ref(clip, ResultOutput);
                case "CSpeedScaleAnimNode":
                    var source = Pose(nodes, Input(node, "m_inputConnection"));
                    var speed = Parameter(nodes, Id(node, "m_param"), "0,130");
                    var scale = Node("SpeedScaleNode", "320,0", ("Source", source), ("Speed", speed), ("DefaultSpeed", 1));
                    nodes.Add(scale);
                    return Ref(scale, ResultOutput);
                default:
                    throw new InvalidOperationException($"State node '{Str(node, "m_sName")}' is a {Str(node, "_class")}, which the AnimGraph2 writer doesn't support.");
            }
        }

        // ------------------------------------------------------------------ transitions

        // Whether a transition's tag conditions hold for a state carrying tags.
        private static bool AppliesFrom(GraphKvObject transition, HashSet<long> tags)
            => Array(transition, "m_conditions").All(c => Str(c, "_class") != "CTagCondition" || tags.Contains(Id(c, "m_tagID")) == Bool(c, "m_comparisonValue"));

        // Adds a transition node (with its conditions) to a conduit graph and returns its identifier.
        private string Transition(JsonArray nodes, GraphKvObject transition, string name)
        {
            var row = nodes.Count;
            var conditions = Array(transition, "m_conditions")
                .Where(c => Str(c, "_class") != "CTagCondition")
                .Select((c, i) => Condition(nodes, c, $"0,{(row + i) * 130}"))
                .ToList();
            var condition = conditions.Count switch
            {
                0 => Add(nodes, Node("ConstBoolNode", $"320,{row * 130}", ("Value", true)), ResultOutput),
                1 => conditions[0],
                _ => Add(nodes, Node("AndNode", $"640,{row * 130}",
                    ("Conditions", new JsonArray(conditions.Select(c => (JsonNode)new JsonObject { ["Input"] = c }).ToArray()))), ResultOutput),
            };
            var node = Node("TransitionNode", $"960,{row * 130}",
                ("Condition", condition),
                ("TransitionName", name),
                ("Duration", Number(transition, "m_blendDuration")),
                ("TimeMatch", "None"),
                ("BlendWeightEasing", "Linear"),
                ("RootMotionBlend", "Blend"),
                // AnimGraph1 takes a transition mid-blend too: a trigger during a blend must not be lost.
                ("CanBeForced", true));
            nodes.Add(node);
            return (string)node["Identifier"]!;
        }

        private JsonObject Condition(JsonArray nodes, GraphKvObject condition, string position)
        {
            switch (Str(condition, "_class"))
            {
                case "CFinishedCondition":
                    var atEnd = Str(condition, "m_option") == "FinishedConditionOption_OnFinished";
                    return Add(nodes, Node("StateCompletedConditionNode", position, ("AtClipEnd", atEnd)), ResultOutput);
                case "CParameterAnimCondition":
                    return Compare(nodes, condition, position);
                default:
                    throw new InvalidOperationException($"Condition {Str(condition, "_class")} isn't supported by the AnimGraph2 writer.");
            }
        }

        private JsonObject Compare(JsonArray nodes, GraphKvObject condition, string position)
        {
            var id = Id(condition, "m_paramID");
            var op = (CompareOp)Long(condition, "m_comparisonOp");
            var value = Obj(condition, "m_comparisonValue")["m_data"];
            var input = Parameter(nodes, id, position);
            var (name, type, options) = _parameters[id];
            if (type == "ID" && op is not (CompareOp.Equal or CompareOp.NotEqual))
                throw new InvalidOperationException($"Enum parameter '{name}' is compared with {op}; AnimGraph2 IDs only match or don't.");
            return type switch
            {
                "Bool" => ((GraphKvBool)value).Value == (op == CompareOp.Equal) ? input : Not(nodes, input, position),
                "ID" => Add(nodes, Node("IDComparisonNode", NextColumn(position), ("Id", input),
                    ("Comparison", op == CompareOp.NotEqual ? "DoesntMatch" : "Matches"),
                    ("Ids", new JsonArray((JsonNode)options[(int)((GraphKvLong)value).Value]))), ResultOutput),
                _ => FloatCompare(nodes, input, op, ((GraphKvDouble)value).Value, position),
            };
        }

        private JsonObject FloatCompare(JsonArray nodes, JsonObject input, CompareOp op, double value, string position)
        {
            var comparison = op switch
            {
                CompareOp.Greater => "GreaterThan",
                CompareOp.GreaterOrEqual => "GreaterThanEqual",
                CompareOp.Less => "LessThan",
                CompareOp.LessOrEqual => "LessThanEqual",
                _ => "NearEqual",
            };
            var compare = Add(nodes, Node("FloatComparisonNode", NextColumn(position), ("Value", input), ("Comparison", comparison),
                ("ComparisonValue", value), ("Epsilon", NearEqualEpsilon)), ResultOutput);
            return op == CompareOp.NotEqual ? Not(nodes, compare, NextColumn(position)) : compare;
        }

        private JsonObject Not(JsonArray nodes, JsonObject input, string position)
            => Add(nodes, Node("NotNode", NextColumn(position), ("Value", input)), ResultOutput);

        // A node reading a parameter, added to a graph.
        private JsonObject Parameter(JsonArray nodes, long id, string position)
        {
            if (!_parameters.TryGetValue(id, out var parameter))
                throw new InvalidOperationException($"The weapon graph reads parameter {id}, which it doesn't define.");
            return Add(nodes, Node($"{parameter.Type}ParameterNode", position,
                ("ParameterIdentifier", _parameterIds[id]), ("ParameterName", parameter.Name)), ParameterOutput);
        }

        // ------------------------------------------------------------------ JSON helpers

        private JsonObject PoseResult(JsonObject pose, string position) => Node("PoseResultNode", position, ("Pose", pose));

        private JsonObject Node(string type, string position, params (string Key, JsonNode? Value)[] properties)
        {
            var node = new JsonObject { ["_class"] = type };
            foreach (var (key, value) in properties)
                node[key] = value;
            node["Identifier"] = NewId();
            node["Position"] = position;
            node["HandleOffsets"] = new JsonObject();
            return node;
        }

        private static JsonObject Add(JsonArray nodes, JsonObject node, string output)
        {
            nodes.Add(node);
            return Ref(node, output);
        }

        private static JsonObject Ref(JsonObject node, string output)
            => new() { ["Identifier"] = (string)node["Identifier"]!, ["Output"] = output };

        private static JsonObject SubGraph(JsonArray nodes, string? kind)
        {
            var graph = new JsonObject { ["Version"] = 1, ["nodes"] = nodes };
            if (kind is not null)
                graph["Kind"] = kind;
            return graph;
        }

        // The same row, one column right: where a node reading the node at position goes.
        private static string NextColumn(string position)
        {
            var comma = position.IndexOf(',');
            return $"{int.Parse(position[..comma], CultureInfo.InvariantCulture) + 240},{position[(comma + 1)..]}";
        }

        private static string Position(double x, double y)
            => string.Create(CultureInfo.InvariantCulture, $"{x},{y}");

        // Stable identifiers: the same graph for the same model always gets the same ones.
        private string NewId()
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{_model}/{_nextId++}"));
            return new Guid(hash.AsSpan(0, 16)).ToString();
        }

        // ------------------------------------------------------------------ AnimGraph1 document readers

        private GraphKvObject Input(GraphKvObject node, string key)
        {
            var id = Id(Obj(node, key), "m_nodeID");
            return _nodes.TryGetValue(id, out var input) ? input : throw new InvalidOperationException($"Node '{Str(node, "m_sName")}' plays node {id}, which the graph doesn't have.");
        }

        private static bool NoInput(GraphKvObject state) => Id(Obj(state, "m_inputConnection"), "m_nodeID") == AnimGraphBuilder.NoOutput;

        private static GraphKvObject Obj(GraphKvObject o, string key) => (GraphKvObject)o[key];
        private static IEnumerable<GraphKvObject> Array(GraphKvObject o, string key) => ((GraphKvArray)o[key]).Items.Cast<GraphKvObject>();
        private static IEnumerable<GraphKvValue> Values(GraphKvObject o, string key) => ((GraphKvArray)o[key]).Items;
        private static string Str(GraphKvObject o, string key) => ((GraphKvString)o[key]).Value;
        private static bool Bool(GraphKvObject o, string key) => ((GraphKvBool)o[key]).Value;
        private static long Long(GraphKvObject o, string key) => ((GraphKvLong)o[key]).Value;
        private static double Number(GraphKvObject o, string key) => ((GraphKvDouble)o[key]).Value;
        private static long Id(GraphKvObject o, string key) => ((GraphKvLong)Obj(o, key)["m_id"]).Value;
    }
}
