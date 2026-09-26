#nullable enable annotations

namespace WeaponImporter.Core.Formats.Fbx;

/// <summary>
/// Upgrades an FBX 6.x document (FBX 2006-2010, e.g. Blender 2.7x's exporter) to the FBX 7
/// layout the rest of the importer reads. FBX 6 links objects by name ("Model::bone") instead
/// of ids, keeps each mesh inside its model, writes properties as <c>Property</c> entries in a
/// <c>Properties60</c> block, and stores animation as <c>Takes</c> with per-channel key lists.
/// The upgrade gives every object an id, moves mesh data into a Geometry object, rewrites
/// properties as <c>P</c> entries and turns each take into an AnimationStack with a layer,
/// curve nodes and curves, wired with the same connections an FBX 7 file has.
/// </summary>
public static class FbxLegacy
{
    /// <summary>FBX version the document declares (FBXHeaderExtension/FBXVersion), 0 when absent.</summary>
    public static int Version(FbxNode root)
    {
        var v = root.Child("FBXHeaderExtension")?.Child("FBXVersion");
        if (v is null || v.Properties.Count == 0)
            return 0;
        try
        {
            return v.Prop<int>(0);
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    public static bool IsLegacy(FbxNode root) => Version(root) is > 0 and < 7000;

    /// <summary>The document in FBX 7 layout (a new tree; <paramref name="root"/> is not changed).</summary>
    public static FbxNode Upgrade(FbxNode root)
    {
        var objects6 = root.Child("Objects") ?? new FbxNode("Objects");
        var result = new FbxNode("(root)");
        foreach (var header in root.Children.Where(c => c.Name is "FBXHeaderExtension" or "Creator" or "CreationTime"))
            result.Children.Add(header);

        var objects = new FbxNode("Objects");
        var connections = new FbxNode("Connections");
        long nextId = 1_000_000;
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);    // "Model::bone" -> id
        var geometryOf = new Dictionary<long, long>();                       // model id -> geometry id
        var kinds = new Dictionary<long, string>();                          // id -> node type

        long IdOf(string full)
        {
            if (!ids.TryGetValue(full, out var id))
                ids[full] = id = nextId++;
            return id;
        }

        void Connect(string kind, long child, long parent, string? property = null)
        {
            var c = new FbxNode("C");
            c.Properties.Add(kind);
            c.Properties.Add(child);
            c.Properties.Add(parent);
            if (property is not null)
                c.Properties.Add(property);
            connections.Children.Add(c);
        }

        // Global settings (FBX 6 keeps them inside Objects).
        if (objects6.Child("GlobalSettings") is { } globals)
            result.Children.Add(WithProperties70(new FbxNode("GlobalSettings"), globals));

        foreach (var node in objects6.Children)
        {
            if (node.Name is "GlobalSettings" || node.Properties.Count == 0 || node.Properties[0] is not string full)
                continue;
            var subClass = node.Properties.Count > 1 && node.Properties[1] is string s ? s : "";
            var id = IdOf(full);
            switch (node.Name)
            {
                case "Model":
                {
                    var model = NewObject("Model", id, full, subClass == "Limb" ? "LimbNode" : subClass);
                    WithProperties70(model, node);
                    // FBX 6 cameras aim with points (in the scene's space) instead of their axes.
                    foreach (var aim in node.Children.Where(c => c.Name is "Position" or "LookAt" or "Up"))
                        model.Children.Add(aim);
                    kinds[id] = "Model";
                    if (node.Child("Vertices") is not null)
                    {
                        var geometryId = nextId++;
                        var geometry = NewObject("Geometry", geometryId, "Geometry::" + NameOf(full), "Mesh");
                        foreach (var child in node.Children)
                            if (IsGeometryChild(child.Name))
                                geometry.Children.Add(child);
                        objects.Children.Add(geometry);
                        geometryOf[id] = geometryId;
                        kinds[geometryId] = "Geometry";
                        Connect("OO", geometryId, id);
                    }
                    objects.Children.Add(model);
                    break;
                }
                case "Deformer":
                {
                    var deformer = NewObject("Deformer", id, full, subClass);
                    foreach (var child in node.Children)
                        if (child.Name is not ("Properties60" or "Properties70"))
                            deformer.Children.Add(child);
                    objects.Children.Add(deformer);
                    kinds[id] = "Deformer";
                    break;
                }
                case "Pose":
                {
                    var pose = NewObject("Pose", id, full, subClass);
                    foreach (var child in node.Children)
                    {
                        if (child.Name != "PoseNode")
                        {
                            pose.Children.Add(child);
                            continue;
                        }
                        var poseNode = new FbxNode("PoseNode");
                        foreach (var entry in child.Children)
                        {
                            if (entry.Name == "Node" && entry.Properties.Count > 0 && entry.Properties[0] is string target)
                            {
                                var n = new FbxNode("Node");
                                n.Properties.Add(IdOf(target));
                                poseNode.Children.Add(n);
                            }
                            else
                            {
                                poseNode.Children.Add(entry);
                            }
                        }
                        pose.Children.Add(poseNode);
                    }
                    objects.Children.Add(pose);
                    break;
                }
                case "Material" or "Texture" or "Video":
                {
                    var obj = NewObject(node.Name, id, full, subClass);
                    WithProperties70(obj, node);
                    foreach (var child in node.Children)
                        if (child.Name is not ("Properties60" or "Properties70"))
                            obj.Children.Add(child);
                    objects.Children.Add(obj);
                    kinds[id] = node.Name;
                    break;
                }
            }
        }

        // Connections by name.
        foreach (var c in root.Child("Connections")?.ChildrenNamed("Connect") ?? Enumerable.Empty<FbxNode>())
        {
            if (c.Properties.Count < 3 || c.Properties[0] is not string kind || c.Properties[1] is not string child || c.Properties[2] is not string parent)
                continue;
            if (!ids.TryGetValue(child, out var childId))
                continue;
            var parentId = parent is "Model::Scene" || !ids.TryGetValue(parent, out var p) ? 0L : p;
            kinds.TryGetValue(childId, out var childKind);
            kinds.TryGetValue(parentId, out var parentKind);
            // A skin deforms the mesh's geometry, which FBX 6 kept inside the model.
            if (childKind == "Deformer" && parentKind == "Model" && geometryOf.TryGetValue(parentId, out var geometryId))
                parentId = geometryId;
            // FBX 6 links a texture to its material without naming the slot: it is the colour map.
            if (childKind == "Texture" && parentKind == "Material" && kind == "OO")
            {
                Connect("OP", childId, parentId, "DiffuseColor");
                continue;
            }
            if (kind == "OP" && c.Properties.Count > 3 && c.Properties[3] is string property)
                Connect("OP", childId, parentId, property);
            else
                Connect("OO", childId, parentId);
        }

        // Takes -> animation stacks.
        foreach (var take in root.Child("Takes")?.ChildrenNamed("Take") ?? Enumerable.Empty<FbxNode>())
        {
            var takeName = take.Properties.Count > 0 && take.Properties[0] is string tn ? tn : "Take";
            var stackId = nextId++;
            var stack = NewObject("AnimationStack", stackId, "AnimStack::" + takeName, "");
            var props = new FbxNode("Properties70");
            if (take.Child("LocalTime") is { Properties.Count: >= 2 } local)
            {
                props.Children.Add(P("LocalStart", "KTime", "Time", "", local.Prop<long>(0)));
                props.Children.Add(P("LocalStop", "KTime", "Time", "", local.Prop<long>(1)));
            }
            stack.Children.Add(props);
            objects.Children.Add(stack);
            var layerId = nextId++;
            objects.Children.Add(NewObject("AnimationLayer", layerId, "AnimLayer::BaseLayer", ""));
            Connect("OO", layerId, stackId);

            foreach (var modelTake in take.ChildrenNamed("Model"))
            {
                if (modelTake.Properties.Count == 0 || modelTake.Properties[0] is not string modelName || !ids.TryGetValue(modelName, out var modelId))
                    continue;
                var transform = modelTake.ChildrenNamed("Channel").FirstOrDefault(ch => ch.Properties.Count > 0 && ch.Properties[0] as string == "Transform");
                if (transform is null)
                    continue;
                foreach (var channel in transform.ChildrenNamed("Channel"))
                {
                    var which = channel.Properties.Count > 0 ? channel.Properties[0] as string : null;
                    var property = which switch { "T" => "Lcl Translation", "R" => "Lcl Rotation", "S" => "Lcl Scaling", _ => null };
                    if (property is null)
                        continue;
                    var curveNodeId = nextId++;
                    var curveNode = NewObject("AnimationCurveNode", curveNodeId, "AnimCurveNode::" + which, "");
                    var cnProps = new FbxNode("Properties70");
                    var curves = new List<(string Axis, long Id)>();
                    foreach (var axisChannel in channel.ChildrenNamed("Channel"))
                    {
                        var axis = axisChannel.Properties.Count > 0 ? axisChannel.Properties[0] as string : null;
                        if (axis is not ("X" or "Y" or "Z"))
                            continue;
                        var def = axisChannel.Child("Default") is { Properties.Count: > 0 } d ? d.Prop<double>(0) : 0.0;
                        cnProps.Children.Add(P("d|" + axis, "Number", "", "A", def));
                        var (times, values) = ReadKeys(axisChannel.Child("Key"));
                        if (times.Length == 0)
                            continue;
                        var curveId = nextId++;
                        var curve = NewObject("AnimationCurve", curveId, "AnimCurve::", "");
                        var kt = new FbxNode("KeyTime");
                        kt.Properties.Add(times);
                        var kv = new FbxNode("KeyValueFloat");
                        kv.Properties.Add(values);
                        curve.Children.Add(kt);
                        curve.Children.Add(kv);
                        objects.Children.Add(curve);
                        curves.Add((axis, curveId));
                    }
                    curveNode.Children.Add(cnProps);
                    objects.Children.Add(curveNode);
                    Connect("OO", curveNodeId, layerId);
                    Connect("OP", curveNodeId, modelId, property);
                    foreach (var (axis, curveId) in curves)
                        Connect("OP", curveId, curveNodeId, "d|" + axis);
                }
            }
        }

        result.Children.Add(objects);
        result.Children.Add(connections);
        return result;
    }

    /// <summary>
    /// FBX 6 key list: <c>time, value, interpolation[, extra...]</c> per key. Times are integer
    /// ticks that only grow; everything between a value and the next such time (interpolation
    /// letters, tangents) is skipped.
    /// </summary>
    public static (long[] Times, float[] Values) ReadKeys(FbxNode? key)
    {
        if (key is null)
            return (Array.Empty<long>(), Array.Empty<float>());
        var p = key.Properties;
        var times = new List<long>();
        var values = new List<float>();
        var i = 0;
        while (i < p.Count)
        {
            if (p[i] is long or int && i + 1 < p.Count && IsNumber(p[i + 1]))
            {
                var t = Convert.ToInt64(p[i], System.Globalization.CultureInfo.InvariantCulture);
                if (times.Count == 0 || t > times[^1])
                {
                    times.Add(t);
                    values.Add((float)Convert.ToDouble(p[i + 1], System.Globalization.CultureInfo.InvariantCulture));
                    i += 2;
                    continue;
                }
            }
            i++;
        }
        return (times.ToArray(), values.ToArray());
    }

    private static bool IsNumber(object o) => o is long or int or double or float or short;

    private static bool IsGeometryChild(string name)
        => name is "Vertices" or "PolygonVertexIndex" or "Edges" or "GeometryVersion" or "Layer" or "Shape"
            || name.StartsWith("LayerElement", StringComparison.Ordinal);

    private static string NameOf(string full)
    {
        var i = full.IndexOf("::", StringComparison.Ordinal);
        return i >= 0 ? full[(i + 2)..] : full;
    }

    private static FbxNode NewObject(string type, long id, string fullName, string subClass)
    {
        var node = new FbxNode(type);
        node.Properties.Add(id);
        node.Properties.Add(fullName);
        node.Properties.Add(subClass);
        return node;
    }

    private static FbxNode P(string name, string type, string label, string flags, params object[] values)
    {
        var p = new FbxNode("P");
        p.Properties.Add(name);
        p.Properties.Add(type);
        p.Properties.Add(label);
        p.Properties.Add(flags);
        p.Properties.AddRange(values);
        return p;
    }

    /// <summary>Copies <paramref name="source"/>'s Properties60 into a Properties70 block on <paramref name="target"/>.</summary>
    private static FbxNode WithProperties70(FbxNode target, FbxNode source)
    {
        var block = new FbxNode("Properties70");
        foreach (var property in source.Child("Properties60")?.ChildrenNamed("Property") ?? Enumerable.Empty<FbxNode>())
        {
            // FBX 6: name, type, flags, values...  FBX 7: name, type, label, flags, values...
            if (property.Properties.Count < 2 || property.Properties[0] is not string name)
                continue;
            var p = new FbxNode("P");
            p.Properties.Add(name);
            p.Properties.Add(property.Properties[1]);
            p.Properties.Add("");
            p.Properties.AddRange(property.Properties.Skip(2));
            if (property.Properties.Count == 2)
                p.Properties.Add("");
            block.Children.Add(p);
        }
        target.Children.Add(block);
        return target;
    }
}
