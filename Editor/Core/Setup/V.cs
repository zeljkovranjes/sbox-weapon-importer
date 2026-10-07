#nullable enable annotations

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>Array conversions for the serializable types.</summary>
public static class V
{
    public static float[] A(Vector3 v) => new[] { v.X, v.Y, v.Z };
    public static float[] A(Quaternion q) => new[] { q.X, q.Y, q.Z, q.W };
    public static float[] A(XForm x) => new[] { x.Pos.X, x.Pos.Y, x.Pos.Z, x.Rot.X, x.Rot.Y, x.Rot.Z, x.Rot.W };
    public static Vector3 Of(float[]? a) => a is { Length: >= 3 } ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
    public static Quaternion Q(float[]? a) => a is { Length: >= 4 } ? MathQ.Normalize(new Quaternion(a[0], a[1], a[2], a[3])) : Quaternion.Identity;
    public static XForm X(float[]? a) => a is { Length: >= 7 } ? new XForm(new Vector3(a[0], a[1], a[2]), MathQ.Normalize(new Quaternion(a[3], a[4], a[5], a[6]))) : XForm.Identity;
}
