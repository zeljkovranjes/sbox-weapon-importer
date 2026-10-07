#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

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
