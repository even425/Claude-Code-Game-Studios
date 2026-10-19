# ADR-0001 · Layered Architecture — Keep the Presentation Layer, Rewrite the Domain Layer

| | |
|---|---|
| **Status** | **Accepted** |
| **Date** | 2026-10-17 |
| **Deciders** | Developer |
| **Supersedes** | — |
| **Superseded by** | — |
| **Records decision** | D-001 in `production/07_技术决策与待确认事项.md` |
| **Enforces** | 设计基线 V1.2 §13.2, §13.3 · invariant I9 (05 §5.1) |
| **Guard** | `tests/unit/architecture/DomainLayerPurityTests.cs` |

---

## Context

`src/` contains a 2227-line, 7-file C# prototype. It compiles cleanly
(`dotnet build` → 0 errors, 0 warnings) and it runs. That is not the problem.

The problem is that it was written against a **design that has since been
retired**. The current baseline V1.2 states plainly that heroes are *always
present*, and that death and resurrection carry **no cost of any kind**
(基线 §4.6). The prototype encodes the opposite:

| Retired mechanism | Traces in the code |
|---|---|
| Earth-timezone daily schedule | `LoginAt` ×7, `IsOnlineAt` ×7, `UtcOffset` ×5, `LocalLoginAt` ×4, `PlayHours` ×5 |
| Online / on-duty / presence checks | `Online` ×23, `OnDuty` ×6, `Present` ×7 |
| Absence | `AbsentDays` ×12 |
| Away state | `Away` ×9 |
| Restlessness / unsettled mood | `RestlessDays` ×4 |
| Death halts the hero for a day | retracted outright |

A code audit concluded that this design is **permeated**, not layered: the
retired concepts appear at 23 sites for `Online` and 12 for `AbsentDays`
alone. It is not a feature that can be deleted — it is the shape of the
control flow.

Two structural problems exist independently of the retired design and would
have required a fix regardless (D-001 §4.1):

- **A1** — `Main.cs` (632 lines) is the giant `GameManager` that 基线 §13.4
  forbids.
- **A2** — `HeroAgent` mixes 41 lines of drawing into a domain object, so the
  domain cannot compile without Godot. That directly violates 基线 §13.3.

### The asset worth protecting

`VillageView.cs` — 497 lines, of which the audit judged roughly **470 lines
pure drawing** (45° isometric rendering, UI). It is very likely unrelated to the
retired design, and it is the single most valuable thing in the prototype.
Anything that throws it away spends far more than it saves.

---

## Decision

**Route B — keep the presentation layer, rewrite the domain layer.**

| Existing file | Disposition |
|---|---|
| `VillageView.cs` (497 lines) | Splits into the Presentation layer. **~470 lines to keep.** |
| `WorldClock.cs` (79 lines) | Keep the in-game time advancement. Drop the retired schedule logic. M3 needs a simulation clock regardless. |
| `HeroAgent.cs` (293 lines) | **Rewrite** the state portion. |
| `Main.cs` (632 lines) | Split: rendering → Presentation, command coordination → Application. |
| `HeroFactory.cs` (207 lines) | Keep the extraction logic, **remove the timezone weighting**. |
| `VillageLife.cs` (159 lines) | Audit pending; may be largely retainable. |
| `Chronicle.cs` (360 lines) | Audit pending. Debt **A3**: `Chronicle.Entry` has no event-provenance field, so acceptance items C1/C2 are structurally impossible to pass as written. |

### Scope of this milestone

This ADR records the **architecture**, and `src/{Presentation,Application,Domain,Data}/`
now exists as a skeleton. The existing 7 files are **deliberately left exactly
where they are**. Migrating them is the D-001 *execution* stage, not
engineering-structure setup, and moving `VillageView.cs` before its audit is
settled would put the most valuable asset at risk for no structural gain.

### Layer responsibilities

Taken verbatim from 基线 §13.2 and 05 §2.1 — this ADR does not restate or
extend them. Each folder under `src/` carries a README stating what it holds,
what it must not hold, and its dependency direction.

```text
Presentation  ──▶  Application  ──▶  Domain
      ▲               │                │
      └───── read-only queries + events ◀┘

Application ──DTOs──▶ Data (save / load / migrate / validate)
```

The domain layer is split by **which module owns which data**, never by screen
(05 §2.3). Each module is the single writer of its own data (invariant I10).

---

## Alternatives considered

### (a) Refactor in place — **rejected**

Strip the retired mechanics and patch the rest.

Rejected on the audit evidence: the retired design reaches 23 `Online` sites
and 12 `AbsentDays` sites, spread across the control flow. The coupling that
would have to be unwound is hidden, and the estimate for hidden-coupling
removal is the least reliable number in any refactor. It also leaves A1 and A2
in place, because those are structural, not content, problems.

