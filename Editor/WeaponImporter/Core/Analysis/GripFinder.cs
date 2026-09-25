#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core.Analysis;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Finds where hands belong from the weapon's own geometry: the pistol grip hangs below the
/// receiver behind the trigger, the handguard runs under the barrel ahead of the magazine,
/// melee handles are the round, narrow end. Contacts are then probed on the real surface.
/// Model space: +X forward, +Y left, +Z up.
/// </summary>
public static class GripFinder
{
    /// <summary>
    /// A right palm sits on the right side of a pistol grip (heel on the backstrap), so the
    /// fingers point forward around it and the wrist stays straight behind the grip.
    /// </summary>
    private const float PalmWrapDegrees = 70f;

    public static void FindGrips(WeaponAnalysis a)
    {
        if (a.WeaponBvh.IsEmpty)
            return;

        if (a.Type.Type == WeaponType.Melee)
        {
            FindMeleeGrips(a);
            return;
        }

        a.Primary = FindPistolGrip(a) ?? BarGrip(a) ?? FallbackPrimary(a);
        foreach (var c in SupportCandidates(a))
            a.SupportAreas.Add(c);
        a.Support = a.SupportAreas.FirstOrDefault();
    }

    // ------------------------------------------------------------------ primary

    /// <summary>Silhouette of the weapon without parts that also hang down (magazine, foregrip, pump).</summary>
    private static ShapeProfile FrameProfile(WeaponAnalysis a)
    {
        var excluded = new HashSet<int>();
        foreach (var part in a.Parts.Where(p => p.Kind is PartKind.Magazine or PartKind.Foregrip or PartKind.Pump))
            foreach (var t in PartTriangles(a, part))
                excluded.Add(t);
        var tris = a.WeaponTriangles.Where(t => !excluded.Contains(t)).ToArray();
        return tris.Length > 0 ? ShapeProfile.Build(a.Asset.Mesh, tris) : a.Profile;
    }

    public static IEnumerable<int> PartTriangles(WeaponAnalysis a, PartDetection part)
    {
        var mesh = a.Asset.Mesh;
        var bone = part.Bone != "" ? a.Asset.Skeleton.IndexOf(part.Bone) : -1;
        var meshParts = new HashSet<int>(part.Meshes.Select(n => mesh.PartNames.ToList().IndexOf(n)).Where(i => i >= 0));
        HashSet<int>? bones = null;
        if (bone >= 0)
        {
            bones = new HashSet<int> { bone };
            for (var b = 0; b < a.Asset.Skeleton.Count; b++)
                if (a.Asset.Skeleton[b].ParentIndex >= 0 && bones.Contains(a.Asset.Skeleton[b].ParentIndex))
                    bones.Add(b);
        }
        foreach (var t in a.WeaponTriangles)
            if ((bones is not null && bones.Contains(mesh.TriangleBone(t))) || meshParts.Contains(mesh.TrianglePart[t]))
                yield return t;
    }

    /// <summary>Triangles of the frame: the weapon minus parts that sit inside or hang off the grip.</summary>
    private static int[] FrameTriangles(WeaponAnalysis a)
    {
        var excluded = new HashSet<int>();
        foreach (var part in a.Parts.Where(p => p.Kind is PartKind.Magazine or PartKind.Foregrip or PartKind.Pump or PartKind.Trigger))
            foreach (var t in PartTriangles(a, part))
                excluded.Add(t);
        var tris = a.WeaponTriangles.Where(t => !excluded.Contains(t)).ToArray();
        return tris.Length > 0 ? tris : a.WeaponTriangles;
    }

