#nullable enable annotations

using System.Globalization;
using System.Numerics;
using System.Text;

namespace WeaponImporter.EditorTools.Core.Formats.Dmx;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

/// <summary>Deterministic element ids: a 128-bit FNV-1a hash of scope + path, formatted as a GUID.</summary>
public static class DmxIds
{
    public static string Guid(string scope, string path)
    {
        var bytes = Encoding.UTF8.GetBytes(scope + "\n" + path);
        var a = Fnv(bytes, 0xcbf29ce484222325UL);
        var b = Fnv(bytes, 0x84222325cbf29ce4UL ^ (ulong)bytes.Length);
        var hex = a.ToString("x16", CultureInfo.InvariantCulture) + b.ToString("x16", CultureInfo.InvariantCulture);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private static ulong Fnv(byte[] data, ulong basis)
    {
        var h = basis;
        foreach (var d in data)
        {
            h ^= d;
            h *= 0x100000001b3UL;
        }
        // Final avalanche (splitmix64) so similar paths differ in every digit.
        h ^= h >> 30;
        h *= 0xbf58476d1ce4e5b9UL;
        h ^= h >> 27;
        h *= 0x94d049bb133111ebUL;
        h ^= h >> 31;
        return h;
    }
}
