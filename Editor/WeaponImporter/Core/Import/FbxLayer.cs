#nullable enable annotations

using WeaponImporter.Core.Formats.Fbx;

namespace WeaponImporter.Core.Import;

/// <summary>
/// One FBX geometry layer element (LayerElementNormal / LayerElementUV / ...): mapping
/// (ByPolygonVertex, ByVertice/ByControlPoint, ByPolygon, AllSame) and reference
/// (Direct, IndexToDirect) resolved to a value per corner.
/// </summary>
internal sealed class FbxLayer
{
    private readonly string _mapping;
    private readonly double[] _values;
    private readonly int[]? _index;
    private readonly int _stride;

    private FbxLayer(string mapping, double[] values, int[]? index, int stride)
    {
        _mapping = mapping;
        _values = values;
        _index = index;
        _stride = stride;
    }

    /// <summary>The first (lowest layer number) element named <paramref name="element"/>, or null.</summary>
    public static FbxLayer? Read(FbxNode geometry, string element, string valuesName, string[] indexNames, int stride)
    {
        var node = First(geometry, element);
        if (node is null)
            return null;
        var values = node.Child(valuesName)?.AsDoubleArray(0);
        if (values is null || values.Length < stride)
            return null;
        var mapping = StringOf(node, "MappingInformationType") ?? "ByPolygonVertex";
        var reference = StringOf(node, "ReferenceInformationType") ?? "Direct";
        int[]? index = null;
        if (reference is "IndexToDirect" or "Index")
        {
            foreach (var name in indexNames)
                if (node.Child(name) is { } ix)
                {
                    index = ix.AsIntArray(0);
                    break;
                }
            if (index is null)
                return null;
        }
        return new FbxLayer(mapping, values, index, stride);
    }

    /// <summary>Value slice for a corner, or null when the layer does not cover it.</summary>
    public double[]? Get(int polygonVertex, int controlPoint, int polygon)
    {
        var i = _mapping switch
        {
            "ByPolygonVertex" => polygonVertex,
            "ByVertice" or "ByVertex" or "ByControlPoint" => controlPoint,
            "ByPolygon" => polygon,
            "AllSame" => 0,
            _ => polygonVertex,
        };
        if (_index is not null)
        {
            if ((uint)i >= (uint)_index.Length)
                return null;
            i = _index[i];
        }
        if (i < 0 || (long)(i + 1) * _stride > _values.Length)
            return null;
        var result = new double[_stride];
        Array.Copy(_values, i * _stride, result, 0, _stride);
        foreach (var d in result)
            if (!double.IsFinite(d))
                return null;
        return result;
    }

    /// <summary>LayerElementMaterial: per-polygon index into the model's connected materials.</summary>
    public static MaterialLayer? ReadMaterials(FbxNode geometry)
    {
        var node = First(geometry, "LayerElementMaterial");
        var values = node?.Child("Materials")?.AsIntArray(0);
        if (node is null || values is null || values.Length == 0)
            return null;
        return new MaterialLayer(StringOf(node, "MappingInformationType") ?? "AllSame", values);
    }

    internal sealed class MaterialLayer
    {
        private readonly bool _allSame;
        private readonly int[] _values;

        public MaterialLayer(string mapping, int[] values)
        {
            _allSame = mapping == "AllSame";
            _values = values;
        }

        public int Get(int polygon) => _allSame ? _values[0] : (uint)polygon < (uint)_values.Length ? _values[polygon] : _values[0];
    }

    private static FbxNode? First(FbxNode geometry, string element)
    {
        FbxNode? best = null;
        var bestLayer = long.MaxValue;
        foreach (var n in geometry.ChildrenNamed(element))
        {
            long layer = 0;
            if (n.Properties.Count > 0 && n.Properties[0] is int or long or short)
                layer = n.Prop<long>(0);
            if (layer < bestLayer)
            {
                best = n;
                bestLayer = layer;
            }
        }
        return best;
    }

    private static string? StringOf(FbxNode node, string name)
        => node.Child(name) is { Properties.Count: > 0 } c && c.Properties[0] is string s ? s.Trim() : null;
}
