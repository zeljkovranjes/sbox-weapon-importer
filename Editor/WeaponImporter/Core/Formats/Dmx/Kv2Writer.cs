#nullable enable annotations

using System.Globalization;
using System.Numerics;
using System.Text;

namespace WeaponImporter.Core.Formats.Dmx;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Low-level DMX <c>keyvalues2_noids</c> text emitter: CRLF line endings, tab indentation,
/// top-level elements separated by blank lines (the layout fbx2dmx produces).
/// </summary>
public sealed class Kv2Writer
{
    public const string ModelHeader = "<!-- dmx encoding keyvalues2_noids 4 format model 22 -->";

    private readonly StringBuilder _sb = new();
    private readonly bool _blankAfterInline;
    private int _indent;

    /// <param name="blankAfterInline">Emit an indentation-only line after each inline element (fbx2dmx animation quirk).</param>
    public Kv2Writer(bool blankAfterInline = false) => _blankAfterInline = blankAfterInline;

    public void Raw(string text) => _sb.Append(text).Append("\r\n");

    private void Line(string text) => _sb.Append('\t', _indent).Append(text).Append("\r\n");

    public void Attr(string name, string type, string value)
        => Line($"\"{Escape(name)}\" \"{type}\" \"{Escape(value)}\"");

    public void BeginTop(string className)
    {
        Line($"\"{className}\"");
        Line("{");
        _indent++;
    }

    public void EndTop()
    {
        _indent--;
        Line("}");
        _sb.Append("\r\n");
    }

    public void BeginInline(string name, string className)
    {
        Line($"\"{Escape(name)}\" \"{className}\"");
        Line("{");
        _indent++;
    }

    public void EndInline()
    {
        _indent--;
        Line("}");
        if (_blankAfterInline)
            Line("");
    }

    public void BeginArrayElement(string className)
    {
        Line($"\"{className}\"");
        Line("{");
        _indent++;
    }

    public void EndArrayElement(bool last)
    {
        _indent--;
        Line(last ? "}" : "},");
    }

    public void BeginArray(string name, string type = "element_array")
    {
        Line($"\"{Escape(name)}\" \"{type}\" ");
        Line("[");
        _indent++;
    }

    public void EndArray()
    {
        _indent--;
        Line("]");
    }

    public void ElementRef(string id, bool last) => Line($"\"element\" \"{id}\"" + (last ? "" : ","));

    public void Value(string value, bool last) => Line($"\"{Escape(value)}\"" + (last ? "" : ","));

    /// <summary>A whole value array on consecutive lines.</summary>
    public void ValueArray<T>(string name, string type, IReadOnlyList<T> values, Func<T, string> format)
    {
        BeginArray(name, type);
        for (var i = 0; i < values.Count; i++)
            Value(format(values[i]), i == values.Count - 1);
        EndArray();
    }

    public void Refs(string name, IReadOnlyList<string> ids)
    {
        BeginArray(name);
        for (var i = 0; i < ids.Count; i++)
            ElementRef(ids[i], i == ids.Count - 1);
        EndArray();
    }

    public void EmptyBinary(string name)
    {
        Line($"\"{Escape(name)}\" \"binary\" ");
        Line("\"");
        Line("\"");
    }

    public override string ToString() => _sb.ToString();

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    // ------------------------------------------------------------------ formatting

    /// <summary>Invariant float, up to 10 decimals, trailing zeros stripped, no negative zero.</summary>
    public static string F(float value)
    {
        if (value == 0f || !float.IsFinite(value))
            return "0";
        return ((double)value).ToString("0.##########", CultureInfo.InvariantCulture);
    }

    public static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Vec(Vector3 v) => $"{F(v.X)} {F(v.Y)} {F(v.Z)}";

    public static string Vec(Vector2 v) => $"{F(v.X)} {F(v.Y)}";

    public static string Quat(Quaternion q) => $"{F(q.X)} {F(q.Y)} {F(q.Z)} {F(q.W)}";

    public static string Time(double seconds) => seconds.ToString("0.0000", CultureInfo.InvariantCulture);
}

/// <summary>Deterministic element ids: a 128-bit FNV-1a hash of scope + path, formatted as a GUID.</summary>
public static class DmxIds
{
    public static string Guid(string scope, string path)
    {
        var bytes = Encoding.UTF8.GetBytes(scope + "\n" + path);
        var a = Fnv(bytes, 0xcbf29ce484222325UL);
        var b = Fnv(bytes, 0x84222325cbf29ce4UL ^ (ulong)bytes.Length);
        var hex = a.ToString("x16", CultureInfo.InvariantCulture) + b.ToString("x16", CultureInfo.InvariantCulture);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private static ulong Fnv(byte[] data, ulong basis)
    {
        var h = basis;
        foreach (var d in data)
        {
            h ^= d;
            h *= 0x100000001b3UL;
        }
        // Final avalanche (splitmix64) so similar paths differ in every digit.
        h ^= h >> 30;
        h *= 0xbf58476d1ce4e5b9UL;
        h ^= h >> 27;
        h *= 0x94d049bb133111ebUL;
        h ^= h >> 31;
        return h;
    }
}
