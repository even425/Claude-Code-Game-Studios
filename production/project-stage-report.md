# Project Stage Analysis Report

**Generated**: 2026-10-17
**Stage**: Concept (configured) / **Pre-Production (observed)**
**Stage Confidence**: CONCERNS — 25 of 49 catalogued steps returned `NO_CHECK`, and the code root is polluted with 59 orphaned files from a dead prototype, which makes the "identify major systems" step unreliable
**Analysis Scope**: Full project
**Config**: `modes.rigor: minimal`, `modes.automation: autonomous`, Godot 4.7.2 / C#

---

## Executive Summary

The project has a playable Godot build: a 45° isometric village that runs its own
clock, a population that logs in and out on Earth-timezone schedules, and daily
events that change state. What it does not have is a single unit test of game
logic — 2173 lines of C#, and the only test in the suite is `1 + 1 == 2`.

**The dominant finding of this scan is a tier fact, not a content gap.** At
`workflow: minimal`, absent GDDs, art bible, UX specs, ADRs, epics and sprint
plans are explicitly **not** gaps — `design/game-brief.md` plus a configured engine
is the normal state, and the brief's build order *is* the plan. The
`artifact-check.sh` output lists 20 `ABSENT` steps; **roughly 18 of those are
correct at this tier and must not be "fixed".** A future session that reads only
that table will spend a day writing documents nobody asked for.

The four real findings are all non-document and all invisible to the catalog scan.

**Current Focus**: M0 — "the village lives on its own" (clock, timezone schedules, daily events with consequences). Implemented and verified 2026-10-17.
**Blocking Issues**: none. Nothing here prevents further code work.
**Estimated Time to Next Stage**: not applicable at `minimal` — `/gate-check` does not run, so `project.stage` stays where `/start` put it until the tier is raised.

---

## ⚠️ Read this before acting on the ABSENT list

From `.claude/skills/project-stage-detect` step 3, at `workflow: minimal`:

> a `design/game-brief.md` + engine present is the normal state. Do NOT flag absent
> GDDs, art bible, UX specs, ADRs, epics or sprint plans as gaps: the brief's build
> order is the plan, and the expected next step is code.

So the following are **expected and correct**: `design/gdd/game-concept.md`,
`design/art/art-bible.md`, `design/gdd/systems-index.md`,
`docs/architecture/architecture.md`, `design/accessibility-requirements.md`,
`design/ux/*`, `production/epics/*`, `production/sprints/*`,
`production/security/*`, `production/qa/playtests/*`.

**Do not create them.** Raise `modes.rigor` first if the project actually wants them.

---

## Completeness Overview

### Design Documentation
- **Status**: complete *for this tier*
- **Files Found**: `design/game-brief.md` (the design record at `minimal`), `design/registry/entities.yaml`
  - GDD files in `design/gdd/`: 0 — **correct at `minimal`**
  - Narrative docs in `design/narrative/`: 0 — correct at `minimal`
  - Level designs in `design/levels/`: 0 — correct at `minimal`
- **Key Gaps**: none at this tier.
  - ⚠️ **Caveat on the brief, not a gap**: `game-brief.md` has grown to ~3000 words
    from a developer whose own contribution is a few hundred. Much of it was
    written by the assistant, not decided by the developer. See *Design Debt*
    below — this is a real problem, and it is not one the catalog detects.

### Source Code
- **Status**: functional prototype
- **Files Found**: **7 C# files**, ~2173 lines in `src/` (Godot code root)
  - ✅ `VillageView.cs` — 45° isometric renderer, hand-drawn `_Draw`, depth sorted
  - ✅ `Main.cs` — scene tree built in code; squad, expeditions, construction
  - ✅ `VillageLife.cs` — daily ambient events (M0)
  - ✅ `WorldClock.cs` — autonomous clock
  - ✅ `HeroAgent.cs` — hero state, schedules, work rate
  - ✅ `HeroFactory.cs` — summon, world archetypes, timezone assignment
  - ✅ `Chronicle.cs` — the 杂谈 text panel
- **Key Gaps**:
  - [ ] **59 orphaned `.pyc` files in `src/`** — eight dead subdirectories
        (`audio/`, `battle/`, `core/`, `data/`, `level/`, `narrative/`, `ui/`,
        `visual/`) contain nothing but `__pycache__` bytecode from the abandoned
        Python 「洞天修士」prototype (`chapter1_battle`, `realm`, `equipment`,
        `save_system`). Sources were deleted; the caches were left behind **in the
        code root**. This makes file counts and system identification unreliable —
        including for this report, and for any agent that globs `src/`.

