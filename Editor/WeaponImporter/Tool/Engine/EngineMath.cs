using Sandbox;
using WeaponImporter.Core.Maths;
using N = System.Numerics;

namespace WeaponImporter.Tool;

/// <summary>Conversions between the core's System.Numerics types and s&amp;box types.</summary>
public static class EngineMath
{
    public static Vector3 ToEngine( this N.Vector3 v ) => new( v.X, v.Y, v.Z );
    public static N.Vector3 ToCore( this Vector3 v ) => new( v.x, v.y, v.z );
    public static Rotation ToEngine( this N.Quaternion q ) => new( q.X, q.Y, q.Z, q.W );
    public static N.Quaternion ToCore( this Rotation r ) => new( r.x, r.y, r.z, r.w );
    public static Transform ToEngine( this XForm x ) => new( x.Pos.ToEngine(), x.Rot.ToEngine() );
    public static XForm ToCore( this Transform t ) => new( t.Position.ToCore(), t.Rotation.ToCore() );
}
