# Presentation Layer

**Source of truth**: 设计基线 V1.2 §13.2 (表现层) · 05 §2.1 · D-001 决定 B ("保住呈现层")

## What belongs here

Everything that talks to the Godot scene tree.

- Map, 45° isometric camera (`Camera2D`), zoom and follow logic
- Scene nodes for heroes, monsters and buildings
- Animation, particle effects, sound
- UI: HUD, panels, the 杂谈 (Chronicle) text panel
- Input capture — turning raw input into **player intents**, never into state writes

## What must never live here

- **Any gameplay rule.** A `.cs` file in this folder that decides *what happens* is
  in the wrong layer.
- **Any direct write to domain state.** Dragging a hero in the UI does not set
  `hero.CurrentActivity`. It issues a command through the Application layer, and
  the Presentation node refreshes itself when the resulting domain event comes
  back.
- `using ClaudeCodeGameStudios.Domain` is *expected* here — but only to **read**
  snapshots and to **subscribe** to domain events.

## Dependency direction

```text
Presentation  ──▶  Application  ──▶  Domain
      ▲                │                │
      └──── queries/events ◀────────────┘
      (read-only, one way)
```

Presentation may know about Domain. Domain may **never** know about Presentation.

## Relationship to the existing code

`src/VillageView.cs` (497 lines, pure drawing) is the highest-value asset in this
refactor and belongs in this layer. **It has not been moved** — D-001 execution
stage, not M1. Do not treat its current location in `src/` as its final home.

Note debt **A4**: `VillageView.cs:71` currently drives the domain `Tick`, which is
control inversion against §13.2. When it is migrated, the Application layer must
own tick order.
