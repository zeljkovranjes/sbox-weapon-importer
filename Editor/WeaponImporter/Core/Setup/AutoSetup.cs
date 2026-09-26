#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Hands;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Setup;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// One-click setup: turns an analysis into a complete <see cref="WeaponSetup"/> with sensible
/// defaults, keeping anything the user already marked manual in <paramref name="previous"/>.
/// </summary>
public static class AutoSetup
{
    /// <param name="stock">The installed stock third-person sequences (none when not installed).</param>
    public static WeaponSetup Build(WeaponAnalysis a, WeaponSetup? previous = null, IReadOnlyCollection<string>? stock = null)
    {
        var s = previous ?? new WeaponSetup();
        s.Name = string.IsNullOrEmpty(s.Name) || s.Name == "weapon" ? a.Asset.Name : s.Name;
        s.Source = string.IsNullOrEmpty(s.Source) ? a.Asset.SourcePath : s.Source;
        if (!s.TypeManual)
            s.Type = a.Type.Type;
        if (!s.OrientationManual)
        {
            s.ModelRotationQ = a.ModelToCanonical;
            s.Scale = a.Scale;
        }
        s.ReferenceClip = a.ReferenceClip;
        s.RootBone = a.Asset.Skeleton[a.RootBone].Name;

        // Parts: keep manual ones, refresh the rest.
        var manualParts = s.Parts.Where(p => p.Manual).ToList();
        s.Parts = manualParts.Concat(a.Parts.Where(p => manualParts.All(m => m.Kind != p.Kind)).Select(p => new PartSetup
        {
            Kind = p.Kind,
            Bone = p.Bone,
            Meshes = p.Meshes.ToList(),
            Confidence = p.Confidence,
            Center = V.A(p.Center),
        })).ToList();

        if (s.Muzzle is not { Manual: true })
            s.Muzzle = a.Muzzle is null ? null : Point(a, a.Muzzle);
        if (s.Eject is not { Manual: true })
            s.Eject = a.Eject is null || s.Type is WeaponType.Revolver or WeaponType.Melee or WeaponType.Launcher ? null : Point(a, a.Eject);

        if (!s.Primary.Manual && a.Primary is { } primary)
            s.Primary = Grip(primary);
        if (s.Support is not { Manual: true })
            s.Support = a.Support is { } support ? Grip(support) : null;
        // One copy in each hand: the left hand holds its own, not the right one's.
        if (!s.DualManual)
            s.Dual = a.Dual;
        s.UseSupportHand = s.Support is not null && (WeaponTypes.TwoHanded(s.Type) || s.Type == WeaponType.Melee) && !s.Dual;

        // Weapon clips per role.
        foreach (var role in AnimationRoles.All)
        {
            if (s.WeaponAnimations.TryGetValue(role, out var existing) && existing.Manual)
                continue;
            if (a.AssignedAnimations.TryGetValue(role, out var guess))
                s.WeaponAnimations[role] = new AnimationBinding
                {
                    Clip = guess.Animation,
                    Confidence = guess.Confidence,
                    Variants = a.AnimationVariants.TryGetValue(role, out var more) ? more.ToList() : new List<string>(),
                };
            else
                s.WeaponAnimations.Remove(role);
        }

        // Third person: the character's animgraph handles every role unless told otherwise.
        foreach (var role in AnimationRoles.All)
            if (!s.ThirdPerson.TryGetValue(role, out var tp) || !tp.Manual)
                s.ThirdPerson[role] = new CharacterAnimation { Source = CharacterAnimationSource.Graph };

        // Stock animations, when installed: a hold and actions suited to the weapon.
        if (!s.StockStyleManual)
            s.StockStyle = StockThirdPerson.Suggest(s.Type, StockThirdPerson.NameOf(a.Asset), a.Length, s.UseSupportHand && s.Support is not null, s.Dual);
        if (stock is { Count: > 0 })
            StockThirdPerson.Apply(s, s.StockStyle, stock);

        DefaultContacts(s, a);
        DetectEvents(s, a);
        return s;
    }

    public static PointSetup Point(WeaponAnalysis a, PointDetection p)
    {
        // Stored in model units relative to the bone; convert canonical offsets back.
        var bone = a.Asset.Skeleton.IndexOf(p.Bone);
        var local = p.Local;
        var scale = a.Scale > 1e-6f ? a.Scale : 1f;
        return new PointSetup
        {
            Bone = p.Bone,
            Position = V.A(local.Pos / scale),
            Rotation = V.A(local.Rot),
            Canonical = V.A(p.Model.Pos),
            Confidence = p.Confidence,
            Manual = p.Manual,
        };
    }

    public static GripSetup Grip(GripCandidate g) => new()
    {
        Style = g.Style,
        Contact = V.A(g.Surface.Contact),
        Normal = V.A(g.Surface.Normal),
        Axis = V.A(g.Surface.Axis),
        Confidence = g.Confidence,
        Reason = g.Reason,
        Manual = g.Manual,
    };

