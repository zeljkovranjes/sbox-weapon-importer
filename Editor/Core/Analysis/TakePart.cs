#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Rig;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>One action found inside a longer take: frames [Start, End], inclusive.</summary>
public sealed record TakePart(int Start, int End, bool Rest)
{
    public int Frames => End - Start + 1;
}
