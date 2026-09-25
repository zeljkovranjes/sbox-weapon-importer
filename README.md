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
  weapon, at their camera, in sync with the third-person hold. Turn off `FirstPerson` on it when
  your camera is in third person.

## Requirements

Third-person animations need the
[Humanoid Retargeter](https://github.com/zeljkovranjes/humanoid-retargeter) library.

## Credits

Built-in grips come from "FPS AK-74m animations" by Cransh (CC-BY-4.0) and the Uzi first-person
animations by 1Matzh.
