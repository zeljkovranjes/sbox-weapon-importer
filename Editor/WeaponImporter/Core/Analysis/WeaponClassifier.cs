#nullable enable annotations

using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Analysis;

/// <summary>
/// Guesses the weapon category from names (file, bones, meshes, clips) and shape (length,
/// bore size, detected parts). The type only picks defaults; users can always override it.
/// </summary>
public static class WeaponClassifier
{
    private static readonly (WeaponType Type, string[] Names)[] NameHints =
    {
        (WeaponType.Revolver, new[] { "revolver", "magnum", "python", "colt", "44", "357", "cylinder", "peacemaker", "rhino" }),
        (WeaponType.Pistol, new[] { "pistol", "handgun", "glock", "deagle", "deserteagle", "eagle", "beretta", "m9", "m1911", "1911", "usp", "p250", "p2000", "fiveseven", "sidearm", "sig", "makarov", "tec9", "cz75" }),
        (WeaponType.Smg, new[] { "smg", "uzi", "mp5", "mp7", "mp9", "p90", "vector", "ump", "mac10", "mac", "thompson", "bizon", "submachine", "pdw", "mp40", "ppsh" }),
        (WeaponType.Shotgun, new[] { "shotgun", "spas", "benelli", "m870", "remington", "nova", "xm1014", "sawedoff", "pump", "doublebarrel", "aa12", "saiga" }),
        (WeaponType.Sniper, new[] { "sniper", "awp", "awm", "barrett", "m82", "l96", "scout", "ssg", "m24", "dragunov", "svd", "kar98", "mosin", "boltaction", "marksman", "dmr", "rifle_sniper" }),
        (WeaponType.Launcher, new[] { "rpg", "launcher", "bazooka", "rocket", "grenadelauncher", "m79", "at4", "javelin", "stinger", "law", "panzerfaust" }),
        (WeaponType.Melee, new[] { "knife", "sword", "katana", "axe", "hatchet", "bat", "crowbar", "machete", "blade", "melee", "club", "wrench", "dagger", "bayonet", "spear", "mace", "pipe" }),
        (WeaponType.Rifle, new[] { "rifle", "ak", "ak47", "ak74", "akm", "m4", "m4a1", "m16", "ar15", "ar", "scar", "famas", "aug", "g36", "galil", "hk416", "assault", "carbine", "fal", "m14" }),
    };

    /// <summary>
    /// Types named in a piece of text. When one alias contains another ("sniperrifle" holds both
    /// "sniper" and "rifle") only the alias the token starts with counts.
    /// </summary>
    private static IEnumerable<WeaponType> NamedTypes(string text)
    {
        var tokens = NameTokens.Split(text);
        var joined = NameTokens.Joined(tokens);
        var hits = new List<(WeaponType Type, string Alias, string Token)>();
        foreach (var (type, names) in NameHints)
            foreach (var n in names)
                foreach (var t in tokens)
                    if (t == n || (n.Length >= 5 && (t.StartsWith(n, StringComparison.Ordinal) || joined.Contains(n, StringComparison.Ordinal))))
                    {
                        hits.Add((type, n, t));
                        break;
                    }
        var kept = hits
            .Where(h => !hits.Any(o => o.Type != h.Type && o.Token == h.Token && o.Token.StartsWith(o.Alias, StringComparison.Ordinal) && !h.Token.StartsWith(h.Alias, StringComparison.Ordinal)))
            .Where(h => !hits.Any(o => o.Type != h.Type && o.Alias.Length > h.Alias.Length && o.Alias.Contains(h.Alias, StringComparison.Ordinal)));
        return kept.Select(h => h.Type).Distinct();
    }

    /// <summary>Type from the file, mesh and bone names alone (used before geometry is trusted).</summary>
    public static WeaponType? FromNames(WeaponAsset asset)
    {
        var scores = new Dictionary<WeaponType, float>();
        var sources = new List<(string, float)> { (asset.Name, 1.6f), (Path.GetFileNameWithoutExtension(asset.SourcePath), 1.6f) };
        sources.AddRange(asset.Mesh.PartNames.Select(n => (n, 0.5f)));
        foreach (var (text, weight) in sources)
            foreach (var type in NamedTypes(text))
                scores[type] = scores.GetValueOrDefault(type) + weight;
        return scores.Count == 0 ? null : scores.OrderByDescending(kv => kv.Value).First().Key;
    }

