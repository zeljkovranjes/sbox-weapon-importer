#nullable enable annotations

using System.Collections;
using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Graph;

// Minimal KV3 value model + writer for animgraph2 documents, ported from the original importer
// (Serialization/Kv.cs + Kv3.cs, writer only). Kept separate from Core/Output/Kv3 on purpose: the
// layout differs slightly (trailing CRLF after the root, block-array rules) and this exact layout
// is the one verified to compile in the s&box AnimGraph editor. Prefixed names avoid clashing
// with WeaponImporter.EditorTools.Core.Output.KvObject & co.

/// <summary>Base of the animgraph KV3 value model.</summary>
public abstract class GraphKvValue
{
}
