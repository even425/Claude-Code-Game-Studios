# Domain / Village

**Source**: 05 §2.3 (村庄与建筑) · 05 §3.3 · 不变量 I7, I12 · 基线 §12.1

## Owns — you are the single writer for

- The `Village` instance: `Id`, building instance set, resource stock
- `BuildingInstance`: `Id`, owning village id, building definition id,
  built state / construction progress, world position
- Building construction rules

## Does not own

- **What a building actually does.** 基线 §12.3 is 【待定】 and B1 in
  `production/07_技术决策与待确认事项.md` is an explicit acceptance item: an
  implementer may **not** assign a building a function, and in particular may not
  infer from its *name* that it changes combat or economic values. If a building
  is brought into phase 1, its purpose must first be written in `04` or `07`.
- Rewards arriving from a run — those come back through settlement, which writes
  village resources *by calling this module*.

## Phase 1 scope

- Only the nine confirmed buildings (基线 §12.1): 土屋 (starting), 烟囱, 井,
  围栏, 铁匠铺, 武器店, 服装店, 试炼塔, 防护罩.
- Only the three starting 土屋 need to be visibly usable. The rest exist as
  **definitions with an empty effect field** — not placeholder numbers
  (05 §3.3).

## Constraints

- **I7 — runtime state never writes back to static definitions** (基线 §13.6).
  A building *instance* mutates; the building *definition* does not.
- **Ownership is not nullable.** An orphaned `BuildingInstance` is a **save
  validation failure** with an understandable error, never a silent drop
  (基线 §13.7, 05 §4.2). "引用断了" has exactly three legal resolutions —
  reject the command, fail validation, or explicitly-declared-lossy logging.
  There is no fourth.

## Must not

Reference Godot in any form. See `../README.md`.
