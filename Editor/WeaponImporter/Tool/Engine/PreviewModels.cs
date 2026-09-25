using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using N = System.Numerics;

namespace WeaponImporter.Tool;

/// <summary>
/// Render models built in memory, so the weapon appears in the viewport the moment it is
/// analyzed (before anything is compiled): the file's own UVs and normals, one mesh per material,
/// textured when <see cref="PreviewMaterials"/> are given.
/// </summary>
public static class PreviewModels
{
    public const string SurfaceMaterial = "materials/default.vmat";

    /// <summary>The canonical weapon (weapon triangles only) as a static render model.</summary>
    public static Model BuildWeaponModel( WeaponAnalysis analysis, IReadOnlyList<Material> materials = null )
        => Build( analysis.Asset.Mesh, analysis.WeaponTriangles, materials );

    /// <summary>
    /// A render model of some triangles of a mesh. <paramref name="materials"/> follow the mesh's
    /// materials (null or missing entries use the plain surface material).
    /// </summary>
    public static Model Build( TriMesh mesh, IReadOnlyList<int> triangles, IReadOnlyList<Material> materials = null )
    {
        var normals = mesh.EnsureNormals();
        var uvs = mesh.CornerUVs;
        var fallback = Material.Load( SurfaceMaterial );
        var builder = Model.Builder;
        foreach ( var group in triangles.GroupBy( t => mesh.TriangleMaterial.Length > t ? mesh.TriangleMaterial[t] : 0 ) )
        {
            var vertices = new List<Vertex>( group.Count() * 3 );
            var indices = new List<int>( group.Count() * 3 );
            var bounds = Bounds.Empty;
            foreach ( var t in group )
            {
                var (a, b, c) = mesh.Triangle( t );
                var tangent = N.Vector3.Normalize( b - a );
                var corners = new[] { a, b, c };
                for ( var k = 0; k < 3; k++ )
                {
                    var corner = t * 3 + k;
                    var p = corners[k];
                    var n = normals[corner];
                    // No UVs in the file: a planar projection keeps the surface readable.
                    var uv = uvs is not null ? uvs[corner] : new N.Vector2( p.X * 0.1f, p.Y * 0.1f );
                    indices.Add( vertices.Count );
                    vertices.Add( new Vertex( p.ToEngine(), n.ToEngine(), new Vector4( tangent.X, tangent.Y, tangent.Z, 1 ), new Vector4( uv.X, uv.Y, 0, 0 ) ) );
                    bounds = bounds.Encapsulate( p );
                }
            }
            var material = materials is not null && group.Key >= 0 && group.Key < materials.Count ? materials[group.Key] ?? fallback : fallback;
            var m = new Mesh( material );
            m.CreateVertexBuffer( vertices.Count, Vertex.Layout, vertices );
            m.CreateIndexBuffer( indices.Count, indices );
            m.Bounds = new BBox( bounds.Min.ToEngine(), bounds.Max.ToEngine() );
            builder = builder.AddMesh( m );
        }
        return builder.Create();
    }
}
