# Domain / Settlement

**Source**: 05 §2.3（结算与回流）· 05 §6 · 04 §6 · 不变量 **I4 / I15** · 基线 §13.7 · 04 Q4（D-010）

> ## Why this is its own module and not a helper method
>
> 05 §6 makes settlement the **single unavoidable path** by which a run's
> results reach the village and the heroes. Making it a folder is what makes
> that structural rather than aspirational: there is nowhere else a result
> could legally be applied, so "some other code path also granted the reward"
> becomes visible instead of being discovered later.
>
> The first skeleton pass deliberately skipped this folder on the argument that
> settlement is "a collaborator". That argument was rejected — a collaborator
> with no home is a collaborator with no single writer.

## Owns — you are the single writer for

- Reading a `BattleResult` and turning it into village resource deltas, hero
  inventory deltas, and domain events
- **The idempotency flag** that guarantees a result is applied exactly once
- The 「结算要么完整应用，要么不应用」 guarantee（不变量 I15）

## Does not own

- **Defining the rewards.** What a result is worth belongs to
  `04_副本与自动战斗规则`; it is 【待定】 (T-004). This module applies what it
  is told, and writes no numbers.
- Deciding a battle outcome — that is `Combat/`.
- Deciding whether a run succeeded — that is `Dungeon/`.
- **Writing any of the data it applies.** It may only write through the module
  that owns it（不变量 I10）: village resources through `Village/`, hero inventory
  through `Heroes/`, events through `Events/`.

## Constraints

- **Idempotent, and the flag must be persisted.** Invariant I4: calling
  settlement twice on one `DungeonRun` must leave the second call with a delta
  of exactly zero — for resources, inventory and hero state alike. A boolean is
  not sufficient if a single run can produce several distinct settlements (see
  `04` §6); the flag must be able to distinguish *which* application has
  already happened.
- **All-or-nothing**（不变量 I15）: compute every delta in memory, validate,
  then commit once. Never a partial write. If a commit fails midway, the end
  state must be either fully applied or not applied at all.
- **A failed run is still settled.** Failure is a settlement with its own
  outcome, not an absence of settlement. What a failure is *worth* is
  **【建议】: nothing** (D-010 Q1) — and it still costs nothing extra.
- **Loading must not re-apply**（基线 §13.7）: 存档往返后，同一次结算的增量必须为零。
- **【待定】 — do not invent**: reward contents and failure conditions (T-004).
  Also 【待定】: the injection point for testing a mid-commit failure
  (`04` §12 Q4, registered as TP-001).

## Must not

Reference Godot in any form. See `../README.md`.
