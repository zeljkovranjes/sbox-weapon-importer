# Changelog

## 2026-11-03
### Added
- AnimGraph2 support, chosen by the character: when the character the weapon is fitted to is posed with an
  AnimGraph2 graph (`.ag`, its own or the borrowed citizen/human one), the preview drives that graph and the bake
  writes AnimGraph2 weapon graphs (`<name>.ag`, `<name>_fp.ag`, assigned with `animation_graph_name`) with the
  same parameters, states and transitions, plus a `.skel` and an `.anim` per clip under `generated/`. AnimGraph1
  characters (every shipped one today) bake exactly as before. An edited `.ag` is kept like an edited `.vanmgrph`.
- `WeaponImporter.Engine.AnimGraphParameters` sets animgraph parameters on either graph system; Weapon Hold and
  Weapon Viewmodel use it. On an AnimGraph2 character, Weapon Hold only sees action triggers set through it
  (`AnimGraphParameters.Trigger( body, "b_attack" )`), since game code can't read AnimGraph2 parameters.
### Fixed
- A bake no longer reports a compile error for a file the engine refused to compile while a content build was
  running; it tries again.

## 2026-10-06
### Breaking
- Runtime types moved out of the `WeaponImporter` namespace. Add the matching `using` to game code
  (for example the weapon-importer-demo project); scenes, prefabs and `.wgrip` files made before keep
  loading, because every component, resource and system carries an `[Alias]` with its old name.
  - `WeaponImporter.WeaponHold` -> `WeaponImporter.Components.WeaponHold`
  - `WeaponImporter.WeaponViewmodel` -> `WeaponImporter.Components.WeaponViewmodel`
  - `WeaponImporter.WeaponGripProfile` -> `WeaponImporter.Resources.WeaponGripProfile`
  - `WeaponImporter.WeaponHoldSystem` -> `WeaponImporter.Systems.WeaponHoldSystem`
  - `WeaponImporter.ArmSolver`, `ArmSolution`, `HoldData`, `HoldContact`, `ActionInfo`,
    `FingerRotation`, `OverlayInfo` -> the same names in `WeaponImporter.Engine`
- Editor-side namespaces renamed (only matters if you call the importer's editor code):
  `WeaponImporter.Core.*` -> `WeaponImporter.EditorTools.Core.*`,
  `WeaponImporter.Tool` -> `WeaponImporter.EditorTools.Engine`,
  `WeaponImporter.Tool.UI` -> `WeaponImporter.EditorTools.UI` (plus `.Preview`, `.Steps`,
  `.Timeline`, `.Widgets` by folder).
### Changed
- Brought under the workspace package standard: runtime code is split into Engine, Components,
  Systems and Resources folders, the editor code into Core, Engine and UI, one type per file. The
  importer and the components behave as before, and the baker still writes the old component type
  names (resolved through the aliases), so rebaking a weapon doesn't change its prefab.
- The package now has a description, tags and a README in the standard format.
### Fixed
- Retargeting animations through the Humanoid Retargeter works with its restructured version
  (it moved its types into new namespaces); older versions of the retargeter still work too.
