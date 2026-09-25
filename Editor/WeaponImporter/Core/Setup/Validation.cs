#nullable enable annotations

using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Setup;

public enum CheckSeverity { Ok, Info, Warning, Error }

/// <summary>One validation line: "✓ Skeleton" or "! Left wrist penetration detected".</summary>
public sealed record CheckResult
{
    public required string Name { get; init; }
    public required CheckSeverity Severity { get; init; }
    public string Message { get; init; } = "";

    /// <summary>A safe automatic fix, when one exists.</summary>
    public Action? AutoFix { get; init; }
    public string AutoFixLabel { get; init; } = "Auto Fix";

    public bool NeedsAttention => Severity >= CheckSeverity.Warning;
}

/// <summary>Everything validation can look at; engine-side facts are optional.</summary>
public sealed record ValidationInput
{
    public required WeaponSetup Setup { get; init; }
    public WeaponAnalysis? Analysis { get; init; }
    public GripSolution? Grip { get; init; }

    /// <summary>Compile errors captured by the editor (file -> messages).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? CompileErrors { get; init; }

    /// <summary>Asset references the editor could not resolve.</summary>
    public IReadOnlyList<string> MissingReferences { get; init; } = Array.Empty<string>();

    /// <summary>Clip names present in the compiled model (null when not compiled yet).</summary>
    public IReadOnlyCollection<string>? CompiledSequences { get; init; }

    /// <summary>Problems found by replaying the grip over the character's actions (role, normalized time, message).</summary>
    public IReadOnlyList<(AnimationRole Role, float Time, string Message)> ActionIssues { get; init; } = Array.Empty<(AnimationRole, float, string)>();

    /// <summary>Called by auto fixes that change the setup, so the editor can re-solve.</summary>
    public Action? Changed { get; init; }
}

public static class Validation
{
    public const float SeverePenetration = 0.45f;

