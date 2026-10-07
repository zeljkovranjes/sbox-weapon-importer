#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

public sealed class GraphKvDouble : GraphKvValue
{
    public double Value { get; }
    public GraphKvDouble(double value) => Value = value;
}
