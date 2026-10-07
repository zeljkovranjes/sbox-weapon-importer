# Changelog

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
