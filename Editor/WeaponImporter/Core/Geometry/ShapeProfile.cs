#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Silhouette of a weapon sliced along its length (+X): per slice the lowest and highest
/// surface and the left/right extent. Pistol grips, magazines, stocks and handguards all show
/// up as characteristic shapes in these profiles.
/// </summary>
public sealed class ShapeProfile
{
    public float MinX { get; }
    public float BinWidth { get; }
    public int Count { get; }

    public float[] Bottom { get; }
    public float[] Top { get; }
    public float[] Left { get; }   // +Y
    public float[] Right { get; }  // -Y
    public int[] Samples { get; }

    public float MaxX => MinX + BinWidth * Count;

    private ShapeProfile(float minX, float width, int count)
    {
        MinX = minX;
        BinWidth = width;
        Count = count;
        Bottom = Enumerable.Repeat(float.MaxValue, count).ToArray();
        Top = Enumerable.Repeat(float.MinValue, count).ToArray();
        Left = Enumerable.Repeat(float.MinValue, count).ToArray();
        Right = Enumerable.Repeat(float.MaxValue, count).ToArray();
        Samples = new int[count];
    }

    public bool Has(int bin) => bin >= 0 && bin < Count && Samples[bin] > 0;
    public float X(int bin) => MinX + (bin + 0.5f) * BinWidth;
    public int Bin(float x) => Math.Clamp((int)((x - MinX) / BinWidth), 0, Count - 1);
    public float Height(int bin) => Has(bin) ? Top[bin] - Bottom[bin] : 0f;
    public float Width(int bin) => Has(bin) ? Left[bin] - Right[bin] : 0f;

    /// <summary>Builds the profile from the given triangles (all when null).</summary>
    public static ShapeProfile Build(TriMesh mesh, IEnumerable<int>? triangles = null, float binWidth = 0.25f)
    {
        var tris = triangles?.ToArray() ?? Enumerable.Range(0, mesh.TriangleCount).ToArray();
        var bounds = Bounds.Empty;
        foreach (var t in tris)
        {
            var (a, b, c) = mesh.Triangle(t);
            bounds = bounds.Encapsulate(a).Encapsulate(b).Encapsulate(c);
        }
        if (bounds.IsEmpty)
            return new ShapeProfile(0f, binWidth, 1);

        var length = MathF.Max(bounds.Size.X, binWidth);
        // Keep the bin count bounded for very long or very finely tessellated props.
        binWidth = MathF.Max(binWidth, length / 400f);
        var count = Math.Max(1, (int)MathF.Ceiling(length / binWidth) + 1);
        var profile = new ShapeProfile(bounds.Min.X, binWidth, count);

        foreach (var t in tris)
        {
            var (a, b, c) = mesh.Triangle(t);
            // Sample the triangle densely enough that large faces land in every bin they cross.
            var span = MathF.Max(MathF.Max(Vector3.Distance(a, b), Vector3.Distance(b, c)), Vector3.Distance(c, a));
            var steps = Math.Clamp((int)MathF.Ceiling(span / (binWidth * 0.75f)), 1, 64);
            for (var i = 0; i <= steps; i++)
            {
                for (var j = 0; j <= steps - i; j++)
                {
                    var u = i / (float)steps;
                    var v = j / (float)steps;
                    profile.Add(a + (b - a) * u + (c - a) * v);
                }
            }
        }
        return profile;
    }

    private void Add(Vector3 p)
    {
        var bin = Bin(p.X);
        Samples[bin]++;
        Bottom[bin] = MathF.Min(Bottom[bin], p.Z);
        Top[bin] = MathF.Max(Top[bin], p.Z);
        Left[bin] = MathF.Max(Left[bin], p.Y);
        Right[bin] = MathF.Min(Right[bin], p.Y);
    }

    /// <summary>Median of a per-bin value over occupied bins in [from, to].</summary>
    public float Median(Func<int, float> value, int from = 0, int to = int.MaxValue)
    {
        var values = new List<float>();
        for (var i = Math.Max(0, from); i <= Math.Min(Count - 1, to); i++)
            if (Has(i))
                values.Add(value(i));
        if (values.Count == 0)
            return 0f;
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>Percentile (0..1) of a per-bin value over occupied bins in [from, to].</summary>
    public float Percentile(Func<int, float> value, float q, int from = 0, int to = int.MaxValue)
    {
        var values = new List<float>();
        for (var i = Math.Max(0, from); i <= Math.Min(Count - 1, to); i++)
            if (Has(i))
                values.Add(value(i));
        if (values.Count == 0)
            return 0f;
        values.Sort();
        return values[Math.Clamp((int)(q * (values.Count - 1)), 0, values.Count - 1)];
    }

    /// <summary>Contiguous runs of occupied bins where <paramref name="predicate"/> holds.</summary>
    public List<(int Start, int End)> Runs(Func<int, bool> predicate, int minLength = 1)
    {
        var runs = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i <= Count; i++)
        {
            var on = i < Count && Has(i) && predicate(i);
            if (on && start < 0)
                start = i;
            else if (!on && start >= 0)
            {
                if (i - start >= minLength)
                    runs.Add((start, i - 1));
                start = -1;
            }
        }
        return runs;
    }
}
