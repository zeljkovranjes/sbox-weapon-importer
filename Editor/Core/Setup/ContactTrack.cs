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

public sealed class ContactTrack
{
    public List<ContactKey> Keys { get; set; } = new();

    /// <summary>Planned by the importer (re-planned when the grip changes); false once the user edits it.</summary>
    public bool Generated { get; set; }

    /// <summary>Why the importer planned it this way (shown in the timeline tooltip).</summary>
    public string Note { get; set; } = "";

    /// <summary>Contact weight (0 released, 1 locked) of a hand at normalized time t.</summary>
    public float Weight(Side hand, float t, float durationSeconds)
    {
        var keys = Keys.Where(k => k.Hand == hand).OrderBy(k => k.Time).ToList();
        var weight = 1f;
        foreach (var key in keys)
        {
            if (key.Time > t)
                break;
            var target = key.State == ContactState.Locked ? 1f : 0f;
            var blendNorm = durationSeconds > 1e-3f ? key.Blend / durationSeconds : 0f;
            var u = blendNorm <= 1e-5f ? 1f : Math.Clamp((t - key.Time) / blendNorm, 0f, 1f);
            u = u * u * (3f - 2f * u); // smoothstep
            weight += (target - weight) * u;
        }
        return Math.Clamp(weight, 0f, 1f);
    }
}