    /// <summary>Re-measures a stored grip on the geometry (after edits or a template copy).</summary>
    public static GripCandidate? Candidate(WeaponAnalysis a, GripSetup g)
    {
        if (!g.Enabled)
            return null;
        var contact = V.Of(g.Contact);
        var surface = SurfaceProbe.FromPoint(a.WeaponBvh, contact + V.Of(g.Normal) * 0.05f, V.Of(g.Axis));
        if (surface is null)
            return null;
        // The stored normal was measured robustly (from inside the part, or facing the camera
        // for a click); keep it and only take the fresh contact/extent measurements.
        var normal = V.Of(g.Normal);
        if (normal.LengthSquared() > 0.5f)
            surface = surface with { Normal = Vector3.Normalize(normal) };
        var axis = V.Of(g.Axis);
        if (axis.LengthSquared() > 0.5f)
        {
            var n = surface.Normal;
            var projected = axis - n * Vector3.Dot(axis, n);
            if (projected.LengthSquared() > 1e-4f)
                surface = surface with { Axis = Vector3.Normalize(projected) };
        }
        return new GripCandidate { Surface = surface, Style = g.Style, Confidence = g.Confidence, Reason = g.Reason, Manual = g.Manual };
    }

    /// <summary>
    /// Support hand stays on through idle and fire, lets go for reloads and melee, and the
    /// bolt/pump hand follows the part it works.
    /// </summary>
    public static void DefaultContacts(WeaponSetup s, WeaponAnalysis a)
    {
        void Set(AnimationRole role, params ContactKey[] keys)
        {
            if (s.Contacts.TryGetValue(role, out var existing) && existing.Keys.Any() && !existing.Generated)
                return; // user or template already defined it
            s.Contacts[role] = new ContactTrack { Keys = keys.ToList(), Generated = true };
        }
        if (!s.UseSupportHand)
            return;
        var reloadRelease = s.Type is WeaponType.Pistol or WeaponType.Revolver ? 0.12f : 0.18f;
        var reloadReturn = s.Type is WeaponType.Pistol or WeaponType.Revolver ? 0.82f : 0.86f;
        foreach (var role in new[] { AnimationRole.Reload, AnimationRole.TacticalReload, AnimationRole.EmptyReload })
            Set(role,
                new ContactKey { Hand = Side.Left, Time = reloadRelease, State = ContactState.Released, Blend = 0.3f },
                new ContactKey { Hand = Side.Left, Time = reloadReturn, State = ContactState.Locked, Blend = 0.3f });
        // Shell-by-shell reloads: the support hand loads the shells, then takes the grip back.
        Set(AnimationRole.ReloadStart, new ContactKey { Hand = Side.Left, Time = 0.15f, State = ContactState.Released, Blend = 0.25f });
        Set(AnimationRole.ReloadInsert, new ContactKey { Hand = Side.Left, Time = 0f, State = ContactState.Released, Blend = 0f });
        Set(AnimationRole.ReloadEnd, new ContactKey { Hand = Side.Left, Time = 0f, State = ContactState.Released, Blend = 0f },
            new ContactKey { Hand = Side.Left, Time = 0.7f, State = ContactState.Locked, Blend = 0.25f });
        Set(AnimationRole.Melee, new ContactKey { Hand = Side.Left, Time = 0.05f, State = ContactState.Released, Blend = 0.1f },
            new ContactKey { Hand = Side.Left, Time = 0.85f, State = ContactState.Locked, Blend = 0.2f });
        Set(AnimationRole.Sprint, new ContactKey { Hand = Side.Left, Time = 0f, State = ContactState.Released, Blend = 0.2f });
        Set(AnimationRole.Holster, new ContactKey { Hand = Side.Left, Time = 0f, State = ContactState.Released, Blend = 0.15f });
        Set(AnimationRole.Draw, new ContactKey { Hand = Side.Left, Time = 0f, State = ContactState.Released, Blend = 0f },
            new ContactKey { Hand = Side.Left, Time = 0.7f, State = ContactState.Locked, Blend = 0.2f });
        if (s.Type == WeaponType.Shotgun && a.Part(PartKind.Pump) is { Bone: { Length: > 0 } pumpBone })
            Set(AnimationRole.Bolt, new ContactKey { Hand = Side.Left, Time = 0f, State = ContactState.Locked, Blend = 0f, Follow = pumpBone });
    }

