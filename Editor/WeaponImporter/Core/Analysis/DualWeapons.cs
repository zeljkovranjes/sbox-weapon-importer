#nullable enable annotations

using WeaponImporter.Core.Weapon;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Core.Analysis;

/// <summary>
/// Two copies of a weapon in one file, one per hand (dual pistols, dual sawn-offs): a pair of
/// bones whose names differ only by a side ("sawnoffR" / "sawnoffL", "Gun_L" / "Gun_R",
/// "Right_Pistol" / "Left_Pistol"), each carrying a similar amount of the weapon's geometry.
/// </summary>
public static class DualWeapons
{
    /// <summary>The right-hand weapon's bone, the left-hand one's, and the left one's triangles.</summary>
    public sealed record Pair(int Right, int Left, int[] LeftTriangles);

    /// <summary>Body parts come in left/right pairs too; they are never the weapons.</summary>
    private static readonly string[] BodyWords =
    {
        "shoulder", "clavicle", "collar", "arm", "upperarm", "forearm", "lowerarm", "elbow", "hand", "wrist", "palm", "finger", "thumb", "index",
        "middle", "ring", "pinky", "pink", "point", "thigh", "leg", "upleg", "calf", "shin", "knee", "foot", "ankle", "toe", "ball", "hip", "pelvis",
        "spine", "chest", "neck", "head", "eye", "ear", "brow", "lid", "cheek", "lip", "breast", "pec", "scapula", "twist", "ik", "pole", "goal", "target",
    };

    private static bool Separator(char c) => c is ' ' or '_' or '.' or '-';

    /// <summary>
    /// A bone name without its side, and the side ('L', 'R'), or null when it names none:
    /// "Gun_L", "gun.r", "sawnoffR" (camel case), "Pistol_Left", "LeftPistol", "R_gun".
    /// </summary>
    public static (string Base, char Side)? SideOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            return null;
        var lower = name.ToLowerInvariant();
        (string, char) Result(string rest, char side) => (rest.ToLowerInvariant().Trim(' ', '_', '.', '-'), side);
        // Whole words: "...left" / "...right" at the end or start (after a separator or camel case).
        foreach (var (word, side) in new[] { ("left", 'L'), ("right", 'R') })
        {
            if (lower.EndsWith(word) && lower.Length > word.Length)
            {
                var at = name.Length - word.Length;
                if (Separator(name[at - 1]) || (char.IsUpper(name[at]) && char.IsLower(name[at - 1])))
                    return Result(name[..at], side);
            }
            if (lower.StartsWith(word) && lower.Length > word.Length)
            {
                var next = name[word.Length];
                if (Separator(next) || (char.IsUpper(next) && char.IsLower(name[word.Length - 1])))
                    return Result(name[word.Length..], side);
            }
        }
        // Single letters: "gun_L", "gun.r", "sawnoffR"; "L_gun", "r.gun".
        var last = name[^1];
        if (last is 'L' or 'R' or 'l' or 'r')
        {
            var before = name[^2];
            if (Separator(before) || (char.IsUpper(last) && (char.IsLower(before) || char.IsDigit(before))))
                return Result(name[..^1], char.ToUpperInvariant(last));
        }
        var first = name[0];
        if (first is 'L' or 'R' or 'l' or 'r' && Separator(name[1]))
            return Result(name[1..], char.ToUpperInvariant(first));
        return null;
    }

    /// <summary>
    /// The pair holding the most weapon geometry, when the two sides carry similar amounts
    /// (within 35%) and neither is an arm bone; null for an ordinary single weapon.
    /// </summary>
    public static Pair? Find(WeaponAsset asset, IReadOnlySet<int> armBones, IReadOnlyList<int> weaponTriangles)
    {
        var skeleton = asset.Skeleton;
        var mesh = asset.Mesh;
        // Triangles under each bone's subtree.
        var owner = new List<int>[skeleton.Count];
        foreach (var t in weaponTriangles)
        {
            var b = mesh.TriangleBone(t);
            for (var x = b; x >= 0; x = skeleton[x].ParentIndex)
                (owner[x] ??= new List<int>()).Add(t);
        }
        var sides = new Dictionary<string, (int L, int R)>(StringComparer.Ordinal);
        for (var i = 0; i < skeleton.Count; i++)
        {
            if (armBones.Contains(i) || owner[i] is null || SideOf(skeleton[i].Name) is not { } side || side.Base.Length < 2)
                continue;
            if (NameTokens.Has(NameTokens.Split(side.Base), BodyWords))
                continue;
            sides.TryGetValue(side.Base, out var pair);
            sides[side.Base] = side.Side == 'L' ? (i + 1, pair.R) : (pair.L, i + 1);
        }
        Pair? best = null;
        var bestCount = 0;
        foreach (var (_, (l1, r1)) in sides)
        {
            if (l1 == 0 || r1 == 0)
                continue;
            int l = l1 - 1, r = r1 - 1;
            // Nested pairs (a left gun inside the right gun's subtree) are not two weapons.
            if (IsDescendant(skeleton, l, r) || IsDescendant(skeleton, r, l))
                continue;
            var left = owner[l]!;
            var right = owner[r]!;
            var small = Math.Min(left.Count, right.Count);
            var large = Math.Max(left.Count, right.Count);
            if (small < 12 || small < large * 0.65f)
                continue;
            // Together they must be most of the weapon (not a pair of small side parts).
            if (left.Count + right.Count < weaponTriangles.Count * 0.6f)
                continue;
            if (left.Count + right.Count > bestCount)
            {
                bestCount = left.Count + right.Count;
                best = new Pair(r, l, left.ToArray());
            }
        }
        return best;
    }

    /// <summary>
    /// How a character's left bones mirror its right ones in local rotation: the sign of the
    /// x, y and z parts (the citizen's are -x, -y, +z). Measured on the bind pose of every
    /// "_R" / "_L" bone pair, so any rig gets its own.
    /// </summary>
    public static System.Numerics.Vector3 MirrorSigns(Skeleton skeleton)
    {
        var candidates = new[] { new System.Numerics.Vector3(-1, -1, 1), new System.Numerics.Vector3(-1, 1, -1), new System.Numerics.Vector3(1, -1, -1) };
        var best = candidates[0];
        var bestError = float.MaxValue;
        foreach (var signs in candidates)
        {
            var error = 0f;
            for (var i = 0; i < skeleton.Count; i++)
            {
                var name = skeleton[i].Name;
                if (!name.EndsWith("_R", StringComparison.Ordinal) || skeleton.IndexOf(name[..^2] + "_L") is not (var l and >= 0))
                    continue;
                var mirrored = Mirror(skeleton[i].RestLocal.Rot, signs);
                error += 1f - MathF.Abs(System.Numerics.Quaternion.Dot(mirrored, skeleton[l].RestLocal.Rot));
            }
            if (error < bestError)
            {
                bestError = error;
                best = signs;
            }
        }
        return best;
    }

    /// <summary>A right-side local rotation as the left side has it.</summary>
    public static System.Numerics.Quaternion Mirror(System.Numerics.Quaternion q, System.Numerics.Vector3 signs)
        => new(q.X * signs.X, q.Y * signs.Y, q.Z * signs.Z, q.W);

    private static bool IsDescendant(Skeleton skeleton, int bone, int ancestor)
    {
        for (var x = skeleton[bone].ParentIndex; x >= 0; x = skeleton[x].ParentIndex)
            if (x == ancestor)
                return true;
        return false;
    }
}
