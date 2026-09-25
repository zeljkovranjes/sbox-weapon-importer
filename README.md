# Weapon Importer for s&box

Import a weapon, check it in first and third person, adjust the grip while an animation plays,
and bake a game-ready prefab.

## Use

1. Open **View → Weapon Importer**.
2. Drop an FBX, GLB, glTF or VMDL onto the window.
3. Check the pages on the left (**Weapon**, **Grip**, **Animations**, **Materials**, **Export**).
4. Press **Bake**.

The importer sets up the weapon type, grips, animations and textures automatically. Pick another
grip, character or hold from the dropdowns, or drag a hand across the weapon in the preview.

## Output

Everything goes to `Assets/weapons/<name>/`. Put `<name>.prefab` under your player: its
**Weapon Hold** component keeps the hands on the weapon and plays the weapon's animations when the
character attacks, reloads or deploys.

## Requirements

Third-person animations need the
[Humanoid Retargeter](https://github.com/zeljkovranjes/humanoid-retargeter) library.

## Credits

Built-in grips come from "FPS AK-74m animations" by Cransh (CC-BY-4.0) and the Uzi first-person
animations by 1Matzh.
