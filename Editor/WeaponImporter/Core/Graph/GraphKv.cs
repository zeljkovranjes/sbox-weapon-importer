#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.Core.Graph;

// Minimal KV3 value model + writer for animgraph2 documents, ported from the original importer
// (Serialization/Kv.cs + Kv3.cs, writer only). Kept separate from Core/Output/Kv3 on purpose: the
// layout differs slightly (trailing CRLF after the root, block-array rules) and this exact layout
// is the one verified to compile in the s&box AnimGraph editor. Prefixed names avoid clashing
// with WeaponImporter.Core.Output.KvObject & co.

/// <summary>Base of the animgraph KV3 value model.</summary>
public abstract class GraphKvValue
{
}

/// <summary>A KV3 object: insertion-ordered string-keyed map.</summary>
public sealed class GraphKvObject : GraphKvValue
{
    private readonly List<string> _keys = new();
    private readonly Dictionary<string, GraphKvValue> _map = new(StringComparer.Ordinal);

    /// <summary>Keys in insertion order.</summary>
    public IReadOnlyList<string> Keys => _keys;

    public int Count => _keys.Count;

    /// <summary>Gets a value (throws when absent) or sets it (appends new keys at the end).</summary>
    public GraphKvValue this[string key]
    {
        get => _map[key];
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (_map.TryAdd(key, value))
                _keys.Add(key);
            else
                _map[key] = value;
        }
    }

    public GraphKvValue? GetOrNull(string key) => _map.TryGetValue(key, out var v) ? v : null;
}

/// <summary>A KV3 array.</summary>
public sealed class GraphKvArray : GraphKvValue
{
    public List<GraphKvValue> Items { get; } = new();
}

public sealed class GraphKvString : GraphKvValue
{
    public string Value { get; }
    public GraphKvString(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
}

public sealed class GraphKvLong : GraphKvValue
{
    public long Value { get; }
    public GraphKvLong(long value) => Value = value;
}

public sealed class GraphKvDouble : GraphKvValue
{
    public double Value { get; }
    public GraphKvDouble(double value) => Value = value;
}

public sealed class GraphKvBool : GraphKvValue
{
    public bool Value { get; }
    public GraphKvBool(bool value) => Value = value;
}

public sealed class GraphKvNull : GraphKvValue
{
    public static readonly GraphKvNull Instance = new();
}

/// <summary>Terse builders so node construction reads like the file it produces.</summary>
public static class GraphKv
{
    /// <summary>Builds an object from key/value pairs; values are converted with <see cref="From"/>.</summary>
    public static GraphKvObject Obj(params (string Key, object? Value)[] pairs)
    {
        var obj = new GraphKvObject();
        foreach (var (key, value) in pairs)
            obj[key] = From(value);
        return obj;
    }

    public static GraphKvArray Arr(IEnumerable<object?> values)
    {
        var array = new GraphKvArray();
        foreach (var value in values)
            array.Items.Add(From(value));
        return array;
    }

    public static GraphKvArray Arr(params object?[] values) => Arr((IEnumerable<object?>)values);

    /// <summary>Converts a CLR value to a KV3 value.</summary>
    public static GraphKvValue From(object? value) => value switch
    {
        null => GraphKvNull.Instance,
        GraphKvValue kv => kv,
        string s => new GraphKvString(s),
        bool b => new GraphKvBool(b),
        int i => new GraphKvLong(i),
        long l => new GraphKvLong(l),
        uint u => new GraphKvLong(u),
        float f => new GraphKvDouble(f),
        double d => new GraphKvDouble(d),
        IEnumerable enumerable => Arr(enumerable.Cast<object?>()),
        _ => throw new ArgumentException($"Unsupported KV3 value type {value.GetType().Name}."),
    };

    /// <summary>An <c>{ m_id = N }</c> reference object.</summary>
    public static GraphKvObject Id(long id) => Obj(("m_id", id));

    /// <summary>
    /// Serializes a document in the shipped style: header line, tabs, <c>key = </c> before block
    /// values, trailing commas after array entries, inline scalar arrays, CRLF line endings.
    /// </summary>
    public static string Serialize(string header, GraphKvObject root)
    {
        if (header is null)
            throw new ArgumentNullException(nameof(header));
        if (root is null)
            throw new ArgumentNullException(nameof(root));
        var sb = new StringBuilder();
        sb.Append(header).Append("\r\n");
        WriteObjectBlock(sb, root, 0, trailingComma: false);
        return sb.ToString();
    }

