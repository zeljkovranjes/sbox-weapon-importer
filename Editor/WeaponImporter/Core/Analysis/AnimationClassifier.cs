#nullable enable annotations

namespace WeaponImporter.Core.Analysis;

/// <summary>A guess at an animation's role, with how sure the classifier is.</summary>
public sealed record AnimationGuess(string Animation, AnimationRole Role, float Confidence, string Reason)
{
    /// <summary>First-person (view model) or third-person (world / body) clip, when the name says so.</summary>
    public AnimationPerspective Perspective { get; init; }
}

public enum AnimationPerspective { Any, FirstPerson, ThirdPerson }

/// <summary>
/// Maps animation names onto <see cref="AnimationRole"/>s using aliases common across game
/// rigs and marketplace packs (<c>shoot</c>, <c>reload_empty</c>, <c>deploy</c>, <c>lookat</c>,
/// <c>ads_fire</c>, <c>rechamber</c>...). Scoring is token based so prefixes, numbering and
/// casing do not matter.
/// </summary>
public static class AnimationClassifier
{
    private sealed record Rule(AnimationRole Role, string[] Any, string[]? Joined = null, string[]? Requires = null, string[]? Excludes = null, float Weight = 1f);

    private static readonly string[] AimTokens = { "ads", "aim", "aiming", "iron", "ironsight", "ironsights", "sight", "sights", "zoom", "scope" };
    private static readonly string[] AimLoopTokens = { "idle", "loop", "hold", "pose", "static", "aiming" };
    private static readonly string[] AimOutTokens = { "out", "exit", "end", "lower", "leave", "stop", "release" };

