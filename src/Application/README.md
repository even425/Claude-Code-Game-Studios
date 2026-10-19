# Application Layer

**Source of truth**: 设计基线 V1.2 §13.2 (应用层) · 05 §2.1 · 05 §2.4 (命令通道)

## What belongs here

- **Player command entry points** — the only sanctioned way outside code gets in.
  A command that fails validation is rejected here, before the Domain sees it.
- **Use-case coordination** — dispatch a command to the owning domain module,
  then let the resulting events propagate.
- **Game time and speed control** — driving the simulation clock forward. Note
  05 §3.9: the clock *object* is domain-owned, but *pacing it* is this layer's job.
- **Scene and session lifecycle** — starting a run, loading a save, ending it.

## What must never live here

- **Rules themselves.** If you find yourself writing an `if` that encodes a
  gameplay outcome, that `if` belongs to the domain module that owns the data.
- **Fields on domain objects.** The Application layer holds references; the
  domain modules hold ownership.
- **A god object.** §13.4 forbids a `GameManager` containing all business logic,
  and D-001 debt **A1** records that `src/Main.cs` (632 lines) is exactly that.
  If you add a global service here, it must be genuinely global (clock, session,
  save path) and contain no rules.

## Dependency direction

```text
Application  ──▶  Domain
     │
     └──▶ Data (save/load, via DTOs)
```

Application knows about Domain and Data. Neither knows about Application.
Presentation knows about Application; Application does **not** reach into
Presentation.

## The three channels (05 §2.4)

| Direction | Channel | Legal use |
|---|---|---|
| Presentation / Application → Domain | **Command** | the only way to change domain state |
| Domain → Domain | **DomainEvent** | "this happened"; subscribers do their own work |
| Domain → Presentation / Application | **Query** or event | read-only snapshot for display |

Prohibited, per 05 §2.4 and §13.5:
1. Presentation writing a domain field directly.
2. Module B writing module A's data. Go through a command or an event so the
   **owning** module performs the write.
3. An event handler bypassing the business service to mutate core data
   (acceptance item V4).