    public static TypeGuess Classify(WeaponAnalysis a)
    {
        var scores = Enum.GetValues<WeaponType>().ToDictionary(t => t, _ => 0f);
        var reasons = new Dictionary<WeaponType, string>();

        void Score(WeaponType t, float s, string why)
        {
            scores[t] += s;
            if (s > 0f && (!reasons.ContainsKey(t) || s >= 0.5f))
                reasons[t] = why;
        }

        // Names: the file name is the strongest signal, then bones/meshes, then clip names.
        var sources = new List<(string Text, float Weight)> { (a.Asset.Name, 1.6f), (Path.GetFileNameWithoutExtension(a.Asset.SourcePath), 1.6f) };
        sources.AddRange(a.Asset.Mesh.PartNames.Select(n => (n, 0.5f)));
        sources.AddRange(a.Asset.Skeleton.Bones.Where(b => !a.ArmBones.Contains(b.Index)).Select(b => (b.Name, 0.35f)));
        sources.AddRange(a.Asset.Clips.Select(c => (c.Name, 0.25f)));

        // Short aliases ("ar", "ak", "44") must match a whole token.
        foreach (var (text, weight) in sources)
            foreach (var type in NamedTypes(text))
                Score(type, weight, $"name '{text}'");

        // Shape.
        var length = a.Length;
        var bore = a.BoreDiameter;
        var hasMag = a.Part(PartKind.Magazine) is not null;
        var hasTrigger = a.Part(PartKind.Trigger) is not null;
        var hasCylinder = a.Part(PartKind.Cylinder) is not null;
        var hasPump = a.Part(PartKind.Pump) is not null;
        var hasSlide = a.Part(PartKind.Slide) is not null;
        var hasBolt = a.Part(PartKind.Bolt) is not null;
        var hasScope = a.Part(PartKind.Scope) is not null;
        var hasStock = HasStock(a);

        if (hasCylinder) Score(WeaponType.Revolver, 1.2f, "rotating cylinder");
        if (hasSlide) Score(WeaponType.Pistol, 0.8f, "slide");
        if (hasPump) Score(WeaponType.Shotgun, 1.0f, "pump");
        // Loading shell by shell (start / one shell / end clips) is what shotguns do.
        var reloadRoles = a.Asset.Clips.Select(c => AnimationClassifier.Classify(c.Name).Role).ToHashSet();
        if (reloadRoles.Contains(AnimationRole.ReloadInsert) || (reloadRoles.Contains(AnimationRole.ReloadStart) && reloadRoles.Contains(AnimationRole.ReloadEnd)))
            Score(WeaponType.Shotgun, 1.2f, "reloads shell by shell");
        if (hasScope && hasBolt && length > 34f) Score(WeaponType.Sniper, 0.8f, "long, scoped, bolt action");

        if (length is > 4f and < 12f && !hasStock) { Score(WeaponType.Pistol, 0.9f, $"compact ({length:0.#} in)"); Score(WeaponType.Revolver, 0.4f, $"compact ({length:0.#} in)"); }
        if (length is >= 11f and < 27f) Score(WeaponType.Smg, 0.6f, $"{length:0.#} in long");
        if (length is >= 25f and < 42f && hasStock) Score(WeaponType.Rifle, 0.8f, $"{length:0.#} in with a stock");
        if (length is >= 25f and < 42f && !hasStock) Score(WeaponType.Rifle, 0.4f, $"{length:0.#} in long");
        if (length >= 40f) { Score(WeaponType.Sniper, 0.5f, $"very long ({length:0.#} in)"); Score(WeaponType.Launcher, 0.2f, $"very long ({length:0.#} in)"); }
        if (bore > 2.2f && length > 20f) Score(WeaponType.Launcher, 1.0f, $"wide bore ({bore:0.#} in)");
        if (bore is > 0.75f and < 1.6f && length > 24f) Score(WeaponType.Shotgun, 0.3f, "wide barrel");
        if (!hasTrigger && !hasMag && a.Asset.Clips.All(c => !c.Name.Contains("reload", StringComparison.OrdinalIgnoreCase)))
        {
            // Solid, thin shapes without firearm parts read as melee weapons.
            var thin = MathF.Max(a.WeaponBounds.Size.Y, a.WeaponBounds.Size.Z) < length * 0.3f;
            Score(WeaponType.Melee, thin ? 0.7f : 0.35f, "no trigger, magazine or reload");
        }
        if (hasMag && !hasStock && length < 13f) Score(WeaponType.Pistol, 0.3f, "magazine, no stock");

        var ordered = scores.Where(kv => kv.Key != WeaponType.Custom).OrderByDescending(kv => kv.Value).ToArray();
        var best = ordered[0];
        if (best.Value < 0.3f)
            return new TypeGuess(WeaponType.Custom, 0.2f, "not enough evidence", scores);
        var runnerUp = ordered.Length > 1 ? ordered[1].Value : 0f;
        var confidence = Math.Clamp(0.4f + (best.Value - runnerUp) * 0.35f + MathF.Min(best.Value, 2f) * 0.1f, 0.25f, 0.98f);
        return new TypeGuess(best.Key, confidence, reasons.GetValueOrDefault(best.Key, ""), scores);
    }

    /// <summary>A stock: geometry behind the grip region reaching near bore height over several inches.</summary>
    public static bool HasStock(WeaponAnalysis a)
    {
        var p = a.Profile;
        if (a.Length < 16f)
            return false;
        var rearEnd = p.MinX + a.Length * 0.22f;
        var tall = 0;
        var total = 0;
        for (var i = 0; i < p.Count && p.X(i) < rearEnd; i++)
        {
            if (!p.Has(i))
                continue;
            total++;
            if (p.Height(i) > a.BoreDiameter * 2.5f && p.Height(i) > 2.2f)
                tall++;
        }
        return total > 0 && tall > total * 0.5f;
    }
}
