#nullable enable annotations

using System.Numerics;

namespace WeaponImporter.EditorTools.Core.Geometry;

using Vector3 = System.Numerics.Vector3;

/// <summary>A ray/triangle hit.</summary>
public readonly record struct MeshHit(float Distance, int Triangle, Vector3 Point, Vector3 Normal);
