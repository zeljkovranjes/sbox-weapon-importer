#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

public sealed class GraphKvString : GraphKvValue
{
    public string Value { get; }
    public GraphKvString(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
}
