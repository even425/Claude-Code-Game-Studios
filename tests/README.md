# Test Infrastructure

**Engine**: Godot 4.7.2 (.NET build) · **Language**: C#
**Test Framework**: gdUnit4 (C# — NuGet `gdUnit4.api` + `gdUnit4.test.adapter`)
**CI**: `.github/workflows/tests.yml`
**Setup date**: 2026-10-17

## This project is C#, and that changes the framework

The Godot gdUnit4 addon at `res://addons/gdUnit4/` and its GDScript runner
(`godot -s res://addons/gdUnit4/bin/GdUnitCmdTool.gd`) are the **GDScript**
integration. They do **not** apply here. The C# integration is a separate .NET
test project driven by `dotnet test`:

| | GDScript (not this project) | C# (this project) |
|---|---|---|
| Distribution | addon copied into `res://addons/gdUnit4/` | NuGet packages |
| Runner | `godot -s .../GdUnitCmdTool.gd` | `dotnet test` |
| Test code | `.gd` files | `.cs` files in a separate `.csproj` |
| Pass / fail exit | 0 / 100 | 0 / 1 |

Anything in this repo that still names the GDScript runner is wrong for this
project, not merely unconfigured.

## Directory Layout

```
tests/
  Claude-Code-Game-Studios.Tests.csproj   # the test project
  unit/                                  # unit tests — tests/unit/[system]/
  integration/                           # cross-system tests
  smoke/critical-paths.md                # manual gate read by /smoke-check

production/qa/
  evidence/                              # screenshots + sign-off records
```

> Manual evidence lives under `production/qa/evidence/`, not `tests/` — that is
> where `/story-done`, `/smoke-check` and `/test-evidence-review` look.

## Running Tests

```bash
dotnet test tests/Claude-Code-Game-Studios.Tests.csproj
```

Exits **0** when every test passes, **1** when any fails.

> **`dotnet` is not on `PATH` in every shell.** If `dotnet: command not found`,
> run `export PATH="/c/Program Files/dotnet:$PATH"` first, or use the full path
> `"/c/Program Files/dotnet/dotnet.exe"`.

### Gate self-check

A green suite means nothing until you have seen it fail. `tests/unit/FrameworkTests.cs`
documents a one-line test to add temporarily to confirm the runner reports
`失败: 1` and exits 1. Last confirmed 2026-10-17.

### Packages — do not "upgrade" these casually

```
Microsoft.NET.Test.Sdk   17.11.1
gdUnit4.api              5.1.0-rc5     ← prerelease, required
gdUnit4.test.adapter     3.1.1
GodotSharp               4.7.2
```

`gdUnit4.test.adapter 3.1.1` depends on `gdUnit4.api >= 5.1.0-rc5`. Pinning the
stable `5.0.0` fails the restore with **NU1605 package downgrade**. The rc is not
optional here.

## Test Naming

C# convention (`.claude/rules/test-standards.md`):
class `[System]Tests`, method `[Scenario]_[Expected]` —
`VillageTests.cs` → `Village_UpgradeRequiresMoreMaterial()`

File: `tests/unit/[system]/[System]Tests.cs`

## The test project is excluded from the game build

`Claude-Code-Game-Studios.csproj` carries `<Compile Remove="tests/**/*.cs" />`.
Godot.NET.Sdk globs `**/*.cs`, so without it the test sources — which reference
GdUnit4 — would be pulled into the **game** assembly and fail to compile there.

## Known cleanup

`tests/_spike.csproj` and `tests/_spike_test.cs` are leftovers from discovering
the C# gdUnit4 API on 2026-10-17; file deletion was refused by the sandbox.
They are excluded from this project in the `.csproj`. **Delete both files and
then remove the TEMPORARY EXCLUSIONS `<ItemGroup>` from this `.csproj`.**

## Story Type → Test Evidence

| Story Type | Required Evidence | Location |
|---|---|---|
| Logic | Automated unit test | `tests/unit/[system]/` |
| Integration | Integration test OR playtest doc | `tests/integration/[system]/` |
| Visual/Feel | Screenshot + sign-off | `production/qa/evidence/` |
| UI | Screenshot of each screen touched | `production/qa/evidence/` |
| Config/Data | Smoke check pass | `production/qa/smoke-*.md` |

> At `qa.level: minimal` tests are **waived** (advisory). The retained
> screenshot for Visual/Feel and UI stories is **not** waived.

## CI

Tests run on every push to `main` and every pull request. A failed suite blocks
merging.
