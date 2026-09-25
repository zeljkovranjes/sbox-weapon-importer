#nullable enable annotations

using System.Globalization;
using System.Text;

namespace WeaponImporter.Core.Output;

/// <summary>A KV3 value: object, array, string, number, bool or null.</summary>
public abstract record KvNode;

public sealed record KvObject : KvNode
{
    public List<(string Key, KvNode Value)> Entries { get; } = new();

    public KvObject Add(string key, KvNode value)
    {
        Entries.Add((key, value));
        return this;
    }

    public KvObject Add(string key, string value) => Add(key, new KvString(value));
    public KvObject Add(string key, double value) => Add(key, new KvNumber(value));
    public KvObject Add(string key, int value) => Add(key, new KvNumber(value, true));
    public KvObject Add(string key, bool value) => Add(key, new KvBool(value));
    public KvObject Add(string key, params double[] values) => Add(key, new KvArray(values.Select(v => (KvNode)new KvNumber(v)).ToList()));
}

public sealed record KvArray(List<KvNode> Items) : KvNode
{
    public KvArray() : this(new List<KvNode>()) { }
    public KvArray Add(KvNode node)
    {
        Items.Add(node);
        return this;
    }
}

public sealed record KvString(string Value) : KvNode;
public sealed record KvNumber(double Value, bool Integer = false) : KvNode;
public sealed record KvBool(bool Value) : KvNode;
public sealed record KvNull : KvNode;

/// <summary>
/// Text KV3 in the layout ModelDoc writes: CRLF, tabs, `key = ` before blocks, trailing commas
/// after array entries, scalar arrays on one line. Numbers never use exponent notation
/// (ModelDoc reads "1E-05" as 1).
/// </summary>
public static class Kv3
{
    public const string ModelDocHeader = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->";
    public const string GenericHeader = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->";

    public static string Serialize(KvObject root, string header = ModelDocHeader)
    {
        var sb = new StringBuilder();
        sb.Append(header).Append("\r\n");
        WriteObject(sb, root, 0);
        sb.Append("\r\n");
        return sb.ToString();
    }

    private static void Indent(StringBuilder sb, int depth) => sb.Append('\t', depth);

    private static void WriteObject(StringBuilder sb, KvObject obj, int depth)
    {
        sb.Append("{\r\n");
        foreach (var (key, value) in obj.Entries)
        {
            Indent(sb, depth + 1);
            sb.Append(Key(key)).Append(" = ");
            WriteValue(sb, value, depth + 1);
            sb.Append("\r\n");
        }
        Indent(sb, depth);
        sb.Append('}');
    }

    private static void WriteValue(StringBuilder sb, KvNode value, int depth)
    {
        switch (value)
        {
            case KvObject o:
                sb.Append("\r\n");
                Indent(sb, depth);
                WriteObject(sb, o, depth);
                break;
            case KvArray a when a.Items.All(i => i is KvNumber or KvBool or KvString) && a.Items.Count <= 16 && a.Items.All(i => i is not KvString s || s.Value.Length < 40):
                sb.Append("[ ");
                sb.Append(string.Join(", ", a.Items.Select(Scalar)));
                sb.Append(" ]");
                break;
            case KvArray a:
                sb.Append("\r\n");
                Indent(sb, depth);
                sb.Append("[\r\n");
                foreach (var item in a.Items)
                {
                    Indent(sb, depth + 1);
                    if (item is KvObject io)
                        WriteObject(sb, io, depth + 1);
                    else if (item is KvArray)
                        WriteValue(sb, item, depth + 1);
                    else
                        sb.Append(Scalar(item));
                    sb.Append(",\r\n");
                }
                Indent(sb, depth);
                sb.Append(']');
                break;
            default:
                sb.Append(Scalar(value));
                break;
        }
    }

    private static string Scalar(KvNode node) => node switch
    {
        KvString s => "\"" + s.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        KvNumber { Integer: true } n => ((long)n.Value).ToString(CultureInfo.InvariantCulture),
        KvNumber n => Number(n.Value),
        KvBool b => b.Value ? "true" : "false",
        KvNull => "null",
        _ => "null",
    };

    /// <summary>Fixed-point, no exponent, at least one decimal.</summary>
    public static string Number(double v)
    {
        if (!double.IsFinite(v))
            v = 0;
        if (Math.Abs(v) < 1e-9)
            return "0.0";
        var s = v.ToString("0.0#########", CultureInfo.InvariantCulture);
        return s;
    }

    private static string Key(string key)
        => key.All(c => char.IsLetterOrDigit(c) || c == '_') ? key : "\"" + key + "\"";
}
