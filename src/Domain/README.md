# Domain Layer (模拟 / 领域层)

**Source of truth**: 设计基线 V1.2 §13.2 / §13.3 / §13.5 / §13.6 · 05 §2.1–§2.5 ·
不变量 I9 · **D-001 acceptance criterion**

> ## ⛔ THE ONE HARD RULE
>
> **No file under `src/Domain/` may reference Godot.** Not `using Godot`, not
> `using Godot.Collections`, not `global::Godot.Vector2`, not `[GlobalClass]`.
>
> The domain is plain C#. If a domain type needs a Godot type to be expressed,
> **the boundary is broken and that type belongs in another layer** — that is the
> finding, not a problem to work around.
>
> This is enforced, not documented-and-hoped-for:
> `tests/unit/architecture/DomainLayerPurityTests.cs` scans every `.cs` file
> here on every `dotnet test` run, and CI runs it on every push and PR.

## What belongs here

Rules and state. Nothing else.

- Hero behaviour rules, current-state slots, current activity
- Battle rules and the immutable `BattleResult`
- Mission / squad lifecycle rules
- Building rules
- Relationships — **phase 1 is a placeholder only** (05 §7)
- Simulation clock and per-step module scheduling order
- Production of domain events

## What must never live here

- **Any Godot type.** No `Node`, no `SceneTree`, no `Resource`, no `Vector2`,
  no `[Signal]`, no `[Export]`, no `GetTree()`, no `Callable`.
- Rendering, input, UI, audio.
- Save-file I/O, versioning and migration — that is the Data layer. The domain
  does not know saves exist.
- **Placeholders for 【待定】 items.** 05 §0 is explicit: 【待定】 must not be
  filled in by the implementer. If a value is 待定, leave the field absent or
  empty and record the question in `production/07_技术决策与待确认事项.md`.
  Phase 1 deliberately introduces no combat numbers, no fatigue curve and no
  squad size constant (05 §2.5, §3.4, §8).

## Dependency direction

```text
Application ──commands──▶ Domain ◀──subscribe── Presentation
     │                        ▲
     └──DTOs──▶ Data ─────────┘  (restore state)
```

The Domain layer references **nothing** in this repository's other layers. Its
dependencies are the BCL only. Invariant I9 states the intent precisely: the
domain assembly must be compilable and its rules testable with no Godot runtime
present.

## Modules — each is the single writer of its own data (05 §2.3)

Split by **who owns which data**, not by screen. See each folder's README for
that module's exact remit.

| Folder | Owns (唯一写者) | Explicitly not |
|---|---|---|
| `Clock/` | Simulation time, day sequence + intra-day progress, **per-step module execution order** | deciding what any hero does |
| `Heroes/` | Hero identity, activity, state slots, inventory references | combat numbers, trade pricing |
| `Village/` | Village instance, building instances and their site state, village resource stock | what a building actually *does* (基线 §12.3 【待定】) |
| `Quests/` | Squad legality, mission lifecycle, mission phase progress | what happens inside a dungeon run |
| `Dungeon/` | Dungeon run instances, stage advancement, completion-condition判定 | combat rules, battle results, village resources |
| `Combat/` | Executing the automatic battle, producing the immutable `BattleResult` | stage advancement, the rewards themselves |
| `Settlement/` | Applying a result to village / heroes / events, **exactly once** | defining what the result is worth |
| `Events/` | Domain event records, 杂谈 generation and de-duplication | deciding whether an event is worth recording |

**Eight modules, eight folders.** 05 §2.3 (V1.0.2) lists exactly these eight
rows, one 唯一写者 each — invariant I10 is what makes the split meaningful.

`Settlement/` is a module rather than a shared helper because 05 §6 makes it
the single unavoidable path by which a run's results reach the village. Giving
it a folder is what makes that structural: there is nowhere else a result could
legally be applied. It writes only *through* the module that owns each piece of
data.

`Dungeon/` and `Combat/` are separate because `04` already separates a run's
lifecycle (§3/§4) from the battle rules interface (§5), and 05 §3.6 makes
`BattleResult` its own entity. Folding them back together would give one module
two writers and break I10.

## Entity conventions (05 §3.1)

- Every runtime entity carries a stable `Id`, never reused, never changed.
- Cross-entity references are **IDs**, not object references — save files,
  validation (I13) and test doubles all depend on this.
- No entity holds a reference to a Presentation node.
- Domain objects are **not** Godot `Node`s (基线 §13.3). One domain entity
  corresponds to at most one presentation node, which owns itself.
