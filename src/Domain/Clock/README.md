# Domain / Clock

**Source**: 05 §2.3（模拟时钟与调度）· 05 §3.9 · 03 §2 · 基线 §13.2 · D-006 / D-009 R-2

## Owns — you are the single writer for

- Advancing simulation time
- The day sequence number and intra-day progress（**日序号 + 日内进度**，D-009 R-2）
- The simulation step counter — how many steps have been processed
- The time-scale setting
- **The per-step module execution order** — which module runs first inside one
  simulation step

> ## Why this folder exists at all
>
> This module was **missing** from the first pass of the skeleton. 05 §2.3 lists
> it as a module with a single-writer remit, and leaving it homeless would have
> meant two things were true at once: the schedule order would have had to live
> in the Application layer (breaking I10), and 03's decision loop would have had
> no defined caller.

## Does not own

- **Deciding what any hero does.** 03 §3's decision flow belongs to `Heroes/`;
  this module only decides *when* that flow runs.
- Battle rules, village state, mission lifecycle — those belong to their owners.

## Constraints

- **Day boundary is a function of simulation time, and the two must stay
  mutually derivable.** 日序号 and 日内进度 must remain functions of each other;
  they are not allowed to evolve independently (05 §3.9.1).
- **Simulation must be separable from frame rate**（基线 §13.1）. Rendering runs
  at engine frame rate; rules run on this clock. A behaviour that depends on the
  frame rate is a defect, and acceptance item **A-01（帧率无关性）** exists to
  catch it.
- **Time keeps running during a dungeon run.** D-006: a run does not freeze the
  village. Heroes not in the squad continue their day; the clock is not paused
  for them.
- **【待定】 — do not invent**: the relationship between real time and simulation
  time, and the time-scale value itself (T-001). Phase 1 provides the structure
  and the injection point. **Write no numbers.**
- 【待定】行动耗时与频率 (T-002) belongs to `Heroes/`, not here — but this module
  is what schedules it.

## Must not

Reference Godot in any form. See `../README.md`. In particular there is no
`SceneTreeTimer`, no `_Process`, no `Engine.GetTicksMsec()` — a clock that can
only be read from the engine is not testable.