    // Order matters only for ties; more specific rules carry higher weights.
    private static readonly Rule[] Rules =
    {
        new(AnimationRole.AdsFire, new[] { "fire", "shoot", "shot", "attack", "firing" }, new[] { "adsfire", "aimfire", "ironfire", "firead", "fireaim", "fireiron", "sightfire", "scopefire" }, Requires: new[] { "ads", "aim", "iron", "ironsight", "ironsights", "sight", "scope", "zoom" }, Weight: 1.35f),
        new(AnimationRole.FireEmpty, new[] { "fire", "shoot", "shot", "attack", "dry", "dryfire" }, new[] { "fireempty", "firelast", "shootempty", "shootlast", "dryfire", "lastshot", "emptyfire" }, Requires: new[] { "empty", "last", "dry", "final" }, Weight: 1.3f),
        new(AnimationRole.Fire, new[] { "fire", "shoot", "shot", "attack", "firing", "recoil", "primary", "burst", "auto", "semi" }, Excludes: new[] { "reload", "select", "mode", "switch" }),

        new(AnimationRole.EmptyReload, new[] { "reload", "rld", "reloading" }, new[] { "reloadempty", "emptyreload", "reloadlong", "reloaddry", "reloadfull" }, Requires: new[] { "empty", "dry", "long", "full", "outofammo" }, Weight: 1.3f),
        new(AnimationRole.TacticalReload, new[] { "reload", "rld", "reloading" }, new[] { "reloadtac", "tacreload", "reloadtactical", "tacticalreload", "reloadshort", "reloadpartial" }, Requires: new[] { "tac", "tactical", "short", "partial", "fast", "speed" }, Weight: 1.3f),
        new(AnimationRole.Reload, new[] { "reload", "rld", "reloading", "magswap", "magchange" }, Excludes: new[] { "start", "end", "loop", "insert" }),

        new(AnimationRole.Unjam, new[] { "unjam", "clear", "clearjam", "fixjam", "malfunctionclear", "tapack", "remedy" }, new[] { "unjam", "clearjam", "fixjam", "jamclear", "jamfix" }, Weight: 1.3f),
        new(AnimationRole.Jam, new[] { "jam", "jammed", "malfunction", "misfire", "stovepipe" }, Excludes: new[] { "clear", "fix", "un" }),
        new(AnimationRole.Bolt, new[] { "bolt", "rechamber", "chamber", "pump", "charge", "charging", "cock", "cocking", "rack", "cycle", "lever", "slide" }, Excludes: new[] { "reload", "fire", "shoot", "idle" }),

        new(AnimationRole.Draw, new[] { "draw", "deploy", "equip", "pullout", "raise", "unholster", "takeout", "ready", "select", "pickup" }, new[] { "firstdraw", "drawfirst", "pullout", "takeout" }, Excludes: new[] { "reload" }),
        new(AnimationRole.Holster, new[] { "holster", "putaway", "unequip", "lower", "stow", "putdown", "deselect", "hide" }, new[] { "putaway", "putdown" }, Excludes: new[] { "unholster" }),
        new(AnimationRole.Inspect, new[] { "inspect", "lookat", "examine", "check", "fidget", "admire", "showoff", "look" }, new[] { "lookat", "checkmag", "magcheck", "showoff" }),

        new(AnimationRole.Sprint, new[] { "sprint", "run", "running", "dash" }),
        new(AnimationRole.Walk, new[] { "walk", "walking", "move", "moving", "jog", "strafe", "bob" }),
        // Aiming, any naming style: raising the sights ("Aim_In", "ADS", "IronIn", "Zoom_Enter"),
        // the held loop ("Aim_Idle", "ADS_Loop"), lowering them ("Aim_Out", "ADS_Exit", "Unaim").
        new(AnimationRole.Ads, AimTokens.Concat(new[] { "adsin", "aimin" }).ToArray(), new[] { "aimin", "adsin", "ironin", "zoomin", "sightin", "scopein", "aimenter", "adsenter", "aimstart", "adsstart" }, Excludes: AimOutTokens.Concat(AimLoopTokens).Concat(new[] { "fire", "shoot", "unaim", "unads" }).ToArray()),
        new(AnimationRole.AdsIdle, AimLoopTokens, new[] { "aimidle", "adsidle", "aimloop", "adsloop", "ironidle", "zoomidle", "aimhold", "adshold", "aimingidle" }, Requires: AimTokens, Excludes: AimOutTokens.Concat(new[] { "fire", "shoot" }).ToArray(), Weight: 1.3f),
        new(AnimationRole.AdsOut, AimOutTokens.Concat(new[] { "unaim", "unads", "adsout", "aimout" }).ToArray(), new[] { "aimout", "adsout", "ironout", "zoomout", "sightout", "scopeout", "unaim", "unads", "aimexit", "adsexit", "aimend", "adsend" }, Requires: AimTokens.Concat(new[] { "unaim", "unads", "adsout", "aimout" }).ToArray(), Excludes: new[] { "fire", "shoot" }, Weight: 1.3f),
        new(AnimationRole.Melee, new[] { "melee", "bash", "stab", "slash", "swing", "hit", "knife", "punch", "butt", "strike", "attackmelee" }, new[] { "meleeattack" }, Weight: 1.1f),
        new(AnimationRole.Idle, new[] { "idle", "rest", "hold", "static", "pose", "bind", "stand", "base", "default" }, Excludes: new[] { "to", "fire", "reload" }, Weight: 0.9f),
    };

    private static readonly string[] FirstPersonTokens = { "fp", "1p", "vm", "v", "view", "viewmodel", "arms", "firstperson", "fps" };
    private static readonly string[] ThirdPersonTokens = { "tp", "3p", "wm", "w", "world", "worldmodel", "thirdperson", "tps", "body", "player", "citizen" };

    /// <summary>Classifies one animation name.</summary>
    public static AnimationGuess Classify(string name, AnimationMotionHint? motion = null)
    {
        var tokens = NameTokens.Split(name);
        var joined = NameTokens.Joined(tokens);
        var perspective = NameTokens.Has(tokens, ThirdPersonTokens) ? AnimationPerspective.ThirdPerson
            : NameTokens.Has(tokens, FirstPersonTokens) ? AnimationPerspective.FirstPerson
            : AnimationPerspective.Any;

        AnimationRole best = AnimationRole.Unknown;
        float bestScore = 0f;
        string reason = "no matching name";

        foreach (var rule in Rules)
        {
            var score = 0f;
            string why = "";
            if (rule.Joined is { } joinedAliases && joinedAliases.Any(j => joined.Contains(j, StringComparison.Ordinal)))
            {
                score = 1f;
                why = $"name contains '{joinedAliases.First(j => joined.Contains(j, StringComparison.Ordinal))}'";
            }
            else if (NameTokens.Has(tokens, rule.Any))
            {
                if (rule.Requires is { } req && !NameTokens.Has(tokens, req))
                    continue;
                score = rule.Requires is null ? 0.8f : 0.95f;
                why = $"'{tokens.First(t => rule.Any.Any(a => t == a || (a.Length >= 4 && t.StartsWith(a, StringComparison.Ordinal))))}' in name";
            }
            else
            {
                continue;
            }

            if (rule.Excludes is { } ex && NameTokens.Has(tokens, ex))
                score *= 0.45f;
            score *= rule.Weight;

            if (score > bestScore)
            {
                bestScore = score;
                best = rule.Role;
                reason = why;
            }
        }

        // Motion evidence breaks ties and rescues meaningless names ("anim_03").
        if (motion is { } m)
        {
            var (role, conf, why) = FromMotion(m);
            if (best == AnimationRole.Unknown && role != AnimationRole.Unknown)
            {
                best = role;
                bestScore = conf;
                reason = why;
            }
            else if (role == best && role != AnimationRole.Unknown)
            {
                bestScore = MathF.Min(1f, bestScore + 0.1f);
            }
        }

        return new AnimationGuess(name, best, MathF.Min(1f, bestScore), reason) { Perspective = perspective };
    }

