using Sandbox;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.Tool;

/// <summary>
/// Reads an existing s&amp;box model as a weapon source: bind skeleton, render triangles (each
/// vertex assigned to its nearest bone, since the engine exposes no skin weights) and every
/// sequence sampled at 30 fps.
/// </summary>
public static class VmdlReader
{
    public static WeaponAsset Read( string path, string name )
    {
        var model = Model.Load( path );
        if ( model is null || model.IsError )
            throw new InvalidOperationException( $"'{path}' is not a loadable model." );

        var skeleton = model.BoneCount > 0 ? CharacterLibrary.SkeletonOf( model ) : WeaponAsset.SingleBone();
        var verts = model.GetVertices() ?? Array.Empty<Vertex>();
        var raw = model.GetIndices() ?? Array.Empty<uint>();
        if ( verts.Length == 0 || raw.Length < 3 )
            throw new InvalidOperationException( $"'{path}' has no readable render mesh." );

        var positions = verts.Select( v => v.Position.ToCore() ).ToArray();
        var indices = raw.Take( raw.Length - raw.Length % 3 ).Select( i => (int)i ).Where( i => i < positions.Length ).ToArray();
        if ( indices.Length % 3 != 0 )
            indices = indices.Take( indices.Length - indices.Length % 3 ).ToArray();

        // Nearest bone by distance to the bone segment (bone origin to its first child).
        var boneSegments = Enumerable.Range( 0, skeleton.Count ).Select( b =>
        {
            var head = skeleton.RestWorld[b].Pos;
            var child = Enumerable.Range( 0, skeleton.Count ).FirstOrDefault( c => skeleton[c].ParentIndex == b, -1 );
            var tail = child >= 0 ? skeleton.RestWorld[child].Pos : head;
            return (head, tail);
        } ).ToArray();
        var vertexBone = positions.Select( p =>
        {
            var best = 0;
            var bestD = float.MaxValue;
            for ( var b = 0; b < boneSegments.Length; b++ )
            {
                var (h, t) = boneSegments[b];
                var ab = t - h;
                var u = ab.LengthSquared() > 1e-8f ? Math.Clamp( N.Vector3.Dot( p - h, ab ) / ab.LengthSquared(), 0f, 1f ) : 0f;
                var d = N.Vector3.DistanceSquared( p, h + ab * u );
                if ( d < bestD ) { bestD = d; best = b; }
            }
            return best;
        } ).ToArray();

        var clips = new List<Clip>();
        var world = new SceneWorld();
        try
        {
            var sm = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false };
            foreach ( var seq in model.AnimationNames )
            {
                sm.CurrentSequence.Name = seq;
                sm.Update( 0 );
                var duration = sm.CurrentSequence.Duration;
                var frames = Math.Clamp( (int)MathF.Round( duration * 30f ) + 1, 2, 3000 );
                var clip = new Clip( seq, 30f, false );
                for ( var f = 0; f < frames; f++ )
                {
                    sm.CurrentSequence.Time = duration * f / (frames - 1);
                    sm.Update( 0 );
                    var locals = new XForm[skeleton.Count];
                    for ( var b = 0; b < skeleton.Count; b++ )
                        locals[b] = sm.GetParentSpaceBone( b ).ToCore();
                    clip.Frames.Add( locals );
                }
                clips.Add( clip );
            }
            sm.Delete();
        }
        finally
        {
            world.Delete();
        }

        var attachments = model.Attachments?.All?.Select( a => new SourceAttachment( a.Name, a.Bone?.Name ?? skeleton[0].Name, a.LocalTransform.ToCore() ) ).ToList() ?? new List<SourceAttachment>();
        return new WeaponAsset
        {
            Name = name,
            Kind = SourceKind.Vmdl,
            SourcePath = path,
            Skeleton = skeleton,
            Mesh = new TriMesh( positions, indices, vertexBone ),
            Clips = clips,
            Attachments = attachments,
        };
    }
}
