# Domain / Heroes

**Source**: 05 §2.3 (英雄) · 05 §3.2 · 不变量 I1, I2, I6

## Owns — you are the single writer for

- `Hero` runtime state: `Id`, owning village id, current state slots,
  current activity (idle / daily activity / on a mission), inventory
  references, first-dungeon-clear flag, death-and-revival availability flag
- The rules that decide when a hero transitions between activities

## Does not own

- Combat numbers (→ `../Combat/`)
- Trade pricing or any economic value
- `Squad` and `Mission` membership — this module *answers* "is this hero
  available?" but does not decide membership (→ `../Quests/`)

## Constraints already fixed

- **I6 — death carries no persistent cost.** Resurrecting adds no stat loss, no
  time penalty, no item loss, no relationship penalty (基线 §4.6). A field
  expressing permanent loss is itself a violation. `HeroAgent.cs` is scheduled
  for a rewrite under D-001 precisely because it models the retired design.
- **Four orthogonal axes, not one "personality"** (基线 §4.1–§4.3): traits /
  gameplay preferences / current state / relationships stay separate.
  Preferences change weights and inclination, never feasibility (基线 §4.3).
- **【待定】 — do not fill these in**: state slot names and durations
  (基线 §4.4), trait entries, preference categories and weight tables
  (05 §8.1, baseline 待确认清单 #1–#4). Leave them absent.
- Hero creation flow is phase 1 minimal; the acquisition *form* is still
  【待定】 (基线 §10.3/§10.6).

## Must not

Reference Godot in any form. See `../README.md`.
