# Weapon Importer

Journal for this library. Keep it current: decisions, engine gotchas, what failed and why, the next step.

## Package

| | |
|---|---|
| Ident | `notpointless.chomnr_weapon_importer` |
| Type | library |
| Root namespace | `WeaponImporter` |
| Depends on | Humanoid Retargeter (optional, found by reflection in the editor) |
| Published | |

## Decisions

- The importer's engine-free core lives in `Editor/Core` (namespace `WeaponImporter.EditorTools.Core`),
  not `Code/WeaponImporter/Core`: it is editor-only and uses `System.IO.File`/`Directory`, which the
  runtime whitelist blocks, and it has no business shipping to players. `dev\WeaponImporter.Core.csproj`
  builds it as plain .NET (that is what `sbox-check` builds) and the xunit tests reference it.
- The runtime has no plain-C# Core: `ArmSolver`/`HoldData` use s&box maths types, so they are Engine.
- `WeaponBaker` still writes `"WeaponImporter.WeaponHold"` / `"WeaponImporter.WeaponViewmodel"` as the
  prefab `__type`. They resolve through `[Alias]`, and the component GUIDs are derived from the type
  string, so changing it would change every rebaked prefab. Switch only together with a prefab migration.

## Log

- 2026-10-06: brought under the workspace standard. Runtime split into Engine/Components/Systems/
  Resources, editor into Core/Engine/UI/Dev, one type per file, `[Alias]` on the four serialized types.
  Tests moved to `tests\` (path-only edits), gate captures (2 GB) to `dev\data\editor-gate\`, logo to
  `docs\`. All 375 xunit tests and 60/60 runtime checks pass as before; Code, Editor and all compile
  checks build. Not yet verified in the s&box editor (whitelist, alias loading of old prefabs): next
  step is `dev\editor-gate\run_gate.ps1` and opening weapon-importer-demo with the new namespaces.
