#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

public sealed class GraphKvLong : GraphKvValue
{
    public long Value { get; }
    public GraphKvLong(long value) => Value = value;
}
