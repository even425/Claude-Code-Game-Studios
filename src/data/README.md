# Data & Save Layer

**Source of truth**: 设计基线 V1.2 §13.6 / §13.7 · 05 §2.1, §2.5, §2.6 ·
不变量 I7, I11, I14, I15

## What belongs here

- **Static definitions** — what a thing *is*: hero definitions, building
  definitions, dungeon definitions
- **Runtime state serialization** — save/load DTOs
- **Save versioning and migration** — the version number and the migration entry
  point
- **Load validation and backup** — verify IDs and references *before* restoring;
  write to a temp file, then replace; keep the last valid save

## What must never live here

- **Gameplay rules.** Definitions describe; they do not decide.
- **Runtime mutation of a definition.** 基线 §13.6 and invariant I7: a run must
  leave every static definition's field set byte-identical. If saving a run
  changes a definition, the defect is here.
- Godot scene-tree serialization. **Never serialize the scene tree itself**
  (基线 §13.7) — explicit DTOs only.

## Definition vs. runtime state (05 §2.5)

Every content type is split into two models:

| Static definition (Definition) | Runtime state (Instance) |
|---|---|
| hero / building / dungeon definitions | hero / village / building instance / dungeon run |
| fixed at content time | mutable at runtime, **must** round-trip through a save |
| **must not** be written by a save | **must** be fully restorable |

Phase 1 deliberately introduces **no** attribute / affix / data-table indirection
layer. Fields live directly on the definition — one more layer would only make
the M1–M7 read path longer (05 §2.5).

## The save boundary

Exactly the runtime entities of 05 §3, no more and no less:

> Village · BuildingInstance · Hero state · Squad · Mission · DungeonRun ·
> BattleResult · DomainEvent · ChronicleEntry · SimClock

The save boundary is also the **idempotency surface**. Flags like
`SettlementApplied` are written to disk explicitly; they must never be
recomputed on load (05 §2.6, invariant I4).

## Load must reject, not silently repair (I11)

Validation runs **before** state restoration. A save with a broken reference
must **fail to load with an understandable error**, never half-restore. These
are the named cases (基线 §13.7):

- duplicated rewards · duplicated event consequences · lost or duplicated
  equipment · conflicting building positions · lost mission participants ·
  a finished event reverting to in-progress

**I15 — settlement is all-or-nothing.** If settlement fails partway, the total
delta to village resources and hero inventory is either **exactly 0** or
**exactly the complete delta the result declares**. There is no partial crediting
— "不发一半奖" (05 §4.2).

## 【待定】 — do not invent

RNG state persistence (基线 待确认清单 #24). The floor requirement is that
reload must not duplicate rewards or settlement. Whether a deterministic
continuation is also required is decided by a technical prototype (基线 §13.7).
Decide it there, not here.

## ⚠️ Directory-name collision on Windows

This folder shares a path with a leftover Python prototype folder `src/data/`
(`__pycache__` only, no source). Windows filesystems are case-insensitive, so
`src/data` **is** `src/Data`. The prototype's compiled Python is not part of the
game build and is scheduled for deletion — see the note in the M1 report.

## Must not

The "no Godot" rule of `../Domain/README.md` is scoped to Domain. This layer
**may** use Godot types (file I/O via `FileAccess`, `Resource`-backed
definitions) — but definitions read from disk are still static definitions and
still may not be mutated at runtime (I7).
