#nullable enable annotations

using System.Text;

namespace WeaponImporter.Core.Analysis;

/// <summary>
/// Splits bone, mesh and animation names into lowercase tokens: <c>"tag_MagazineLeft01"</c>
/// becomes <c>tag, magazine, left, 01</c>. Rig noise such as <c>mixamorig:</c> or
/// <c>Armature|</c> prefixes is stripped first.
/// </summary>
public static class NameTokens
{
    private static readonly string[] Prefixes = { "mixamorig", "armature", "rootnode", "scene", "valvebiped" };

    public static string[] Split(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Array.Empty<string>();

        // Keep the part after namespace/take separators ("Armature|reload", "rig:mag").
        var clean = name.Trim();
        var sep = clean.LastIndexOfAny(new[] { '|', ':' });
        if (sep >= 0 && sep < clean.Length - 1)
            clean = clean[(sep + 1)..];

        var tokens = new List<string>();
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length > 0)
                tokens.Add(sb.ToString().ToLowerInvariant());
            sb.Clear();
        }

        for (var i = 0; i < clean.Length; i++)
        {
            var c = clean[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }
            if (sb.Length > 0)
            {
                var prev = clean[i - 1];
                var lowerToUpper = char.IsLower(prev) && char.IsUpper(c);
                var letterDigit = char.IsLetter(prev) != char.IsLetter(c);
                // "ADSFire" -> ads, fire: an upper run followed by upper+lower splits before the last upper.
                var acronymEnd = char.IsUpper(prev) && char.IsUpper(c) && i + 1 < clean.Length && char.IsLower(clean[i + 1]);
                if (lowerToUpper || letterDigit || acronymEnd)
                    Flush();
            }
            sb.Append(c);
        }
        Flush();

        return tokens.Where(t => !Prefixes.Contains(t)).ToArray();
    }

    /// <summary>
    /// Whether any token equals one of the aliases, or starts with an alias of five or more
    /// letters ("sights" matches "sight"; "handle" does not match "hand").
    /// </summary>
    public static bool Has(IReadOnlyList<string> tokens, params string[] aliases)
    {
        foreach (var t in tokens)
            foreach (var a in aliases)
                if (t == a || (a.Length >= 5 && t.StartsWith(a, StringComparison.Ordinal)))
                    return true;
        return false;
    }

    /// <summary>Concatenated tokens, for aliases spanning tokens ("fireempty", "reloadempty").</summary>
    public static string Joined(IReadOnlyList<string> tokens) => string.Concat(tokens);
}
