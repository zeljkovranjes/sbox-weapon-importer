#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;

namespace WeaponImporter.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>What to take from a template weapon.</summary>
[Flags]
public enum TemplateParts
{
    None = 0,
    AnimationMappings = 1,
    ThirdPerson = 2,
    Attachments = 4,
    Grips = 8,
    Events = 16,
    Contacts = 32,
    Ik = 64,
    TypeDefaults = 128,
    All = AnimationMappings | ThirdPerson | Attachments | Grips | Events | Contacts | Ik | TypeDefaults,
}

/// <summary>
/// Copies a configured weapon's choices onto a new weapon, adapting them to the new geometry:
/// clips are matched by role rather than name, grips are carried over by their place on the
/// weapon (relative to grip, bore and length) and re-measured on the new surface, event and
/// contact times are normalized so they fit clips of any length.
/// </summary>
public static class TemplateAdapter
{
    public static List<string> Apply(WeaponSetup target, WeaponAnalysis targetAnalysis, WeaponSetup template, TemplateParts parts = TemplateParts.All, WeaponAnalysis? templateAnalysis = null)
    {
        var notes = new List<string>();
        target.Template = template.Name;

        if (parts.HasFlag(TemplateParts.TypeDefaults) && !target.TypeManual)
        {
            target.Type = template.Type;
            target.UseSupportHand = template.UseSupportHand;
            target.IndexOnTrigger = template.IndexOnTrigger;
            notes.Add($"Type set to {template.Type}.");
        }

        if (parts.HasFlag(TemplateParts.AnimationMappings))
        {
            // For each role the template mapped, find the new weapon's clip that plays that role.
            var guesses = targetAnalysis.Animations.ToLookup(g => g.Role);
            foreach (var (role, binding) in template.WeaponAnimations)
            {
                if (target.WeaponAnimations.TryGetValue(role, out var mine) && mine.Manual)
                    continue;
                var exact = targetAnalysis.Asset.FindClip(binding.Clip);
                var byRole = guesses[role].OrderByDescending(g => g.Confidence).FirstOrDefault();
                var clip = exact?.Name ?? byRole?.Animation;
                if (clip is null)
                {
                    notes.Add($"No clip for {AnimationRoles.Label(role)} on the new weapon.");
                    continue;
                }
                target.WeaponAnimations[role] = new AnimationBinding { Clip = clip, Confidence = exact is not null ? 1f : byRole!.Confidence };
            }
        }

        if (parts.HasFlag(TemplateParts.ThirdPerson))
            foreach (var (role, tp) in template.ThirdPerson)
                target.ThirdPerson[role] = new CharacterAnimation { Source = tp.Source, Model = tp.Model, Sequence = tp.Sequence, Manual = tp.Manual };

        if (parts.HasFlag(TemplateParts.Events))
        {
            target.Events.RemoveAll(e => !e.Manual);
            foreach (var e in template.Events)
                if (target.WeaponAnimations.ContainsKey(e.Role))
                    target.Events.Add(new WeaponEvent { Role = e.Role, Kind = e.Kind, Time = e.Time, Manual = e.Manual });
        }

        if (parts.HasFlag(TemplateParts.Contacts))
            foreach (var (role, track) in template.Contacts)
                target.Contacts[role] = new ContactTrack
                {
                    Keys = track.Keys.Select(k => new ContactKey { Hand = k.Hand, Time = k.Time, State = k.State, Blend = k.Blend, Offset = k.Offset?.ToArray(), Follow = MapBone(k.Follow, targetAnalysis) }).ToList(),
                };

        if (parts.HasFlag(TemplateParts.Ik))
            target.Ik = new IkSettings
            {
                WristPreference = template.Ik.WristPreference,
                Smoothing = template.Ik.Smoothing,
                ReachSlack = template.Ik.ReachSlack,
                // Nudges are relative to the old weapon's size; scale them by length ratio.
                WeaponOffsetPosition = V.A(V.Of(template.Ik.WeaponOffsetPosition) * LengthRatio(template, targetAnalysis, templateAnalysis)),
                WeaponOffsetRotation = template.Ik.WeaponOffsetRotation.ToArray(),
            };

        if (parts.HasFlag(TemplateParts.Grips))
        {
            if (template.Primary.Manual && !target.Primary.Manual)
                target.Primary = AdaptGrip(template.Primary, template, targetAnalysis, templateAnalysis, target.Primary, notes, "right");
            if (template.Support is { Manual: true } support && target.Support is not { Manual: true })
                target.Support = AdaptGrip(support, template, targetAnalysis, templateAnalysis, target.Support, notes, "left");
        }

        if (parts.HasFlag(TemplateParts.Attachments))
        {
            // Manual muzzle/eject placements move with the bore; detections on the new weapon are kept otherwise.
            if (template.Muzzle is { Manual: true } && targetAnalysis.Muzzle is not null)
                notes.Add("Muzzle kept from detection on the new barrel.");
        }
        return notes;
    }

    private static string MapBone(string bone, WeaponAnalysis a)
    {
        if (string.IsNullOrEmpty(bone))
            return "";
        if (a.Asset.Skeleton.IndexOf(bone) >= 0)
            return bone;
        // Same kind of part on the new weapon.
        var tokens = NameTokens.Split(bone);
        foreach (var part in a.Parts)
            if (part.Bone.Length > 0 && NameTokens.Split(part.Kind.ToString()).Any(t => tokens.Contains(t)))
                return part.Bone;
        return "";
    }

    private static float LengthRatio(WeaponSetup template, WeaponAnalysis target, WeaponAnalysis? templateAnalysis)
    {
        var old = templateAnalysis?.Length ?? 0f;
        return old > 1e-3f ? target.Length / old : 1f;
    }

    /// <summary>
    /// Moves a grip to the equivalent place: expressed relative to the old weapon's bounds and
    /// bore, mapped into the new weapon's, then snapped onto the nearest real surface.
    /// </summary>
    private static GripSetup AdaptGrip(GripSetup grip, WeaponSetup template, WeaponAnalysis target, WeaponAnalysis? templateAnalysis, GripSetup? fallback, List<string> notes, string which)
    {
        var contact = V.Of(grip.Contact);
        Vector3 mapped;
        if (templateAnalysis is not null)
        {
            var ob = templateAnalysis.WeaponBounds;
            var nb = target.WeaponBounds;
            var rel = (contact - ob.Min) / Vector3.Max(ob.Size, new Vector3(1e-3f));
            mapped = nb.Min + rel * nb.Size;
        }
        else
        {
            mapped = contact;
        }
        var surface = SurfaceProbe.FromPoint(target.WeaponBvh, mapped, V.Of(grip.Axis));
        if (surface is null)
        {
            notes.Add($"Couldn't place the {which} grip from the template; kept the detected one.");
            return fallback ?? grip;
        }
        notes.Add($"The {which} grip was carried over from the template.");
        return new GripSetup
        {
            Style = grip.Style,
            Contact = V.A(surface.Contact),
            Normal = V.A(surface.Normal),
            Axis = V.A(surface.Axis),
            Confidence = 0.8f,
            Reason = "from template",
            Manual = true,
        };
    }
}
