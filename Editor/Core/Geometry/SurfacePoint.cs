#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.EditorTools.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>Closest surface point to a query position.</summary>
public readonly record struct SurfacePoint(Vector3 Point, Vector3 Normal, float Distance, int Triangle)
{
    /// <summary>True when the query lies behind the surface (inside a closed shell).</summary>
    public bool Inside { get; init; }

    /// <summary>Signed distance: negative inside.</summary>
    public float Signed => Inside ? -Distance : Distance;
}
