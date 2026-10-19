# Domain / Quests

**Source**: 05 §2.3 (队伍与任务) · 05 §3.4 · 不变量 I2, I10 · 基线 §2.2

## Owns — you are the single writer for

- `Squad`: `Id`, member hero **id** list, associated mission id
- `Mission`: `Id`, target dungeon definition id, status
  (未开始 / 进行中 / 已完成 / 已失败), participating hero id list, current stage
- Squad legality rules and the mission lifecycle
- Rejecting an invalid dispatch command (unknown dungeon, unavailable hero)

## Does not own

- Anything inside a dungeon run (→ `../Dungeon/`)
- Hero availability rules — you *consult* `../Heroes/`, you do not rewrite a
  hero to "make" it available

## Constraints

- **The player alone decides squad composition** (基线 §2.2). Deciding who goes
  does **not** mean steering each step of the battle (核心玩法循环 §4.1) — that
  boundary is the Application/Domain line, not this module's business.
- **I2 — a hero may hold at most one in-progress mission at a time.** Asserted
  as `Count(mission where status == 进行中 and h in heroes) <= 1` for every
  hero `h`. Dispatch must validate this.
- References are **ID snapshots**. Do not derive squad members by reading
  whatever the squad currently holds, and do not resolve them lazily — 05 §3.5
  explains why: a result must correspond to *who went*, not to *who is in the
  squad at read time*.
- **【待定】 — do not invent**: squad size cap, role composition, entry
  conditions. 05 §3.4 records *only* the member id list for this reason. Do not
  write a `squad_max` constant. (Note the stale `squad_max: 4` in
  `design/registry/entities.yaml` — D-002 marks that registry as **not part of
  this baseline**.)

## Must not

Reference Godot in any form. See `../README.md`.
