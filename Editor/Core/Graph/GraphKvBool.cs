#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

public sealed class GraphKvBool : GraphKvValue
{
    public bool Value { get; }
    public GraphKvBool(bool value) => Value = value;
}
