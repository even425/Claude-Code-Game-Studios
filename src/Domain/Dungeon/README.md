# Domain / Dungeon

**Source**: 05 §2.3 (副本与战斗) · 05 §3.5 · 不变量 I3, I4 · 基线 §8.2

## Owns — you are the single writer for

- `DungeonRun`: `Id`, dungeon definition id, originating mission id,
  participating hero **id snapshot**, current stage index, status
  (未进入 / 进行中 / 已完成 / 已失败), the `SettlementApplied` flag, linked
  battle result id
- Stage advancement inside a run
- Creating the `DungeonRun` once dispatch succeeds and passes legality checks

## Does not own

- Writing village resources or hero inventory. That is settlement's job, and it
  goes *through* `../Village/` and `../Heroes/`.
- Defining reward contents (基线 §8.3 numbers are 【待定】)

## Constraints

- **I3 — at most one `BattleResult` per run.** A run that has ended with no
  result is a **validation failure**, not a valid state (05 §4.2).
- **I4 — a run may be settled exactly once.** Consecutive settlement calls must
  produce a second-time increment of **exactly zero**. The
  `SettlementApplied` flag is **explicitly persisted** (基线 §13.7); it must
  never be recomputed after loading.
- **A dungeon must have a real result path back into village life** (核心玩法循环
  §3 stage F). A dungeon that is only an isolated battle screen fails the
  slice.
- **A dungeon is not a single encounter** — at least two stages (基线 §8.2).
- **Stages are not separate entities in phase 1.** They are an integer index
  inside the run. Upgrade to a real entity only if something needs to *reference*
  a stage (05 §3.5).
- **The hero id snapshot is deliberate.** Settlement must match *who went*.
- **【待定】 — do not invent**: dungeon structure, stage count, failure
  conditions, mission duration.

## Must not

Reference Godot in any form. See `../README.md`.