### Architecture Documentation
- **Status**: n/a at `minimal` (no ADRs required)
- **ADRs Found**: 0 — **correct at `minimal`**
- **Note**: `docs/architecture/tr-registry.yaml` is a technology-radar registry,
  not an ADR.
- **Key Gaps**: none at this tier.

### Production Management
- **Status**: n/a at `minimal`
- **Found**: `production/qa/evidence/` (5 retained screenshots), `production/session-state/active.md`,
  `production/stage.txt`. No sprints, no milestones — **correct at `minimal`**;
  the brief's build order is the plan.
- **Key Gaps**: none at this tier.

### Testing
- **Status**: **effectively 0% of game logic**
- **Test Files**: 1 real test (`tests/unit/FrameworkTests.cs`), plus a leftover
  `tests/_spike.csproj` / `tests/_spike_test.cs` from the framework spike
- **Gate status**: `dotnet test tests/Claude-Code-Game-Studios.Tests.csproj`
  → **passed 1 / failed 0**, verified by execution. The gate works (it was
  confirmed red once, per the note in the test file).
- **Coverage by System**: **0 of 7 source files have any test**
- **Key Gaps**:
  - [ ] **No test covers game logic.** `CLAUDE.md` requires
        "Write tests first when adding gameplay systems." Two real bugs on
        2026-10-17 were pure-function bugs that a test would have caught:
        - `Chronicle` printed the literal token `{x}` into the UI, because the
          two-person slot was added to templates but not to the substitution
          function.
        - The stall-test probe compared `Hour` deltas; `Hour` wraps 23.9 → 0.0
          at midnight, so the difference goes negative and reporting stops forever.
              Both of these are deterministic, Godot-free, and testable today.

### Prototypes
- **Active Prototypes**: 3 in `prototypes/` — **all three undocumented**
  - ⚠️ `cunzhang-yu-maoxianzhe-concept/` — no README or REPORT
  - ⚠️ `洞天修士-combat-concept/` — no README or REPORT
  - ⚠️ `洞天修士-combat-unity/` — no README or REPORT
- **Key Gaps**:
  - [ ] None blocking. The session hook flags these at every startup, which is
        noise: two are dead experiments and one is the lineage of the Python
        bytecode still polluting `src/`. A one-line README each would stop the
        recurring flag.

---

## Stage Classification Rationale

**Why Pre-Production (observed)?**

The heuristic table says: *Engine configured, code root has <10 source files* →
Pre-Production. Godot 4.7.2 / C# is configured and verified, and `src/` has 7
C# files.

**Configured vs. observed.** `project.yaml` says `stage: Concept`, as does
`production/stage.txt`. **This is an expected lag, not a contradiction**: at
`minimal`, nothing runs `/gate-check`, so the configured stage stays where
`/start` set it. The observed stage is what the artifacts support.

**Confidence: CONCERNS**, not PASS, for two reasons:
1. `artifact-check.sh` returned **25 `NO_CHECK`** steps out of 49. Those are the
   steps with no file-existence test, so this report cannot speak to them —
   presence checks have a hard ceiling here and should not be read as "fine".
2. The code root contains 59 orphaned `.pyc` files in eight directories that
   *look* like systems with 5+ files. Any count-driven classification —
   including the one above — is reading partly against noise.

---

## Gaps Identified

### Critical Gaps (block progress)

**None.** Nothing here prevents further code work, which is the correct next step
at this tier.

### Important Gaps (affect quality/velocity)

1. **Zero unit tests on 2173 lines of game logic**
   - **Impact**: the project's own standard says tests come first for gameplay
     systems. Two bugs today were pure-function bugs that a test would have
     caught. The testable surface does not need Godot at all.
   - **Suggested action**: unit-test the deterministic, Godot-free functions —
     `Chronicle` slot substitution (must reject unknown tokens), `WorldClock`
     day rollover and `Clock` formatting, `HeroAgent.IsOnlineAt` midnight
     wrap-around, `HeroFactory` stat floor and timezone assignment.

2. **`commands.smoke` is a gate that cannot fail meaningfully**
   - **Impact**: `godot --headless --quit-after 5` exits after 5 frames. For a
     C# project it proves nothing — the same failure class as headless
     `--build-solutions`, which exits 0 without building anything (documented in
     `docs/engine-reference/godot/VERSION.md`). A green smoke run is not evidence.
   - **Suggested action**: replace with a real check — `dotnet build` plus a
     short `--selftest` run, so the smoke command actually exercises the code.

