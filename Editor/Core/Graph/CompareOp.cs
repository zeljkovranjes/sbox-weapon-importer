#nullable enable annotations

namespace WeaponImporter.EditorTools.Core.Graph;

/// <summary>Comparison operators of <c>CParameterAnimCondition</c> / <c>CTimeCondition</c>.</summary>
public enum CompareOp
{
    Equal = 0,
    NotEqual = 1,
    Greater = 2,
    GreaterOrEqual = 3,
    Less = 4,
    LessOrEqual = 5,
}
