<!-- sbox-standard: v1 | type: library | root: WeaponImporter -->
# Weapon Importer

**Standard: sbox-standard v1** (library, root namespace `WeaponImporter`). This package follows the package layout and
code standard in E:\.sbox\CLAUDE.md (loaded automatically), enforced by `sbox-check`. Run it before
calling work done.

s&box library `notpointless.chomnr_weapon_importer`.

## Package facts

Only what the code can't tell you: design decisions, engine gotchas found here, where test assets live.

- The importer core is `Editor/Core` (`WeaponImporter.EditorTools.Core.*`), not `Code/.../Core`: it is
  editor-only and uses System.IO, which the runtime whitelist blocks. It must stay engine-free:
  `dev\WeaponImporter.Core.csproj` (built by sbox-check) and `tests\WeaponImporter.Tests` compile it as
  plain .NET. Core files that use System.Numerics put `using Vector3 = System.Numerics.Vector3;` after
  the namespace line (s&box's global Vector3 shadows it).
- Runtime: `Code/WeaponImporter/Engine` (IK, hold-data parsing, character overlay; s&box maths types),
  `Components` (WeaponHold, WeaponViewmodel), `Systems` (WeaponHoldSystem), `Resources` (WeaponGripProfile).
  The four serialized types carry `[Alias]` with their pre-2026-10-06 names (`WeaponImporter.X`).
- `WeaponBaker` deliberately writes the old `__type` names into prefabs (component GUIDs derive from
  them); don't "fix" them without a prefab migration.
- Editor-side usings for sibling namespaces sit after the namespace line so they win over `using Editor;`.
- Whitelist gotchas found here (in-engine compiler is stricter than dotnet build): no Array.Clone,
  Type.IsPrimitive, OverflowException/InvalidDataException by name, ZLibStream, Environment.NewLine,
  Memory<T>, [GeneratedRegex]. More in `dev\notes\` (api-facts, engine-facts, failures, contracts).
- Test assets: real weapon files come from `WI_CORPUS` (default `C:\Users\chomnr\Desktop\weapons`);
  tests skip when missing. Pose/rig fixtures are in `tests\WeaponImporter.Tests\fixtures` (regenerated
  by the gate case `dev\editor-gate\cases_poses.json`).
- `Editor/Dev` (editor gate hooks) and `dev/`, `tests/` are gitignored: local only. The gate
  (`dev\editor-gate\run_gate.ps1`) runs cases inside a real s&box editor; output goes to
  `dev\data\editor-gate\<name>`. `tests\RuntimeTests` is a console self-check of the runtime maths.
- Compile checks: `dev\EngineCompileCheck.csproj` (Code+Editor), `dev\RuntimeCompileCheck.csproj`
  (Code only), `dev\ShipCompileCheck.csproj` (without Editor/Dev). Build them one at a time (shared obj).
- Used by the weapon-importer-demo game project (not in this workspace).