    private static GripCandidate? FindPistolGrip(WeaponAnalysis a)
    {
        var p = FrameProfile(a);
        // The magazine often sits inside the grip; probe the shell around it, not the magazine.
        var frame = new MeshBvh(a.Asset.Mesh, FrameTriangles(a));
        var length = a.Length;
        var boreZ = a.BoreStart.Z;

        // Receiver bottom: typical lowest surface over the middle of the weapon.
        var from = p.Bin(p.MinX + length * 0.2f);
        var to = p.Bin(p.MinX + length * 0.8f);
        // Upper percentile: on compact guns the handle spans much of the middle, the receiver
        // underside is the higher level most slices share.
        var receiverBottom = p.Percentile(i => p.Bottom[i], length < 14f ? 0.75f : 0.6f, from, to);
        var drop = MathF.Max(0.9f, MathF.Min(2.5f, (boreZ - receiverBottom) * 0.8f));

        var runs = p.Runs(i => p.Bottom[i] < receiverBottom - drop, 2);
        WeaponAnalyzer.Trace?.Invoke($"grip: receiver bottom {receiverBottom:0.##}, drop {drop:0.##}, runs {string.Join(" ", runs.Select(r => $"[{p.X(r.Start):0.#}..{p.X(r.End):0.#}]"))}");
        if (runs.Count == 0)
            return null;

        var trigger = a.Part(PartKind.Trigger);
        var mag = a.Part(PartKind.Magazine);
        var pistolLike = length < 14f;

        (int Start, int End)? bestRun = null;
        var bestScore = float.MinValue;
        var bestReason = "";
        foreach (var run in runs)
        {
            var x0 = p.X(run.Start);
            var x1 = p.X(run.End);
            var width = x1 - x0 + p.BinWidth;
            var cx = (x0 + x1) * 0.5f;
            var deepest = Enumerable.Range(run.Start, run.End - run.Start + 1).Where(p.Has).Min(i => p.Bottom[i]);
            var depth = receiverBottom - deepest;

            var score = MathF.Min(depth, 6f) * 0.8f;
            var why = $"handle below the receiver ({depth:0.#} in)";
            // Grips are 0.8-3 in front to back.
            score -= MathF.Abs(Math.Clamp(width, 0.8f, 3.2f) - width) * 1.2f;
            if (trigger is { } tr)
            {
                // The trigger sits at the front-top of the grip: 0-4 in ahead of its centre.
                var behind = tr.Center.X - cx;
                var outside = behind < -1f ? -1f - behind : behind > 4.5f ? behind - 4.5f : 0f;
                score += 2f - outside;
                if (outside == 0f)
                    why += ", behind the trigger";
            }
            if (!pistolLike)
            {
                // The rear-most drop on a long gun is usually the stock toe.
                if (x0 < p.MinX + length * 0.12f)
                    score -= 3f;
                // The grip sits behind the magazine.
                if (mag is { } m && cx > m.Center.X)
                    score -= 2f;
                // Buttstock drops are long.
                if (width > 5f)
                    score -= 2f;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestRun = run;
                bestReason = why;
            }
        }
        if (bestRun is not { } chosen)
            return null;
        // No real pistol grip: the only drop is the buttstock. Hold the stock's wrist instead.
        if (!pistolLike && p.X(chosen.Start) < p.MinX + length * 0.12f && (p.X(chosen.End) - p.X(chosen.Start)) > 5f)
            return StockWrist(a, frame, p, trigger);

        // Axis of the grip: centre line of its horizontal cross-sections.
        var gripBottom = Enumerable.Range(chosen.Start, chosen.End - chosen.Start + 1).Where(p.Has).Min(i => p.Bottom[i]);
        var axis = GripAxis(frame, a, p, chosen, receiverBottom, gripBottom);

        // Palm height: upper third of the handle, just under the receiver (web of the hand high).
        var contactZ = receiverBottom - (receiverBottom - gripBottom) * 0.32f;
        // Handle extent at that height from the silhouette (slices that reach below contactZ).
        var reaching = Enumerable.Range(chosen.Start, chosen.End - chosen.Start + 1).Where(i => p.Has(i) && p.Bottom[i] <= contactZ).ToList();
        var profX0 = reaching.Count > 0 ? p.X(reaching.First()) : p.X(chosen.Start);
        var profX1 = reaching.Count > 0 ? p.X(reaching.Last()) : p.X(chosen.End);
        var profMid = reaching.Count > 0 ? reaching[reaching.Count / 2] : (chosen.Start + chosen.End) / 2;
        var profileCenter = new Vector3((profX0 + profX1) * 0.5f, p.Has(profMid) ? (p.Left[profMid] + p.Right[profMid]) * 0.5f : a.BoreStart.Y, contactZ);
        var rayCenter = SectionCenter(frame, a, contactZ, profX0 - 0.5f, profX1 + 0.5f);
        // The centre must really be inside the handle: sample across it at this height and
        // take the middle of the longest inside span (open or odd meshes fool single rays).
        var center = InsideCenter(frame, profX0 - 0.5f, profX1 + 0.5f, profileCenter.Y, contactZ)
            ?? (rayCenter is { } rc && rc.X >= profX0 - p.BinWidth && rc.X <= profX1 + p.BinWidth ? rc : profileCenter);

        // Rear-right of the handle: rotate "backwards" around the grip axis toward the weapon's right.
        var back = Vector3.Normalize(-Vector3.UnitX - axis * Vector3.Dot(-Vector3.UnitX, axis));
        var right = Vector3.Normalize(Vector3.Cross(axis, back));
        if (right.Y > 0f) right = -right; // weapon right is -Y
        var rad = PalmWrapDegrees * MathF.PI / 180f;
        var dir = Vector3.Normalize(back * MathF.Cos(rad) + right * MathF.Sin(rad));

        var surface = ProbeFromInside(frame, center, dir, axis) ?? SurfaceProbe.FromPoint(frame, center + dir * 3f, axis);
        if (surface is not null)
        {
            // The handle's axis from its cross-sections is more reliable than the local patch.
            var along = axis - surface.Normal * Vector3.Dot(axis, surface.Normal);
            if (along.LengthSquared() > 1e-4f)
                surface = surface with { Axis = Vector3.Normalize(along) };
        }
        WeaponAnalyzer.Trace?.Invoke($"grip: contactZ {contactZ:0.##} centre {center} axis {axis} dir {dir} -> {surface?.Contact}");
        if (surface is null)
            return null;

        var confidence = Math.Clamp(0.45f + bestScore * 0.08f, 0.3f, 0.95f);
        return new GripCandidate { Surface = surface, Style = GripStyle.Wrap, Confidence = confidence, Reason = bestReason };
    }

    /// <summary>
    /// Straight-stock weapons (pump shotguns, hunting rifles) are held at the narrow wrist of the
    /// stock just behind the trigger guard.
    /// </summary>
    private static GripCandidate? StockWrist(WeaponAnalysis a, MeshBvh frame, ShapeProfile p, PartDetection? trigger)
    {
        var tx = trigger?.Center.X ?? (p.MinX + a.Length * 0.38f);
        // Narrow slice about two inches behind the trigger (the hand's width behind the guard).
        var best = -1;
        var bestScore = float.MaxValue;
        for (var i = p.Bin(tx - 4f); i <= p.Bin(tx - 1f); i++)
        {
            if (!p.Has(i))
                continue;
            var score = p.Height(i) + MathF.Abs(p.X(i) - (tx - 2f)) * 0.6f;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        WeaponAnalyzer.Trace?.Invoke($"stock wrist: trigger x {tx:0.##}, best bin {best}");
        if (best < 0)
            return null;
        var x = p.X(best);
        var y = (p.Left[best] + p.Right[best]) * 0.5f;
        var center = new Vector3(x, y, (p.Top[best] + p.Bottom[best]) * 0.5f);
        // Thumbhole stocks: the slice centre can be in the hole. Use the lowest solid segment
        // along a vertical line that is wrist thick.
        var hits = frame.RaycastAll(new Vector3(x, y, p.Bottom[best] - 2f), Vector3.UnitZ, p.Height(best) + 4f);
        for (var h = 0; h + 1 < hits.Count; h += 2)
        {
            var thick = hits[h + 1].Point.Z - hits[h].Point.Z;
            if (thick >= 0.5f && thick <= 3.2f)
            {
                center = new Vector3(x, y, (hits[h].Point.Z + hits[h + 1].Point.Z) * 0.5f);
                break;
            }
        }
        // The wrist slopes down toward the butt; follow the centre line of nearby slices.
        var back = p.Bin(x - 1.5f);
        var fore = p.Bin(x + 1.5f);
        var axis = p.Has(back) && p.Has(fore)
            ? Vector3.Normalize(new Vector3(p.X(fore) - p.X(back), 0f, (p.Top[fore] + p.Bottom[fore] - p.Top[back] - p.Bottom[back]) * 0.5f))
            : Vector3.UnitX;
        // Hand wraps the wrist: knuckles along its long axis, palm on the right side.
        var surface = ProbeFromInside(frame, center, Vector3.Normalize(new Vector3(0f, -1f, 0.35f)), axis)
            ?? SurfaceProbe.FromPoint(frame, center - Vector3.UnitY * 2f, axis);
        if (surface is null)
            return null;
        return new GripCandidate { Surface = surface, Style = GripStyle.Wrap, Confidence = 0.6f, Reason = "wrist of the stock (no pistol grip)" };
    }

    /// <summary>Middle of the longest run of inside samples along X at (y, z), or null.</summary>
    private static Vector3? InsideCenter(MeshBvh bvh, float x0, float x1, float y, float z)
    {
        const int n = 24;
        int bestStart = -1, bestLen = 0, start = -1;
        for (var i = 0; i <= n; i++)
        {
            var inside = i < n && bvh.IsInside(new Vector3(x0 + (x1 - x0) * (i + 0.5f) / n, y, z), 60f);
            if (inside && start < 0)
                start = i;
            if (!inside && start >= 0)
            {
                if (i - start > bestLen) { bestLen = i - start; bestStart = start; }
                start = -1;
            }
        }
        if (bestLen == 0)
            return null;
        var mid = bestStart + bestLen * 0.5f;
        return new Vector3(x0 + (x1 - x0) * mid / n, y, z);
    }

    /// <summary>Average X/Y centre of the horizontal slice at height z between x0 and x1.</summary>
    private static Vector3? SectionCenter(MeshBvh bvh, WeaponAnalysis a, float z, float x0, float x1)
    {
        var y = a.BoreStart.Y;
        // Two horizontal rays through the slice give its front/back; two lateral rays its sides.
        var back = bvh.Raycast(new Vector3(x0 - 4f, y, z), Vector3.UnitX, x1 - x0 + 8f);
        var front = bvh.Raycast(new Vector3(x1 + 4f, y, z), -Vector3.UnitX, x1 - x0 + 8f);
        if (back is null || front is null || front.Value.Point.X <= back.Value.Point.X)
            return null;
        var cx = (back.Value.Point.X + front.Value.Point.X) * 0.5f;
        var left = bvh.Raycast(new Vector3(cx, y + 6f, z), -Vector3.UnitY, 12f);
        var rightHit = bvh.Raycast(new Vector3(cx, y - 6f, z), Vector3.UnitY, 12f);
        var cy = left is { } l && rightHit is { } r ? (l.Point.Y + r.Point.Y) * 0.5f : y;
        return new Vector3(cx, cy, z);
    }

    private static Vector3 GripAxis(MeshBvh bvh, WeaponAnalysis a, ShapeProfile p, (int Start, int End) run, float top, float bottom)
    {
        var x0 = p.X(run.Start) - 0.5f;
        var x1 = p.X(run.End) + 0.5f;
        var upper = SectionCenter(bvh, a, top - (top - bottom) * 0.15f, x0, x1);
        var lower = SectionCenter(bvh, a, bottom + (top - bottom) * 0.15f, x0, x1);
        if (upper is { } u && lower is { } l && Vector3.Distance(u, l) > 0.5f)
        {
            var d = Vector3.Normalize(u - l);
            // Grips rake back (bottom behind top) by 0-30 degrees; reject anything wilder.
            if (d.Z > 0.7f)
                return d;
        }
        return Vector3.Normalize(new Vector3(0.28f, 0f, 1f));
    }

    /// <summary>
    /// Casts from a point inside the part outward and measures the surface where the ray
    /// leaves it. Falls back to the closest surface point when the part is open.
    /// </summary>
    public static GripSurface? ProbeFromInside(MeshBvh bvh, Vector3 inside, Vector3 direction, Vector3? axisHint)
    {
        var hit = bvh.Raycast(inside, direction, 12f);
        if (hit is not { } h)
            return null;
        // The returned normal faces the ray origin (inward here); outward is the opposite.
        var outward = -h.Normal;
        if (Vector3.Dot(outward, direction) < 0f)
            outward = -outward;
        return SurfaceProbe.Measure(bvh, h.Point, outward, h.Triangle, axisHint);
    }

    /// <summary>
    /// Thumbhole stocks and bullpups (P90, AWP-style stocks): the firing hand wraps the solid
    /// bar just behind the trigger, found by entering the underside and measuring how thick
    /// the solid is before the hole.
    /// </summary>
    private static GripCandidate? BarGrip(WeaponAnalysis a)
    {
        if (a.Part(PartKind.Trigger) is not { } trigger)
            return null;
        var frame = new MeshBvh(a.Asset.Mesh, FrameTriangles(a));
        var x = trigger.Center.X - 1.6f;
        var start = new Vector3(x, a.BoreStart.Y, a.WeaponBounds.Min.Z - 2f);
        var hits = frame.RaycastAll(start, Vector3.UnitZ, a.WeaponBounds.Size.Z + 4f);
        if (hits.Count < 2)
            return null;
        var z0 = hits[0].Point.Z;
        var z1 = hits[1].Point.Z;
        var thickness = z1 - z0;
        // A bar under a hole: thin, and below the bore.
        if (thickness < 0.4f || thickness > 3f || z1 > a.BoreStart.Z)
            return null;
        var center = new Vector3(x, a.BoreStart.Y, (z0 + z1) * 0.5f);
        var surface = ProbeFromInside(frame, center, Vector3.Normalize(new Vector3(0f, -1f, -0.3f)), Vector3.UnitX);
        if (surface is null)
            return null;
        // Fist upright through the thumbhole: knuckle line raked like a pistol grip.
        var raked = Vector3.Normalize(new Vector3(0.35f, 0f, 1f));
        var along = raked - surface.Normal * Vector3.Dot(raked, surface.Normal);
        if (along.LengthSquared() > 1e-4f)
            surface = surface with { Axis = Vector3.Normalize(along) };
        return new GripCandidate { Surface = surface, Style = GripStyle.Wrap, Confidence = 0.5f, Reason = $"bar behind the trigger ({thickness:0.#} in thick)" };
    }

    private static GripCandidate? FallbackPrimary(WeaponAnalysis a)
    {
        // No clear handle: hold the rear third of the underside.
        var x = a.WeaponBounds.Min.X + a.Length * (a.Length < 14f ? 0.3f : 0.35f);
        var below = new Vector3(x, a.BoreStart.Y, a.WeaponBounds.Min.Z - 2f);
        var hit = a.WeaponBvh.Raycast(below, Vector3.UnitZ, a.WeaponBounds.Size.Z + 4f);
        if (hit is null)
            return null;
        var s = SurfaceProbe.Measure(a.WeaponBvh, hit.Value.Point, -Vector3.UnitZ, hit.Value.Triangle, Vector3.UnitX);
        return new GripCandidate { Surface = s, Style = GripStyle.Wrap, Confidence = 0.25f, Reason = "no pistol grip found; using the underside" };
    }

    // ------------------------------------------------------------------ support

    private static IEnumerable<GripCandidate> SupportCandidates(WeaponAnalysis a)
    {
        var list = new List<GripCandidate>();
        var type = a.Type.Type;

        if (a.Part(PartKind.Foregrip) is { } fg && PartSurface(a, fg, new Vector3(-0.6f, 0.6f, 0f), Vector3.UnitZ) is { } fgs)
            list.Add(new GripCandidate { Surface = fgs, Style = GripStyle.Wrap, Confidence = Math.Clamp(fg.Confidence, 0.4f, 0.95f), Reason = "foregrip" });

        if (a.Part(PartKind.Pump) is { } pump && PartSurface(a, pump, new Vector3(0f, 0.55f, -1f), Vector3.UnitX) is { } ps)
            list.Add(new GripCandidate { Surface = ps, Style = GripStyle.Pump, Confidence = Math.Clamp(pump.Confidence, 0.4f, 0.95f), Reason = "pump" });

        if (type is WeaponType.Pistol or WeaponType.Revolver || (a.Length < 12f && type == WeaponType.Custom))
        {
            if (a.Primary is { } primary)
            {
                // Support hand wraps the firing hand: its palm sits on the left of the grip, outside the right fingers.
                var inside = primary.Surface.Center;
                var s = ProbeFromInside(a.WeaponBvh, inside, Vector3.Normalize(new Vector3(0.15f, 1f, -0.2f)), primary.Surface.Axis);
                if (s is not null)
                    list.Add(new GripCandidate { Surface = s, Style = GripStyle.Overlay, Confidence = 0.7f, Reason = "two-handed pistol grip" });
            }
        }
        else if (Handguard(a) is { } hg)
        {
            list.Add(hg);
        }

        // Magazine well as a fallback support area (SMGs, bullpups).
        if (a.Part(PartKind.Magazine) is { } mag && type is not (WeaponType.Pistol or WeaponType.Revolver))
        {
            var tris = PartTriangles(a, mag).ToArray();
            if (tris.Length > 0)
            {
                var top = tris.Max(t => { var (x, y, z) = a.Asset.Mesh.Triangle(t); return MathF.Max(x.Z, MathF.Max(y.Z, z.Z)); });
                var inside = new Vector3(mag.Center.X, mag.Center.Y, MathF.Min(top - 0.6f, (top + mag.Center.Z) * 0.5f));
                var s = ProbeFromInside(a.WeaponBvh, inside, Vector3.Normalize(new Vector3(0.2f, 1f, -0.3f)), Vector3.UnitZ);
                if (s is not null)
                    list.Add(new GripCandidate { Surface = s, Style = GripStyle.Wrap, Confidence = 0.35f, Reason = "magazine well" });
            }
        }

        return list.OrderByDescending(c => c.Confidence);
    }

    private static GripSurface? PartSurface(WeaponAnalysis a, PartDetection part, Vector3 outward, Vector3 axisHint)
        => ProbeFromInside(a.WeaponBvh, part.Center, Vector3.Normalize(outward), axisHint);

    /// <summary>Underside of the barrel/handguard ahead of the magazine and trigger.</summary>
    private static GripCandidate? Handguard(WeaponAnalysis a)
    {
        var p = a.Profile;
        var gripX = a.Primary?.Surface.Center.X ?? (a.WeaponBounds.Min.X + a.Length * 0.35f);
        var muzzleX = a.Muzzle?.Model.Pos.X ?? a.WeaponBounds.Max.X;
        var reach = muzzleX - gripX;
        if (reach < 4f)
            return null;

        var ahead = new[] { a.Part(PartKind.Magazine)?.Center.X ?? gripX, a.Part(PartKind.Trigger)?.Center.X ?? gripX }.Max();
        var ideal = gripX + Math.Clamp(reach * 0.45f, 5f, 16f);
        var minX = MathF.Max(ahead + 2.2f, gripX + 4f);
        var maxX = muzzleX - 1.5f;
        if (minX > maxX)
            minX = maxX = Math.Clamp(ideal, gripX + 2f, muzzleX - 0.5f);
        var x = Math.Clamp(ideal, minX, maxX);

        // Slide to the nearest slice with geometry close under the bore (skip bare barrel gaps).
        var boreZ = a.BoreStart.Z;
        var bestBin = -1;
        var bestDist = float.MaxValue;
        for (var i = p.Bin(minX); i <= p.Bin(maxX); i++)
        {
            if (!p.Has(i))
                continue;
            var under = boreZ - p.Bottom[i];
            if (under < 0.15f || under > 4.5f)
                continue;
            var d = MathF.Abs(p.X(i) - x);
            // Prefer thicker sections (a handguard over a bare barrel).
            d -= MathF.Min(p.Height(i), 3f) * 0.8f;
            if (d < bestDist)
            {
                bestDist = d;
                bestBin = i;
            }
        }
        if (bestBin >= 0)
            x = p.X(bestBin);

        var bin = p.Bin(x);
        var zMid = p.Has(bin) ? (MathF.Min(boreZ, p.Top[bin]) + p.Bottom[bin]) * 0.5f : boreZ;
        var inside = new Vector3(x, a.BoreStart.Y, zMid);
        // Palm under the handguard, heel toward the weapon's left.
        var dir = Vector3.Normalize(new Vector3(0f, 0.5f, -1f));
        var s = ProbeFromInside(a.WeaponBvh, inside, dir, Vector3.UnitX);
        if (s is null)
        {
            var below = a.WeaponBvh.Raycast(new Vector3(x, a.BoreStart.Y, a.WeaponBounds.Min.Z - 2f), Vector3.UnitZ, 20f);
            if (below is null)
                return null;
            s = SurfaceProbe.Measure(a.WeaponBvh, below.Value.Point, -Vector3.UnitZ, below.Value.Triangle, Vector3.UnitX);
        }
        var conf = bestBin >= 0 ? 0.65f : 0.4f;
        return new GripCandidate { Surface = s, Style = GripStyle.Cradle, Confidence = conf, Reason = "handguard under the barrel" };
    }

    // ------------------------------------------------------------------ melee

    private static void FindMeleeGrips(WeaponAnalysis a)
    {
        var p = a.Profile;
        // A handle is a run of small, roughly round slices; blades are flat, guards are wide.
        float Big(int i) => MathF.Max(p.Height(i), p.Width(i));
        float Small(int i) => MathF.Max(0.05f, MathF.Min(p.Height(i), p.Width(i)));
        bool HandleSlice(int i) => p.Has(i) && Big(i) <= 2.2f && Big(i) / Small(i) <= 2.4f;

        var runs = p.Runs(HandleSlice, 3);
        (int Start, int End) handle;
        if (runs.Count > 0)
        {
            // Prefer long runs that touch an end of the weapon (pommel side).
            handle = runs.OrderByDescending(r =>
            {
                var len = (r.End - r.Start + 1) * p.BinWidth;
                var atEnd = r.Start <= 2 || r.End >= p.Count - 3 ? 1.5f : 0f;
                return len + atEnd;
            }).First();
        }
        else
        {
            // Fall back to the thinner end.
            var n = Math.Max(2, p.Count / 5);
            var rear = Enumerable.Range(0, n).Where(p.Has).Select(Big).DefaultIfEmpty(99f).Average();
            var front = Enumerable.Range(p.Count - n, n).Where(p.Has).Select(Big).DefaultIfEmpty(99f).Average();
            handle = rear <= front ? (0, n - 1) : (p.Count - n, p.Count - 1);
        }

        var x0 = p.X(handle.Start);
        var x1 = p.X(handle.End);
        var handleLength = x1 - x0 + p.BinWidth;
        // The blade (the bigger remainder) sits on the side with more geometry.
        var before = Enumerable.Range(0, handle.Start).Count(p.Has);
        var after = Enumerable.Range(handle.End + 1, Math.Max(0, p.Count - handle.End - 1)).Count(p.Has);
        var towardBlade = after >= before ? Vector3.UnitX : -Vector3.UnitX;
        var guardEnd = towardBlade.X > 0 ? x1 : x0;

        Vector3 CenterAt(float x)
        {
            var bin = p.Bin(x);
            return new Vector3(x, p.Has(bin) ? (p.Left[bin] + p.Right[bin]) * 0.5f : a.BoreStart.Y, p.Has(bin) ? (p.Top[bin] + p.Bottom[bin]) * 0.5f : a.BoreStart.Z);
        }

        // One fist is ~3.5 in wide: centre it a little below the guard.
        var cx = guardEnd - towardBlade.X * MathF.Min(1.8f, handleLength * 0.45f);
        var s = ProbeFromInside(a.WeaponBvh, CenterAt(cx), -Vector3.UnitY, towardBlade);
        if (s is null)
            return;
        a.Primary = new GripCandidate { Surface = s, Style = GripStyle.Handle, Confidence = runs.Count > 0 && handleLength > 2.5f ? 0.75f : 0.4f, Reason = $"handle ({handleLength:0.#} in)" };

        if (handleLength > 7f)
        {
            var cx2 = guardEnd - towardBlade.X * MathF.Min(handleLength - 1.5f, 5.4f);
            var s2 = ProbeFromInside(a.WeaponBvh, CenterAt(cx2), Vector3.UnitY, towardBlade);
            if (s2 is not null)
            {
                var support = new GripCandidate { Surface = s2, Style = GripStyle.Handle, Confidence = 0.55f, Reason = "long handle, two-handed" };
                a.SupportAreas.Add(support);
                a.Support = support;
            }
        }
    }
}
