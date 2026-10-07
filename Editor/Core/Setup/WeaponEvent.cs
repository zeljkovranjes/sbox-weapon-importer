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

public sealed class WeaponEvent
{
    public AnimationRole Role { get; set; }
    public WeaponEventKind Kind { get; set; }
    /// <summary>Normalized time 0..1 in the role's clip.</summary>
    public float Time { get; set; }
    public bool Manual { get; set; }

    public static string EngineName(WeaponEventKind kind) => kind switch
    {
        WeaponEventKind.Fire => "weapon_fire",
        WeaponEventKind.MuzzleFlash => "muzzle_flash",
        WeaponEventKind.ShellEject => "shell_eject",
        WeaponEventKind.MagazineDetach => "magazine_detach",
        WeaponEventKind.MagazineInsert => "magazine_insert",
        WeaponEventKind.BoltPull => "bolt_pull",
        WeaponEventKind.BoltRelease => "bolt_release",
        WeaponEventKind.ReloadComplete => "reload_complete",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static string Label(WeaponEventKind kind) => kind switch
    {
        WeaponEventKind.MuzzleFlash => "Muzzle flash",
        WeaponEventKind.ShellEject => "Shell eject",
        WeaponEventKind.MagazineDetach => "Magazine detach",
        WeaponEventKind.MagazineInsert => "Magazine insert",
        WeaponEventKind.BoltPull => "Bolt pull",
        WeaponEventKind.BoltRelease => "Bolt release",
        WeaponEventKind.ReloadComplete => "Reload complete",
        _ => kind.ToString(),
    };
}
