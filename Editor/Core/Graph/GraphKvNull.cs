#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

public sealed class GraphKvNull : GraphKvValue
{
    public static readonly GraphKvNull Instance = new();
}