### (c) Tear down and rebuild everything — **rejected, and clearly worse than (b)**

Rewrite the whole project against the current baseline.

This discards ~470 lines of drawing code that is unrelated to the retired
design — the one piece of the prototype with real, reusable value — and buys
nothing in exchange. It trades a bounded, well-understood problem (rewrite the
domain layer) for a larger unbounded one (re-derive rendering that already
works).

### (b) Keep presentation, rewrite domain — **adopted**

Presentation is the part that works and that the retired design does not
touch. The domain is the part that must change anyway. Separating them lets
each receive exactly the treatment it needs.

---

## Consequences

### The acceptance criterion

> **No file under `src/Domain/` may reference Godot.**

This is D-001's own acceptance criterion, and it is the crisp statement of
基线 §13.3. The domain layer is plain C#. If some domain type cannot be
expressed without a Godot type, that is not an obstacle to route around — it is
the finding, and the type belongs in another layer.

### How it is enforced

`tests/unit/architecture/DomainLayerPurityTests.cs`, running on every
`dotnet test` invocation, which CI runs on every push and pull request.

It scans every `.cs` file under `src/Domain/` for `using Godot`,
`using Godot.Collections`, `extern alias Godot`, `global::Godot`,
fully-qualified `Godot.SomeType`, and `[GlobalClass]`. Comments and string
literals are blanked before the scan, so prose about the rule — of which there
is a lot in this repository — cannot trip it.

Three things keep it from being a vacuous pass:

1. **`Scanner_DetectsGodotReference_InSyntheticSource`** feeds nine
   must-be-detected samples and seven must-not-be-detected samples through the
   scanner on source that never touches disk. It fails if a pattern stops
   firing, or if the comment/literal blanking starts producing false positives.
2. **`DomainFolder_Exists_GuardHasSomethingToScan`** fails if `src/Domain/`
   is moved or renamed, instead of quietly scanning nothing.
3. The result is asserted as a **formatted report string**, not a count, so a
   failure prints the offending file, line and text rather than "expected 0".

An empty `src/Domain/` is the legitimate state during M1 and is not a
violation — "the folder contains no code" cannot violate "the folder's code
references no Godot". Vacuity in that state is covered by items 1 and 2 above.

### Follow-up: a stronger gate (not done here)

Splitting `src/Domain/` into its **own assembly with no `GodotSharp`
reference** would move the check from build-time-scan to **compile error**,
which is strictly better and matches invariant I9's wording exactly ("the
domain assembly compiles and runs its rules tests with no Godot runtime
present").

It was not done in this milestone because it requires:

- a new `src/Domain/*.csproj` and edits to `Claude-Code-Game-Studios.csproj`
  (a `<Compile Remove>` plus a `ProjectReference`), i.e. changes to shipped
  build config; and
- re-checking the CI build step in `.github/workflows/tests.yml`, which
  currently runs `dotnet build Claude-Code-Game-Studios.csproj`.

That is a build-configuration change to existing files, not engineering
skeleton work, so it is proposed separately rather than bundled in.

### Other consequences

- **Domain unit tests need no Godot runtime.** They construct plain objects and
  assert on results. Needing a `Node` to write a domain test is itself a signal
  that the test — or the type — is in the wrong layer.
- **Migration order matters.** `VillageView.cs` should move last, after the
  domain it renders is stable, so the most valuable asset is not in flight
  during the risky part. Note debt **A4**: `VillageView.cs:71` currently drives
  the domain `Tick` from the rendering side. That control inversion must be
  corrected when it moves — the Application layer owns tick order (基线 §13.2).
- **Content debt stays separate.** B1–B7 in `07_技术决策与待确认事项.md` are
  tracked apart from this refactor and must not be folded into it.
- **【待定】 items stay empty.** 05 §0 forbids the implementer filling them.
  Phase 1 deliberately writes no combat numbers, no fatigue curve and no squad
  size constant. The stale `squad_max: 4` in `design/registry/entities.yaml`
  does **not** apply — D-002 places that registry outside this baseline.

---

## References

- `production/07_技术决策与待确认事项.md` — **D-001** (this decision) and §4.1
  architecture debt A1–A7
- `production/异世界村庄经营与英雄冒险游戏_设计基线_V1.2.md` — **§13.2** layered
  architecture, **§13.3** Node vs. domain object, §13.4 global services,
  §13.5 module communication, §13.6 static vs. runtime state, §13.7 saves
- `production/05_核心领域模型与模块边界_V1.0.md` — §2 layering and module
  boundaries, §2.3 module split, §2.4 command/event channels, §2.6 save
  boundary, §5 invariant **I9**
- `src/CLAUDE.md` — coding standards for this directory
- `tests/unit/README.md` — why `tests/unit/architecture/` is a deliberate
  exception to the "no filesystem in unit tests" rule