    public static List<CheckResult> Run(ValidationInput input)
    {
        var s = input.Setup;
        var a = input.Analysis;
        var results = new List<CheckResult>();
        void Add(string name, CheckSeverity sev, string msg = "", Action? fix = null, string fixLabel = "Auto Fix")
            => results.Add(new CheckResult { Name = name, Severity = sev, Message = msg, AutoFix = fix, AutoFixLabel = fixLabel });
        void Changed() => input.Changed?.Invoke();

        // Asset compiles.
        if (input.CompileErrors is { } errors)
        {
            if (errors.Count == 0)
                Add("Compiles", CheckSeverity.Ok);
            else
                foreach (var (file, msgs) in errors)
                    Add("Compiles", CheckSeverity.Error, $"{Path.GetFileName(file)}: {msgs.FirstOrDefault() ?? "compile failed"}");
        }

        // Skeleton / root.
        if (a is not null)
        {
            var skeleton = a.Asset.Skeleton;
            if (skeleton.Count == 0)
                Add("Skeleton", CheckSeverity.Error, "The model has no bones.");
            else if (skeleton.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                Add("Skeleton", CheckSeverity.Warning, "Bone names differ only by case; the engine may merge them.");
            else
                Add("Skeleton", CheckSeverity.Ok, $"{skeleton.Count} bones");

            if (string.IsNullOrEmpty(s.RootBone) || skeleton.IndexOf(s.RootBone) < 0)
                Add("Root", CheckSeverity.Error, $"Root bone '{s.RootBone}' is missing.", () => { s.RootBone = skeleton[a.RootBone].Name; Changed(); });
            else
                Add("Root", CheckSeverity.Ok, s.RootBone);

            var (min, max) = WeaponTypes.TypicalLength(s.Type);
            if (a.Length < min * 0.5f || a.Length > max * 2f)
                Add("Scale", CheckSeverity.Warning, $"{a.Length:0.#} in is unusual for a {WeaponTypes.Label(s.Type).ToLowerInvariant()}.");
        }

        // Animations.
        var clipNames = a?.Asset.Clips.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        var broken = s.WeaponAnimations.Where(kv => !string.IsNullOrEmpty(kv.Value.Clip) && a is not null && !clipNames.Contains(kv.Value.Clip)).ToList();
        foreach (var (role, binding) in broken)
            Add("Animations", CheckSeverity.Error, $"{AnimationRoles.Label(role)} uses '{binding.Clip}', which the model doesn't have.", () => { s.WeaponAnimations.Remove(role); Changed(); }, "Remove");
        if (input.CompiledSequences is { } compiled)
            foreach (var (role, binding) in s.WeaponAnimations.Where(kv => !compiled.Contains(EngineNames.Sequence(kv.Value.Clip))))
                Add("Animations", CheckSeverity.Error, $"{AnimationRoles.Label(role)} ('{binding.Clip}') did not compile into the model.");
        if (WeaponTypes.IsFirearm(s.Type) && a is { Asset.Clips.Count: > 0 })
        {
            var missing = AnimationRoles.Required
                .Where(r => !s.WeaponAnimations.ContainsKey(r) && !(AnimationRoles.Fallback(r) is { } f && s.WeaponAnimations.ContainsKey(f)))
                // A shell-by-shell reload is a reload.
                .Where(r => !(r == AnimationRole.Reload && s.WeaponAnimations.ContainsKey(AnimationRole.ReloadInsert)))
                .ToList();
            if (missing.Count > 0)
                Add("Animations", CheckSeverity.Warning, $"No {string.Join(", ", missing.Select(AnimationRoles.Label))} animation; the character's own animation plays instead.");
            else if (broken.Count == 0)
                Add("Animations", CheckSeverity.Ok, $"{s.WeaponAnimations.Count} mapped");
        }
        else if (broken.Count == 0)
        {
            Add("Animations", CheckSeverity.Ok, s.WeaponAnimations.Count == 0 ? "static weapon" : $"{s.WeaponAnimations.Count} mapped");
        }

        // Grips.
        if (!s.Primary.Enabled)
            Add("Right grip", CheckSeverity.Error, "No firing-hand grip.");
        else if (input.Grip is { } g)
        {
            var q = g.RightQuality;
            if (q.PalmPenetration > SeverePenetration || q.MaxPenetration > SeverePenetration * 1.5f)
                Add("Right grip", CheckSeverity.Warning, $"Right hand passes {MathF.Max(q.PalmPenetration, q.MaxPenetration):0.0} in into the weapon.");
            else if (q.TouchingFingers < 2)
                Add("Right grip", CheckSeverity.Warning, "Right fingers barely touch the grip.");
            else if (g.RightWristBend > GripSolver.MaxWristBend)
                Add("Right grip", CheckSeverity.Warning, $"Right wrist bends {g.RightWristBend:0}° against the forearm. Try Wrist vs aim or pick the grip lower.");
            else
                Add("Right grip", CheckSeverity.Ok, s.Primary.Manual ? "adjusted" : s.Primary.Reason);
        }
        else
            Add("Right grip", CheckSeverity.Ok, s.Primary.Reason);

        var wantsSupport = s.UseSupportHand && WeaponTypes.TwoHanded(s.Type);
        if (wantsSupport && s.Support is null)
            Add("Left grip", CheckSeverity.Warning, "No support-hand area found. Pick one on the weapon, or turn the support hand off.", () => { s.UseSupportHand = false; Changed(); }, "One-handed");
        else if (s.UseSupportHand && s.Support is not null && input.Grip is { Left: not null } lg)
        {
            var q = lg.LeftQuality!;
            if (!lg.LeftReach.Reached && lg.LeftReach.Shortfall > s.Ik.ReachSlack)
                Add("Left grip", CheckSeverity.Warning, $"Left hand can't reach the support grip ({lg.LeftReach.Shortfall:0.0} in short).");
            else if (q.PalmPenetration > SeverePenetration)
                Add("Left grip", CheckSeverity.Warning, "Left wrist penetration detected.");
            else if (lg.LeftWristBend > GripSolver.MaxWristBend)
                Add("Left grip", CheckSeverity.Warning, $"Left wrist bends {lg.LeftWristBend:0}° against the forearm.");
            else if (q.HandOverlap > 0.3f)
                Add("Left grip", CheckSeverity.Warning, "Left hand overlaps the right hand.");
            else if (q.MaxPenetration > SeverePenetration * 1.5f)
                Add("Left grip", CheckSeverity.Warning, $"Left fingers pass {q.MaxPenetration:0.0} in into the weapon.");
            else
                Add("Left grip", CheckSeverity.Ok, s.Support.Manual ? "adjusted" : s.Support.Reason);
        }
        else if (s.UseSupportHand && s.Support is not null)
            Add("Left grip", CheckSeverity.Ok, s.Support.Reason);

        // Keep-clear areas (muzzle, blade): no hand may hold them.
        if (a is not null)
        {
            var forbidden = Grip.GripRegions.Of(a).Where(r => r.Kind == Grip.GripRegionKind.Forbidden).ToList();
            foreach (var (name, grip) in new[] { ("Right grip", (GripSetup?)s.Primary), ("Left grip", s.UseSupportHand ? s.Support : null) })
                if (grip is not null && forbidden.FirstOrDefault(r => r.Contains(V.Of(grip.Contact), 0.25f)) is { } area)
                    Add(name, CheckSeverity.Warning, $"The {(name.StartsWith("Right") ? "right" : "left")} hand holds the {area.Reason}, which should stay clear.");
        }

        // Hands over the animations.
        foreach (var group in input.ActionIssues.GroupBy(i => i.Role))
        {
            var first = group.OrderBy(i => i.Time).First();
            Add("Hands in " + AnimationRoles.Label(group.Key), CheckSeverity.Warning, $"{first.Message} at {first.Time * 100:0}% of the animation.");
        }

        // Muzzle / eject.
        if (WeaponTypes.IsFirearm(s.Type))
        {
            if (s.Muzzle is null)
                Add("Muzzle", CheckSeverity.Error, "No muzzle point.", a?.Muzzle is { } m ? () => { s.Muzzle = AutoSetup.Point(a, m); Changed(); } : null, "Use detected");
            else if (a is not null && a.Asset.Skeleton.IndexOf(s.Muzzle.Bone) < 0)
                Add("Muzzle", CheckSeverity.Error, $"Muzzle bone '{s.Muzzle.Bone}' is missing.", () => { s.Muzzle.Bone = a.Asset.Skeleton[a.RootBone].Name; Changed(); });
            else
                Add("Muzzle", s.Muzzle.Confidence < 0.5f && !s.Muzzle.Manual ? CheckSeverity.Info : CheckSeverity.Ok, s.Muzzle.Manual ? "placed" : "detected");
            if (s.Type is not (WeaponType.Revolver or WeaponType.Launcher) && s.Eject is null)
                Add("Shell eject", CheckSeverity.Info, "No ejection point; shells won't spawn.");
        }

        // Events.
        foreach (var e in s.Events.Where(e => e.Time < 0f || e.Time > 1f || !s.WeaponAnimations.ContainsKey(e.Role)))
        {
            var ev = e;
            Add("Events", CheckSeverity.Warning, $"{WeaponEvent.Label(e.Kind)} on {AnimationRoles.Label(e.Role)} has no clip to live on.", () => { s.Events.Remove(ev); Changed(); }, "Remove");
        }
        foreach (var dup in s.Events.GroupBy(e => (e.Role, e.Kind, MathF.Round(e.Time, 3))).Where(g => g.Count() > 1))
        {
            var extra = dup.Skip(1).ToList();
            Add("Events", CheckSeverity.Info, $"{WeaponEvent.Label(dup.Key.Kind)} is listed twice on {AnimationRoles.Label(dup.Key.Role)}.", () => { foreach (var x in extra) s.Events.Remove(x); Changed(); }, "Remove duplicate");
        }

        // References.
        foreach (var missing in input.MissingReferences)
            Add("References", CheckSeverity.Error, $"Missing: {missing}");

        return results;
    }

    /// <summary>Only the lines that need a person, in order of severity.</summary>
    public static IEnumerable<CheckResult> Problems(IEnumerable<CheckResult> results)
        => results.Where(r => r.NeedsAttention).OrderByDescending(r => r.Severity);
}

/// <summary>Names as the engine sees them (ModelDoc sanitises sequence and bone names).</summary>
public static class EngineNames
{
    public static string Sanitize(string name)
    {
        var raw = name;
        var sep = raw.LastIndexOf('|');
        if (sep >= 0 && sep < raw.Length - 1)
            raw = raw[(sep + 1)..];
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "anim" : s;
    }

    /// <summary>Sequence name the importer gives a source clip in the generated model.</summary>
    public static string Sequence(string clip) => Sanitize(clip).ToLowerInvariant();

    public static string Bone(string bone)
    {
        var hash = bone.IndexOf('#');
        if (hash >= 0)
            bone = bone[..hash];
        return new string(bone.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
    }
}
