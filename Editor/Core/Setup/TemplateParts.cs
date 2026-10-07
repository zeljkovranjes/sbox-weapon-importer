#nullable enable annotations

using System.Numerics;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Geometry;

namespace WeaponImporter.EditorTools.Core.Setup;

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
