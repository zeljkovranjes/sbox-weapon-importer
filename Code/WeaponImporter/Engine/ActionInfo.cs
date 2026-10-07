using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WeaponImporter.Engine;

/// <summary>One baked weapon action (see <c>WeaponHold.Actions</c>).</summary>
public sealed class ActionInfo
{
    /// <summary>Lowercase role key (idle, fire, reload, ...).</summary>
    public string Role { get; set; } = "";

    /// <summary>Action duration in seconds (0 = use the sequence length, or 1 s).</summary>
    public float Seconds { get; set; }

    /// <summary>Weapon model sequence to play ("" = none).</summary>
    public string Sequence { get; set; } = "";

    /// <summary>Character animgraph bool whose rising edge starts the action ("" = code only).</summary>
    public string Trigger { get; set; } = "";

    /// <summary>More weapon sequences played in turn with <see cref="Sequence"/> (left and right punches, a combo of slashes).</summary>
    public List<string> Variants { get; set; } = new();
}
