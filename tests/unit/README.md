# Unit Tests

**Tests for a single system, in isolation.** No engine, no scene tree, no
filesystem, no network — unless the test is explicitly an architecture test
(see below).

## Layout

```text
tests/unit/
  [system]/            one folder per system, matching the layer folder
    [System]Tests.cs
  architecture/        meta-tests about the repository's own structure
```

`[system]` matches the module it exercises: `heroes/`, `village/`, `quests/`,
`dungeon/`, `combat/`, `events/`, `data/`. Prefer mirroring the domain folder
name — a reader looking for a behaviour should be able to guess the file.

## Naming

From `.claude/rules/test-standards.md`:

```csharp
class  VillageTests
method UpgradeBuilding_WithInsufficientResources_IsRejected()
```

- Class: `[System]Tests`, in a file of the same name
- Method: `[Scenario]_[Expected]` — the name says **what happened** and **what
  was expected**, not the method under test
- Every test has an explicit arrange / act / assert structure

## What a unit test may rely on

- **Dependency injection**, never a singleton. A test that needs the clock takes
  an `ISimClock`; it does not reach for `Instance` (D-001 debt **A7** — four
  unseeded `static readonly Random` fields are exactly how this rule gets
  broken).
- **Injected randomness.** Assert on an injected `Random` or a seeded source.
  A test that cannot be made deterministic belongs in a spike, not here.
- **No hardcoded magic numbers** except in boundary-value tests where the exact
  number *is* the assertion.

## Domain tests have one extra constraint

`src/Domain/` cannot reference Godot (see `src/Domain/README.md`). That means
domain unit tests need **no Godot runtime at all** — they construct plain
objects, call rules, assert on results. If you find yourself needing `Node`,
`SceneTree` or `Resource` to write a domain test, the test belongs in
`tests/integration/`, and the thing it is testing probably belongs in a
different layer.

## `architecture/` is a deliberate exception

`tests/unit/architecture/` contains meta-tests that inspect **the repository's
own source** — currently `DomainLayerPurityTests.cs`, which fails the build if
any file under `src/Domain/` references Godot.

These read the filesystem, which the general rule "unit tests must not depend
on external state" forbids. The exception is justified because:

- they test **no game behaviour** — there is nothing hermetic to be;
- a filesystem scan is the only way to see a layering violation, which is a
  property of the source tree rather than of a runtime object;
- they read the checkout that is running them, not an external service;
- they are fast and deterministic, and they fail loudly rather than silently
  skipping.

Keep this folder to structural guards. If a test here starts caring about
gameplay, it belongs in `tests/unit/[system]/`.

## Running

```bash
export PATH="/c/Program Files/dotnet:$PATH"    # dotnet is not always on PATH
dotnet test tests/Claude-Code-Game-Studios.Tests.csproj
```

Exit **0** = pass, **1** = fail. CI runs this on every push and PR.

> **A green suite is only evidence once you have seen it go red.** Add a
> deliberately failing assertion, confirm `失败: 1` and exit 1, then remove it.
> `FrameworkTests.cs` documents the procedure.
