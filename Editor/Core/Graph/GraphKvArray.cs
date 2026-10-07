#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

/// <summary>A KV3 array.</summary>
public sealed class GraphKvArray : GraphKvValue
{
    public List<GraphKvValue> Items { get; } = new();
}
