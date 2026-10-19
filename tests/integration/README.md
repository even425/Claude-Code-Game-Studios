# Integration Tests

**Tests that cross a system boundary.** One or more layers, and therefore
usually the Godot runtime.

## When a test belongs here, not in `unit/`

| Signal | Put it in |
|---|---|
| Needs a `Node`, `SceneTree`, or anything else from the engine | `integration/` |
| Exercises a command travelling Presentation → Application → Domain | `integration/` |
| Exercises a save/load round trip touching more than one module | `integration/` |
| Verifies an invariant spanning two owners — e.g. I4, "settlement increments a second time by exactly zero" | `integration/` |
| Pure rules, pure objects, no engine | `unit/[system]/` |

I4 and I15 are the clearest examples. "Settlement is applied once" cannot be
proven inside any one module's unit test — the *point* is that the write reaches
village resources and hero inventory **once**, which is a cross-module claim.

## Layout

```text
tests/integration/
  [system]/            folder per boundary being crossed
    [System]IntegrationTests.cs
  ...
```

Name the folder after the **boundary**, not after one side of it —
`tests/integration/settlement/`, not `tests/integration/dungeon/`. The test
crosses two systems; whichever one it happens to start from is an
implementation detail.

## Naming

Same convention as unit tests (`.claude/rules/test-standards.md`), with the
integration suffix:

```csharp
class  SettlementIntegrationTests
method Settlement_RunTwice_SecondApplicationIncrementsNothing()
```

Class: `[System]IntegrationTests` · Method: `[Scenario]_[Expected]`

## Rules

- **Clean up after yourself.** A `Node` left alive is an orphan; in gdUnit4 that
  can be an exit code rather than a warning. Free every node you create, even
  when the assertion fails — use a teardown, not a trailing line.
- **Determinism still applies.** Inject the clock and the randomness. An
  integration test that depends on wall-clock timing is a flake waiting for a
  bad night in CI.
- **Assert on observable outcomes, not on internals.** Assert that the village
  resource total moved, not that a private field was assigned.
- **Integration tests do not replace unit tests.** If a rule can be tested
  without the engine, it must be — put it in `unit/` and keep this folder for
  the seams.

## Running

Same command as unit tests — one project, one runner:

```bash
dotnet test tests/Claude-Code-Game-Studios.Tests.csproj
```

## Status

**Empty by design.** M1 establishes the engineering skeleton; there is no
cross-system behaviour to assert until the first slice exists. The folder is
here so the first integration test lands in the right place rather than in
`unit/`.
