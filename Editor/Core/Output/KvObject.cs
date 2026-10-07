#nullable enable annotations

using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Output;

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