    /// <summary>
    /// Classifies a set of animations and resolves duplicates: each role goes to the clip with
    /// the highest confidence, preferring shorter base names ("reload" over "reload_02").
    /// </summary>
    public static IReadOnlyDictionary<AnimationRole, AnimationGuess> Assign(IEnumerable<AnimationGuess> guesses, AnimationPerspective prefer = AnimationPerspective.Any)
    {
        var result = new Dictionary<AnimationRole, AnimationGuess>();
        foreach (var group in guesses.Where(g => g.Role != AnimationRole.Unknown).GroupBy(g => g.Role))
        {
            var pick = group
                .OrderByDescending(g => g.Confidence + (prefer != AnimationPerspective.Any && g.Perspective == prefer ? 0.15f : 0f) - (prefer != AnimationPerspective.Any && g.Perspective != AnimationPerspective.Any && g.Perspective != prefer ? 0.3f : 0f))
                .ThenBy(g => g.Animation.Count(char.IsDigit))
                .ThenBy(g => g.Animation.Length)
                .First();
            result[group.Key] = pick;
        }

        // A lone "reload" that is actually the empty one leaves Reload itself free: promote.
        if (!result.ContainsKey(AnimationRole.Reload))
        {
            if (result.TryGetValue(AnimationRole.TacticalReload, out var tac))
                result[AnimationRole.Reload] = tac with { Role = AnimationRole.Reload, Confidence = tac.Confidence * 0.9f, Reason = "tactical reload used as reload" };
            else if (result.TryGetValue(AnimationRole.EmptyReload, out var empty))
                result[AnimationRole.Reload] = empty with { Role = AnimationRole.Reload, Confidence = empty.Confidence * 0.85f, Reason = "empty reload used as reload" };
        }
        if (!result.ContainsKey(AnimationRole.Fire) && result.TryGetValue(AnimationRole.AdsFire, out var adsFire))
            result[AnimationRole.Fire] = adsFire with { Role = AnimationRole.Fire, Confidence = adsFire.Confidence * 0.8f, Reason = "ADS fire used as fire" };
        return result;
    }

    private static (AnimationRole, float, string) FromMotion(AnimationMotionHint m)
    {
        if (m.MagazineTravel > 2f && m.Duration > 1f)
            return (AnimationRole.Reload, 0.6f, "magazine leaves the weapon");
        if (m.BoltTravel > 0.3f && m.Duration < 0.6f)
            return (AnimationRole.Fire, 0.55f, "short clip with slide/bolt cycling");
        if (m.BoltTravel > 0.3f && m.Duration >= 0.6f)
            return (AnimationRole.Bolt, 0.5f, "bolt cycles without magazine change");
        if (m.RootTravel < 0.05f && m.Duration > 0.5f && m.MaxBoneMotion < 0.1f)
            return (AnimationRole.Idle, 0.5f, "almost no motion");
        return (AnimationRole.Unknown, 0f, "motion inconclusive");
    }
}

/// <summary>Motion measurements used when a name alone is not enough.</summary>
public readonly record struct AnimationMotionHint(float Duration, float MagazineTravel, float BoltTravel, float RootTravel, float MaxBoneMotion);