3. **`commands.build` is `[TO BE CONFIGURED]`**
   - **Impact**: no export preset, so there is no build target. The comment in
     `project.yaml` is correct that the preset name is project-defined — but it
     stays unset until someone creates one in the editor.
   - **Suggested action**: not urgent. Set it via `/settings` when the project
     first exports for Windows.

### Nice-to-Have Gaps

4. **59 orphaned `.pyc` in the code root** — see *Source Code* above.
5. **3 undocumented prototypes** — see *Prototypes* above.

### Not a gap, but real: design debt in the brief

`design/game-brief.md` is the design record at this tier, so whatever lands in it
carries the weight of "decided". Reviewing it on 2026-10-17:

- The developer's own contributions: the setting (transmigrated village chief,
  a system that summons players from Earth who believe it is a game), the
  personality-not-a-diode correction, the timezone correction, the death rule
  (one day offline), the local-NPC second content line, the transaction
  betrayal chain, and the visual direction (three thatched huts → chimney →
  well → fence → smithy, 45° isometric).
- Written by the assistant without the developer having decided it: the module
  table M0–M5, the building dependency web, the three-layer economy, the
  two-axis personality grid, the "scarcity inversion", the 10-person milestone.

**Pattern worth naming, because it recurred**: on 2026-10-17 the developer said
「你这个不要写得太死…感觉你怎么也想不通」 after a casual observation was promoted
into a measured curve, a milestone, and a named principle. The brief is where
those land and become load-bearing. **A future session should treat anything not
traceable to something the developer wrote as a hypothesis, not a decision.**

---

## Recommended Next Steps

### Immediate Priority

1. **Unit-test the pure logic** — effort S
   - Suggested skill: manual (write `tests/unit/[system]/` files directly)
   - Why first: it is the only item where a defect is currently both plausible
     and invisible, and two of today's four bugs were exactly this shape.

2. **Fix `commands.smoke`** — effort S
   - Suggested skill: `/settings`
   - Why second: a command in `project.yaml` that cannot fail is worse than no
     command, because it invites a future session to read green as pass.

### Short-Term

3. **Clean `src/`** — effort S. Remove the eight `__pycache__`-only directories;
   they are the residue of a dead prototype and they sit in the code root.
4. **`/playtest-report`** — effort S. There is a playable build; the polish
   phase wants `production/qa/playtests/*.md`. Formalising the 2026-10-17 M0
   playtest would also capture what was verified and how.
5. **`/code-review`** — effort M. 2173 lines written today, unreviewed.

### Medium-Term

6. Continue the brief's build order (M1 personality-as-rules → M2 dungeons as
   real places → …). **Wait for developer feedback on M0 before starting M1** —
   building ahead of feedback is what produced the three barren prototypes.
7. Set `commands.build` once an export preset exists.
8. Decide whether `modes.rigor` should stay `minimal`. If it stays minimal, the
   `ABSENT` list above should never be worked through.

---

## Follow-Up Skills to Run

- `/code-review` — 2173 unreviewed lines
- `/playtest-report` — a playable build exists and no playtest report is recorded
- `/soak-test` — a soak was run ad-hoc on 2026-10-17 (21 in-game days); the
  framework has a proper one
- `/settings` — fix `commands.smoke`
- `/reverse-document concept prototypes/[name]` — only if the prototypes are
  worth keeping; two look disposable
- **Not recommended at this tier**: `/map-systems`, `/art-bible`,
  `/create-epics`, `/sprint-plan`, `/architecture-decision`. All of these are
  gated on raising `modes.rigor` first.

---

## Appendix: File Counts by Directory

```
design/
  game-brief.md        1   (the design record at minimal)
  registry/            1   entities.yaml
  gdd/                 0   correct at minimal
  narrative/           0   correct at minimal
  levels/              0   correct at minimal
  art/ ux/ assets/     0   correct at minimal

src/                          (Godot code root)
  *.cs                7   ~2173 lines — all real
  audio/ battle/      8   orphaned .pyc only  ← dead 洞天修士 Python prototype
  core/ data/
  level/ narrative/
  ui/ visual/               59 orphaned .pyc total, sources deleted

docs/
  architecture/        1   tr-registry.yaml (tech radar, not an ADR)
  adr-*.md             0   correct at minimal

production/
  sprints/             0   correct at minimal
  milestones/          0   correct at minimal
  qa/evidence/         5   retained screenshots
  session-state/       1   active.md (session checkpoint)
  epics/               0   correct at minimal

tests/                  2   FrameworkTests.cs (1+1==2) + _spike_test.cs leftover
prototypes/             3   directories, 0 documented
```

---

**End of Report**

*Generated by `/project-stage-detect` skill — 2026-10-17*