    private static void WriteObjectBlock(StringBuilder sb, GraphKvObject obj, int indent, bool trailingComma)
    {
        Indent(sb, indent).Append("{\r\n");
        foreach (var key in obj.Keys)
            WritePair(sb, key, obj[key], indent + 1);
        Indent(sb, indent).Append(trailingComma ? "},\r\n" : "}\r\n");
    }

    private static void WritePair(StringBuilder sb, string key, GraphKvValue value, int indent)
    {
        var keyText = IsIdentifier(key) ? key : QuoteString(key);
        switch (value)
        {
            case GraphKvObject o:
                Indent(sb, indent).Append(keyText).Append(" = \r\n");
                WriteObjectBlock(sb, o, indent, trailingComma: false);
                break;
            case GraphKvArray a when IsBlockArray(a):
                Indent(sb, indent).Append(keyText).Append(" = \r\n");
                WriteArrayBlock(sb, a, indent, trailingComma: false);
                break;
            default:
                Indent(sb, indent).Append(keyText).Append(" = ").Append(ScalarText(value)).Append("\r\n");
                break;
        }
    }

    private static void WriteArrayBlock(StringBuilder sb, GraphKvArray array, int indent, bool trailingComma)
    {
        Indent(sb, indent).Append("[\r\n");
        foreach (var item in array.Items)
        {
            switch (item)
            {
                case GraphKvObject o:
                    WriteObjectBlock(sb, o, indent + 1, trailingComma: true);
                    break;
                case GraphKvArray a when IsBlockArray(a):
                    WriteArrayBlock(sb, a, indent + 1, trailingComma: true);
                    break;
                case GraphKvArray a:
                    Indent(sb, indent + 1).Append(InlineArrayText(a)).Append(",\r\n");
                    break;
                default:
                    Indent(sb, indent + 1).Append(ScalarText(item)).Append(",\r\n");
                    break;
            }
        }
        Indent(sb, indent).Append(trailingComma ? "],\r\n" : "]\r\n");
    }

    /// <summary>Arrays holding objects or block arrays are multi-line; scalar arrays stay on one line.</summary>
    private static bool IsBlockArray(GraphKvArray array)
    {
        foreach (var item in array.Items)
        {
            if (item is GraphKvObject || (item is GraphKvArray nested && IsBlockArray(nested)))
                return true;
        }
        return false;
    }

    private static string InlineArrayText(GraphKvArray array)
    {
        if (array.Items.Count == 0)
            return "[ ]";
        var sb = new StringBuilder("[ ");
        for (var i = 0; i < array.Items.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(array.Items[i] is GraphKvArray nested ? InlineArrayText(nested) : ScalarText(array.Items[i]));
        }
        return sb.Append(" ]").ToString();
    }

    private static string ScalarText(GraphKvValue value)
        => value switch
        {
            GraphKvString s => QuoteString(s.Value),
            GraphKvLong l => l.Value.ToString(CultureInfo.InvariantCulture),
            GraphKvDouble d => DoubleText(d.Value),
            GraphKvBool b => b.Value ? "true" : "false",
            GraphKvNull => "null",
            GraphKvArray a => InlineArrayText(a),
            _ => throw new FormatException($"Cannot serialize {value.GetType().Name} as a scalar."),
        };

    private static string DoubleText(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new FormatException($"Cannot serialize non-finite double {value} to KV3.");
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        // The editor's text reader can treat an exponent as a separate token (1E-05 becomes 1):
        // expand the round-trip digits without rounding tiny offsets away.
        var exponentAt = s.IndexOf('E');
        if (exponentAt >= 0)
        {
            var negative = s[0] == '-';
            var mantissa = s.Substring(negative ? 1 : 0, exponentAt - (negative ? 1 : 0));
            var dot = mantissa.IndexOf('.');
            var point = (dot < 0 ? mantissa.Length : dot)
                + int.Parse(s.Substring(exponentAt + 1), CultureInfo.InvariantCulture);
            var digits = mantissa.Replace(".", "");
            s = point <= 0 ? "0." + new string('0', -point) + digits
                : point >= digits.Length ? digits + new string('0', point - digits.Length)
                : digits.Insert(point, ".");
            if (negative) s = "-" + s;
        }
        return s.Contains('.') ? s : s + ".0";
    }

    private static string QuoteString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\'': sb.Append("\\'"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0 || (!char.IsLetter(s[0]) && s[0] != '_'))
            return false;
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }
        return true;
    }

    private static StringBuilder Indent(StringBuilder sb, int indent) => sb.Append('\t', indent);
}
