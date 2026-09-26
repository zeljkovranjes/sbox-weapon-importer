# Weapon Importer for s&box

Import a weapon, check it in first and third person, adjust the grip while an animation plays,
and bake a game-ready prefab.

## Use

1. Open **View → Weapon Importer**.
2. Drop an FBX, GLB, glTF or VMDL onto the window.
3. Check the pages on the left (**Weapon**, **Grip**, **Animations**, **Materials**, **Export**).
4. Press **Bake**.

The importer sets up the weapon type, grips, animations and textures automatically. Pick another
grip, character or hold from the dropdowns, drag a hand across the weapon in the preview, or nudge
a hand with its **Move** arrows.

## Output

Everything goes to `Assets/weapons/<name>/`. Put `<name>.prefab` under your player:

- **Third person:** its **Weapon Hold** keeps the character's hands on the weapon and plays the
  weapon's animations when the character attacks, reloads or deploys.
- **First person:** when the file has its own arms, the prefab also carries `<name>_fp.vmdl` (arms,
  weapon and animations as authored). **Weapon Viewmodel** shows it to the player holding the
  weapon, at their camera, and plays its generated animgraph: fire, reload, inspect and holster
  follow the third-person hold, and setting `Aiming` on Weapon Hold raises and lowers the sights.
  Turn off `FirstPerson` on it when your camera is in third person.

Clips are recognised by name (idle, fire, reload, draw, aim in/out...). Anything that isn't can be
assigned on the **Animations** page: click a row and search for the clip.

Packs that ship pieces separately work too: animations in their own files next to the model (or in
an `Animations` folder) are picked up, and a pack's shared arms model (for example `FP_Arms.fbx`
with `FP_Arms_Pistol_01_Fire.fbx`) is found and the weapon put in its hands. Choose other arms or
turn them off in the **Arms** row on the **Weapon** page.

## Melee, items, fists and dual weapons

Knives, swords, fists and items (syringes, bottles, flashlights...) import like guns: attacks can
play several clips in turn, and blocks and held uses have start, loop and end clips. A file with
two copies of a weapon (`gun_L` / `gun_R`) is set up as dual weapons, one in each hand.

For what the character's animgraph doesn't cover, press **Download stock animations** on the
**Animations** page (under 1 MB, from this repository's releases). The importer then picks a
style for the weapon (one- or two-handed melee, polearm, dual blades, fists, dual pistols, drinking,
injecting, throwing, carrying...) and its character hold and actions. Change the style there, or
give any action your own sequence from any model with the character's skeleton.

## From your game code

Actions come from the character's animgraph (`b_attack`, `b_reload`, `b_deploy`) or
`WeaponHold.Play( "reload" )`. On **Weapon Hold**, set:

- `Aiming` while the player aims down the sights
- `Empty` when the magazine is empty (plays the empty reload / last-round fire if the weapon has them)
- `ShellsToLoad` before a shell-by-shell reload; `ShellInserted` fires for each shell
- `Blocking` while the player holds the guard up, `Using` while they keep using an item
- `Play( "attack2" )`, `Play( "use" )` or `Play( "throw" )` for a heavy attack, a one-off use or a throw

Edited animgraphs and prefabs are kept when you bake again; delete one to have it regenerated.

## Requirements

Third-person animations need the
[Humanoid Retargeter](https://github.com/zeljkovranjes/humanoid-retargeter) library.

## Credits

Built-in grips come from "FPS AK-74m animations" by Cransh (CC-BY-4.0) and the Uzi first-person
animations by 1Matzh. The stock animations are retargeted from Human Melee Animations by Kevin
Iglesias, Item Consumable Animations, Grenade Animation Kit and Insane Gunner Animset.
