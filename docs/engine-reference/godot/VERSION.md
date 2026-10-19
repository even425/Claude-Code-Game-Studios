# Godot Engine — Version Reference

| Field | Value |
|---|---|
| **Engine Version** | 4.7.2 |
| **Project Pinned** | 2026-10-16 |
| **Installed at pin time** | 4.7.2 |
| **Release Date** | 4.7 feature release June 2026; 4.7.2 maintenance release August 2026 |
| **Last Docs Verified** | 2026-10-17 |
| **LLM Knowledge Cutoff** | January 2026 |

## Knowledge Gap Warning

The LLM's training data does **not** cover this version. Godot 4.7 shipped
2026-06 and 4.7.2 shipped 2026-08, both after the January 2026 cutoff. Anything
about 4.7+ is **outside what the model knows** — not "probably fine", outside.

Always cross-reference this directory before suggesting Godot API calls.

**Risk level: HIGH.** Reference docs are required, not optional.

## Installed-Version Gap — CLOSED

The pin was deliberately set ahead of the install on 2026-10-16 (4.7.2 pinned,
4.6 on disk). **That gap is closed as of 2026-10-17**: the probe now resolves
`godot --version` to `4.7.2.stable.official.ed1daf0bf`, from
`C:\Tools\Godot\4.7.2\godot.exe` on `PATH`. Pin and installed version agree.

`project.godot` is **verified** — `godot --headless --path . --import` ran
clean against 4.7.2 (exit 0, no `ERROR` lines, editor layout loaded). An older
4.6 binary also remains at `D:\develop\Godot_v4.6-stable_win64.exe` for
comparison; note it is the GUI variant, so pipe its stdout through
`Godot_v4.6-stable_win64_console.exe` instead.

## Editor Build — .NET / Mono (supersedes the standard build)

The same path now carries the **.NET build**, replaced in place on 2026-10-17:

```
godot --version  →  4.7.2.stable.mono.official.ed1daf0bf   (was ...stable.official...)
GodotSharp/      →  present
```

Same commit hash, so **the 4.6 → 4.7 findings above are unaffected** — this is
the same engine with C# support added, not a different build.

C# toolchain, verified rather than assumed:

| step | result |
|---|---|
| .NET SDK | 9.0.318 @ `C:\Program Files\dotnet` (**not on PATH**) |
| NuGet restore | Godot.NET.Sdk / GodotSharp / GodotSharpEditor 4.7.2 resolve |
| `dotnet build` | 0 errors, 0 warnings, DLL produced |
| failure check | a deliberately broken script → `error CS0103`, exit 1 |

The failure check is the load-bearing row: a green build only means something if
you have seen the gate fail.

> **`--build-solutions` is a no-op here.** It implies `--editor` and reports
> exit 0, but headless Godot does **not** generate the `.csproj` — that happens
> when a C# script is first created through the editor UI. Exit 0 from that flag
> is not evidence that anything compiled.

## Reference Documentation Status — 4.6 → 4.7

**VERIFIED AGAINST THE ENGINE BINARIES, 2026-10-17.**

WebFetch remains blocked for `docs.godotengine.org`, `godotengine.org` and
`raw.githubusercontent.com`, so the migration guide could not be read. Instead
the `## 4.6 → 4.7` claims were tested **directly against both installed
engines** — 4.6 (`89cea1439`) and 4.7.2 (`ed1daf0bf`) — with the same probe
script. That is stronger than the documentation would have been: each
confirmed change has evidence at *both* ends of the transition.

**Result: 6 checks — 4 confirmed, 2 refuted.** Two published-sounding claims
were false and have been removed rather than softened. See
`breaking-changes.md` § 4.6 → 4.7.

What this method cannot cover: anything about 3D physics backends, web export,
or platform tooling that is not observable through `ClassDB` /
`ProjectSettings` from a headless script. Those remain **NOT SOURCEABLE** until
a primary source is reachable.

## Post-Cutoff Version Timeline

| Version | Release | Risk Level | Key Theme |
|---------|---------|------------|-----------|
| 4.8 | ~late 2026 / early 2027 (beta) | — | In development — **do not target** |
| 4.7 | June 2026 | HIGH | `AreaLight3D`, `InputEvent.DEVICE_ID_MOUSE`/`_KEYBOARD`, `AnimationNodeBlendSpace2D.SyncMode`, HDR output, Asset Store, wasm64 web exports |
| 4.6 | Jan 2026 | HIGH | Jolt default, glow rework, D3D12 default on Windows, IK restored |

## Verified Sources

**First-hand (this project, 2026-10-17):** the installed 4.7.2 and 4.6 binaries,
probed headlessly. Authoritative for the API facts recorded in
`breaking-changes.md`.

**Not reachable on 2026-10-17** — consult when WebFetch works again:

- Official docs: https://docs.godotengine.org/en/stable/
- Release blog: https://godotengine.org/blog/release/
- Download archive: https://godotengine.org/download/archive/
- 4.6→4.7 migration guide: https://docs.godotengine.org/en/4.7/tutorials/migrating/upgrading_to_godot_4.7.html
