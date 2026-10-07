#nullable enable annotations

using System.Globalization;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Output;

public sealed record KvArray(List<KvNode> Items) : KvNode
{
    public KvArray() : this(new List<KvNode>()) { }
    public KvArray Add(KvNode node)
    {
        Items.Add(node);
        return this;
    }
}
