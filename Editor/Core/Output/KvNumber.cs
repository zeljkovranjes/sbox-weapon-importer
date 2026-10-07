#nullable enable annotations

using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Output;

public sealed record KvNumber(double Value, bool Integer = false) : KvNode;