    /// <summary>
    /// Events from part motion: magazine detach/insert from the magazine leaving and returning,
    /// bolt pull/release from its travel, fire effects on the first frame of fire clips.
    /// </summary>
    public static void DetectEvents(WeaponSetup s, WeaponAnalysis a)
    {
        s.Events.RemoveAll(e => !e.Manual);
        void Add(AnimationRole role, WeaponEventKind kind, float time)
        {
            if (s.Events.Any(e => e.Role == role && e.Kind == kind))
                return;
            s.Events.Add(new WeaponEvent { Role = role, Kind = kind, Time = Math.Clamp(time, 0f, 1f) });
        }

        foreach (var role in new[] { AnimationRole.Fire, AnimationRole.AdsFire, AnimationRole.FireEmpty })
        {
            if (!WeaponTypes.IsFirearm(s.Type))
                break;
            // Events live on clips; roles the weapon has no clip for get none.
            if (!s.WeaponAnimations.ContainsKey(role))
                continue;
            Add(role, WeaponEventKind.Fire, 0f);
            Add(role, WeaponEventKind.MuzzleFlash, 0f);
            if (s.Eject is not null)
            {
                var bolt = Bone(a, PartKind.Slide) ?? Bone(a, PartKind.Bolt);
                var clip = Clip(a, s, role);
                var peak = bolt is { } b && clip is not null ? PeakTravel(a.Asset, clip, b) : 0.15f;
                Add(role, WeaponEventKind.ShellEject, peak);
            }
        }

        foreach (var role in new[] { AnimationRole.Reload, AnimationRole.TacticalReload, AnimationRole.EmptyReload })
        {
            var clip = Clip(a, s, role);
            if (clip is null)
                continue;
            if (Bone(a, PartKind.Magazine) is { } mag)
            {
                var (leave, back) = LeaveAndReturn(a.Asset, clip, mag, 0.35f);
                if (leave is { } l) Add(role, WeaponEventKind.MagazineDetach, l);
                if (back is { } r) Add(role, WeaponEventKind.MagazineInsert, r);
            }
            if (role == AnimationRole.EmptyReload || role == AnimationRole.Reload)
                if ((Bone(a, PartKind.Bolt) ?? Bone(a, PartKind.ChargingHandle) ?? Bone(a, PartKind.Slide)) is { } bolt)
                {
                    var (pull, release) = LeaveAndReturn(a.Asset, clip, bolt, 0.25f);
                    if (pull is { } p) Add(role, WeaponEventKind.BoltPull, p);
                    if (release is { } r) Add(role, WeaponEventKind.BoltRelease, r);
                }
            Add(role, WeaponEventKind.ReloadComplete, 0.95f);
        }

        if (Clip(a, s, AnimationRole.Bolt) is { } boltClip && (Bone(a, PartKind.Bolt) ?? Bone(a, PartKind.Pump) ?? Bone(a, PartKind.ChargingHandle)) is { } bb)
        {
            var (pull, release) = LeaveAndReturn(a.Asset, boltClip, bb, 0.25f);
            if (pull is { } p) Add(AnimationRole.Bolt, WeaponEventKind.BoltPull, p);
            if (release is { } r) Add(AnimationRole.Bolt, WeaponEventKind.BoltRelease, r);
        }
    }

    private static int? Bone(WeaponAnalysis a, PartKind kind)
        => a.Part(kind) is { Bone: { Length: > 0 } name } && a.Asset.Skeleton.IndexOf(name) is var i && i >= 0 ? i : null;

    private static Clip? Clip(WeaponAnalysis a, WeaponSetup s, AnimationRole role)
        => s.WeaponAnimations.TryGetValue(role, out var b) ? a.Asset.FindClip(b.Clip) : null;

    /// <summary>Normalized times where a bone first moves away from rest by <paramref name="threshold"/> of its peak travel, and settles back.</summary>
    public static (float? Leave, float? Return) LeaveAndReturn(WeaponAsset asset, Clip clip, int bone, float threshold)
    {
        if (clip.FrameCount < 3)
            return (null, null);
        var rest = clip.Frames[0][bone].Pos;
        var travel = clip.Frames.Select(f => Vector3.Distance(f[bone].Pos, rest)).ToArray();
        var peak = travel.Max();
        if (peak < 0.05f)
            return (null, null);
        var limit = peak * threshold;
        var leave = Array.FindIndex(travel, d => d > limit);
        var back = Array.FindLastIndex(travel, d => d > limit);
        float Norm(int f) => f / (float)(clip.FrameCount - 1);
        return (leave >= 0 ? Norm(leave) : null, back >= 0 && back < clip.FrameCount - 1 ? Norm(back + 1) : null);
    }

    private static float PeakTravel(WeaponAsset asset, Clip clip, int bone)
    {
        if (clip.FrameCount < 2)
            return 0f;
        var rest = clip.Frames[0][bone].Pos;
        var best = 0;
        var max = 0f;
        for (var f = 0; f < clip.FrameCount; f++)
        {
            var d = Vector3.Distance(clip.Frames[f][bone].Pos, rest);
            if (d > max) { max = d; best = f; }
        }
        return best / (float)(clip.FrameCount - 1);
    }
}
