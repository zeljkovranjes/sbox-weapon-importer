# Weapon Importer

An editor window that imports a weapon, lets you check it in first and third person, adjust the grip
while an animation plays, and bakes a game-ready prefab. Two runtime components then hold the weapon
in your character's hands (third person) and show the weapon's own arms at the player's camera (first
person). For s&box developers who have weapon models and animations from asset packs.

## Requirements

- Third-person animations from other rigs need the
  [Humanoid Retargeter](https://github.com/zeljkovranjes/humanoid-retargeter) library. The importer
  finds it when it is installed and works without it.
- Stock third-person animations (optional) are downloaded from this repository's releases by the
  **Download stock animations** button (under 1 MB).

## Install

Search for **Weapon Importer** in the s&box library manager, or add `notpointless.chomnr_weapon_importer`.

## Quick start

### In the editor

1. Open **View → Weapon Importer**.
2. Drop an FBX, GLB, glTF or VMDL onto the window.
3. Check the pages on the left (**Weapon**, **Grip**, **Animations**, **Materials**, **Export**). The
   importer sets up the weapon type, grips, animations and textures automatically. Pick another grip,
   character or hold from the dropdowns, drag a hand across the weapon in the preview, or nudge a
   hand with its **Move** arrows.
4. Press **Bake**. Everything goes to `Assets/weapons/<name>/`.
5. Put `<name>.prefab` under your player. Its **Weapon Hold** keeps the character's hands on the
   weapon and plays the weapon's animations when the character attacks, reloads or deploys. When the
   file has its own arms, the prefab also carries `<name>_fp.vmdl` and a **Weapon Viewmodel** that
   shows it to the player holding the weapon; turn off `FirstPerson` on it when your camera is in
   third person.

### In code

Actions come from the character's animgraph (`b_attack`, `b_reload`, `b_deploy`) or from
`WeaponHold.Play`. Set the hold's state from your input and ammo:

```csharp
using WeaponImporter.Components;

var hold = weapon.GetComponentInChildren<WeaponHold>();
hold.Aiming = Input.Down( "attack2" );   // raises the sights in first person
hold.Empty = ammo == 0;                  // empty reload / last-round fire when the weapon has them
hold.ShellsToLoad = 6;                   // before a shell-by-shell reload; ShellInserted fires per shell
hold.Play( "reload" );                   // also "attack2", "use", "throw"
```

`Blocking` (guard up) and `Using` (held item use) work the same way as `Aiming`.

## Options

**Weapon Hold** (the importer fills in the baked hand, finger, contact and action data):

| Option | Default | What it does |
|---|---|---|
| Body | first ancestor renderer with `HoldBone` | Character renderer. |
| Weapon | renderer on this object | Weapon renderer. |
| HoldType | -1 | Citizen holdtype for this weapon (1 pistol, 2 rifle, 3 shotgun, 4 item, 6 melee, 7 rpg); -1 leaves it to the game. |
| Profile | none | A Weapon Grip Profile (`.wgrip`) whose values replace the grip properties. |
| HoldBone | `hold_R` | Character bone the weapon is held by. |
| SupportHand | true | Whether the left hand supports the weapon. |
| ElbowHintWeight | 0.35 | How much the elbows lean toward their hints. |
| Smoothing | 0.35 | Temporal smoothing of the IK corrections. |
| ReachSlack | 1.5 | Inches past full reach before the support hand lets go. |
| TransitionSeconds | 0.15 | Time a changed grip takes to ease in. |
| Correction | 1 | How much of the hand correction is applied (0 = the original animation). |
| Aiming, Empty, Blocking, Using | false | Game state, set from your code (see In code). |
| ShellsToLoad | 4 | Shells a shell-by-shell reload loads. |

**Weapon Viewmodel**: `FirstPerson` (true) shows the first-person view, `HideWorldWeapon` (true)
leaves the owner only the shadow of the third-person weapon, `AimKick` (1) is the kick when firing
aimed without an aimed-fire animation, `NearClip` (1) is the camera near clip while the view shows.
`CameraBone`, `CameraAxes`, `EyeInModel` and `Offset` place the view and are set by the importer.

## How it works

The importer reads the file (FBX 6/7, glTF, GLB or VMDL), finds the weapon, its parts (magazine, bolt,
trigger, muzzle), any first-person arms and the animation clips, and recognises clips by name (idle,
fire, reload, draw, aim in/out...). Hands are fitted to the weapon's real surface, then the weapon,
first-person model, materials, animgraph and prefab are written to `Assets/weapons/<name>/`; edited
animgraphs and prefabs are kept when you bake again. At runtime Weapon Hold follows the character's
hold bone and corrects both arms with two-bone IK so the hands stay on the weapon through every
action. Melee weapons, fists, items and dual weapons (`gun_L` / `gun_R`) import the same way; for what
the character's animgraph doesn't cover, the stock animations add holds and actions by weapon style.

## Multiplayer

Weapon Hold and Weapon Viewmodel run locally on every client from the prefab's baked data. The library
syncs nothing itself: set `Aiming`, `Empty`, `Blocking`, `Using` and call `Play` on every client (for
example from your own `[Sync]` properties or animgraph parameters). The viewmodel is shown only to the
weapon's owner, never on proxies.

## Limitations

- The importer is editor-only; only the baked prefab and the two components ship with a game.
- Clips the importer can't recognise by name must be assigned by hand on the **Animations** page.
- Third-person animations from other rigs need the Humanoid Retargeter.
- Multiplayer sync of the hold's state is left to your game code.

## Development

- `sbox-check` (layout, docs, and the engine-free importer core `dev\WeaponImporter.Core.csproj`).
- `dotnet test tests\WeaponImporter.Tests` (importer core) and
  `dotnet run --project tests\RuntimeTests` (runtime IK and hold-data checks).
- Compile checks against the installed engine: `dotnet build dev\EngineCompileCheck.csproj`,
  `dev\RuntimeCompileCheck.csproj`, `dev\ShipCompileCheck.csproj`.
- In-editor gate: `dev\editor-gate\run_gate.ps1` (captures go to `dev\data\editor-gate\`).
- Tests that need real weapon files read them from `WI_CORPUS` and skip when it is missing.

## License

No license has been chosen yet. Built-in grips come from "FPS AK-74m animations" by Cransh
(CC-BY-4.0) and the Uzi first-person animations by 1Matzh. The stock animations are retargeted from
Human Melee Animations by Kevin Iglesias, Item Consumable Animations, Grenade Animation Kit and Insane
Gunner Animset.
