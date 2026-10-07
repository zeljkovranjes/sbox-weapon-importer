#nullable enable annotations

using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Output;

/// <summary>A KV3 value: object, array, string, number, bool or null.</summary>
public abstract record KvNode;
