using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using N = System.Numerics;

namespace WeaponImporter.Tool;

/// <summary>
/// Render models built in memory, so the weapon appears in the viewport the moment it is
/// analyzed (before anything is compiled).
/// </summary>
public static class PreviewModels
{
    public const string SurfaceMaterial = "materials/default.vmat";

    /// <summary>The canonical weapon (weapon triangles only) as a static render model.</summary>
    public static Model BuildWeaponModel( WeaponAnalysis analysis ) => Build( analysis.Asset.Mesh, analysis.WeaponTriangles, SurfaceMaterial );

    public static Model Build( TriMesh mesh, IReadOnlyList<int> triangles, string material )
    {
        var vertices = new List<Vertex>( triangles.Count * 3 );
        var indices = new List<int>( triangles.Count * 3 );
        var bounds = Bounds.Empty;
        foreach ( var t in triangles )
        {
            var (a, b, c) = mesh.Triangle( t );
            var n = mesh.FaceNormal( t );
            var tangent = N.Vector3.Normalize( b - a );
            foreach ( var p in new[] { a, b, c } )
            {
                indices.Add( vertices.Count );
                vertices.Add( new Vertex( p.ToEngine(), n.ToEngine(), new Vector4( tangent.X, tangent.Y, tangent.Z, 1 ), new Vector4( p.X * 0.1f, p.Y * 0.1f, 0, 0 ) ) );
                bounds = bounds.Encapsulate( p );
            }
        }
        var m = new Mesh( Material.Load( material ) );
        m.CreateVertexBuffer( vertices.Count, Vertex.Layout, vertices );
        m.CreateIndexBuffer( indices.Count, indices );
        m.Bounds = new BBox( bounds.Min.ToEngine(), bounds.Max.ToEngine() );
        return Model.Builder.AddMesh( m ).Create();
    }
}
