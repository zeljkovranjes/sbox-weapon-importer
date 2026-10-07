#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Maths;

namespace WeaponImporter.EditorTools.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// What the solver knows about a point on the weapon a hand should hold. Every value is
/// measured from the mesh, never assumed.
/// </summary>
public sealed record GripSurface
{
    /// <summary>Contact point on the surface.</summary>
    public required Vector3 Contact { get; init; }

    /// <summary>Outward surface normal at the contact.</summary>
    public required Vector3 Normal { get; init; }

    /// <summary>Long axis of the local shape (along a pistol grip, along a handguard).</summary>
    public required Vector3 Axis { get; init; }

    /// <summary>Estimated centre of the cross-section under the contact.</summary>
    public required Vector3 Center { get; init; }

    /// <summary>Thickness through the part along the normal.</summary>
    public required float Depth { get; init; }

    /// <summary>Width across the part, perpendicular to both axis and normal.</summary>
    public required float Width { get; init; }

    /// <summary>Radius a wrapping hand closes around (half the mean of depth and width).</summary>
    public float Radius => MathF.Max(0.15f, (Depth + Width) * 0.25f);

    /// <summary>Free space outward from the contact before other geometry (for the palm).</summary>
    public required float Clearance { get; init; }

    /// <summary>How far the shape runs along the axis either side of the contact.</summary>
    public required float ExtentBack { get; init; }
    public required float ExtentForward { get; init; }

    /// <summary>Triangles within a hand-sized sphere around the contact.</summary>
    public required int NearbyTriangles { get; init; }

    /// <summary>Part (mesh) names within reach of the hand.</summary>
    public required IReadOnlyList<string> NearbyParts { get; init; }

    /// <summary>Surface triangle the contact lies on.</summary>
    public required int Triangle { get; init; }

    /// <summary>
    /// Local frame: X = axis, Y = normal x axis, Z = outward normal (projected orthogonal).
    /// </summary>
    public Quaternion Frame => MathQ.FromAxes(Axis, Vector3.Cross(Normal, Axis));
}
