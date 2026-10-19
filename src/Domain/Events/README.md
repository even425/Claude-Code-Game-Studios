# Domain / Events

**Source**: 05 §2.3 (领域事件与杂谈) · 05 §3.7, §3.8 · 不变量 I5 · 基线 §7.2/§7.4
· 基线 §11.4 · 验收项 C1/C2/C9, V4

## Owns — you are the single writer for

- `DomainEvent` records: event id, simulation timestamp, type, source entity id,
  participant entity id list, payload. **Immutable.** Domains create these when
  they complete a state change.
- `ChronicleEntry` (杂谈条目): entry id, **required** source event id, recorded
  simulation time, participants, text. **Immutable.**
- Generating and de-duplicating chronicle entries from domain events

## Does not own

- Deciding whether an event is worth recording — that judgement belongs to the
  subscribing module.
- Triggering the business effects of an event. The event *system* does
  trigger / lifecycle / coordination only; it does not absorb every business
  detail (基线 §11.4).

## Constraints

- **A chronicle entry without a resolvable source event is fabricated data and
  fails validation** — at load time *and* in tests (核心玩法循环 C9). I5: every
  entry maps to exactly one existing event id, and each event id backs at most
  one entry.
- **The event type is an open registry.** Adding a new event type must not
  require rewriting the core world loop (基线 §11.1).
- **Chronicle is generated from events that happened**, not emitted on a timer
  (基线 §7.2). Confirmed trigger families include: first dungeon clear, trade
  dispute, relationship souring, rare equipment acquired, injured / recovered,
  building completed / upgraded, village daily life, a major event, a hero
  summoned, native-story progress.
- **Chronicle is a text panel, not the narrative spine** (基线 §7.1).
- **V4 — event handlers must not overstep.** A subscriber updates UI,
  relationships or logs. It does not bypass a business service to modify another
  module's core data (I10).
- **A consequence must not be applied twice** because a save was reloaded or a
  settlement re-ran (基线 §13.7). Idempotency flags are persisted, not derived.

## Must not

Reference Godot in any form. See `../README.md`.
