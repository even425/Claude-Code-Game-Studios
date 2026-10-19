# Domain / Combat

**Source**: 05 §2.3 (副本与战斗) · 05 §3.6 · 基线 §8.3/§8.5 · 验收项 D5

## Owns — you are the single writer for

- Executing the automatic battle for a run
- `BattleResult`: `Id`, owning run id, outcome (成功 / 失败), participating hero
  id snapshot, a *description* of output (item / resource references), and the
  battle log

## Does not own

- **Defining the rewards.** Output references are described here; the numbers
  and the reward contents belong to `04_副本与自动战斗规则`.
- Applying the result. Settlement reads a `BattleResult` and is the only thing
  permitted to turn one into resources and inventory.

## Constraints

- **`BattleResult` is immutable after creation** (05 §3.6). A correction is a new
  result plus an explicit replacement plus a recorded reason — never a
  mutation. At most one exists per run.
- **The battle log must reproduce every key event the outcome referred to**
  (基线 §8.5, acceptance item D5). "We rolled higher" is not a log.
- **Combat is fully automatic.** The player never issues an immediate attack.
- **Combat must be watchable on the map**, not a separate battle scene
  (基线 §8.3), and must be **legible** — the player can see who won, who lost,
  and why. That is a Presentation concern, but it constrains what this module
  records: if a reason is not in the log, the player cannot be shown it.
- **【待定】 — do not invent**: damage, hit, cooldown, status effects, AI target
  selection (基线 待确认清单 #13). Phase 1 has no combat formula in this
  repository. **Write no numbers.**

## Must not

Reference Godot in any form. See `../README.md`.
