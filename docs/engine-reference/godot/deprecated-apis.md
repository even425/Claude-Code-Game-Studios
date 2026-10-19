# Godot — Deprecated APIs

Last verified: 2026-10-17 (4.7 deprecations still NOT ESTABLISHED — see below)

If an agent suggests any API in the "Deprecated" column, it MUST be replaced
with the "Use Instead" column.

## 4.7 Deprecations — NOT ESTABLISHED

**This table is empty because nothing could be sourced, NOT because 4.7
deprecated nothing.** Those are opposite conclusions and the difference decides
whether an agent may trust a blank row.

**NOT SOURCEABLE — Godot 4.7's deprecation list.** Two obstacles, both still in
place on 2026-10-17:

1. WebFetch is blocked for `docs.godotengine.org`, `godotengine.org` and
   `raw.githubusercontent.com`, so the migration guide cannot be read.
2. The differential engine probe used for `breaking-changes.md` cannot answer
   this question — it compares APIs that *exist*, not ones that were *removed*.
   Enumerating removals needs the deprecation list itself.

What the probe did observe, which is **suggestive but not evidence of absence**:
every API it checked that differs between 4.6 and 4.7.2 changed by **addition**
(`AreaLight3D` added; `DEVICE_ID_MOUSE`/`_KEYBOARD` added;
`AnimationNodeBlendSpace2D.SYNC_MODE_*` added) — none by removal. Three classes
sampled is a very narrow sample, and a feature release carrying mostly additions
is exactly what you would expect from a release that also changed the headline
features. **This does not substitute for the table below being filled in.**

An agent proposing a 4.7-era API must say it is unverified against this file
rather than reading the absence as approval. To fill this in, read
`https://docs.godotengine.org/en/4.7/tutorials/migrating/upgrading_to_godot_4.7.html`
when WebFetch works again.

## Nodes & Classes

| Deprecated | Use Instead | Since | Notes |
|------------|-------------|-------|-------|
| `TileMap` | `TileMapLayer` | 4.3 | One node per layer instead of multi-layer node |
| `VisibilityNotifier2D` | `VisibleOnScreenNotifier2D` | 4.0 | Renamed for clarity |
| `VisibilityNotifier3D` | `VisibleOnScreenNotifier3D` | 4.0 | Renamed for clarity |
| `YSort` | `Node2D.y_sort_enabled` | 4.0 | Property on Node2D, not a separate node |
| `Navigation2D` / `Navigation3D` | `NavigationServer2D` / `NavigationServer3D` | 4.0 | Server-based API |
| `EditorSceneFormatImporterFBX` | `EditorSceneFormatImporterFBX2GLTF` | 4.3 | Renamed |

## Methods & Properties

| Deprecated | Use Instead | Since | Notes |
|------------|-------------|-------|-------|
| `yield()` | `await signal` | 4.0 | GDScript 2.0 coroutine syntax |
| `connect("signal", obj, "method")` | `signal.connect(callable)` | 4.0 | Callable-based connections |
| `instance()` | `instantiate()` | 4.0 | Renamed |
| `PackedScene.instance()` | `PackedScene.instantiate()` | 4.0 | Renamed |
| `get_world()` | `get_world_3d()` | 4.0 | Explicit 2D/3D split |
| `OS.get_ticks_msec()` | `Time.get_ticks_msec()` | 4.0 | Time singleton preferred |
| `duplicate()` for nested resources | `duplicate_deep()` | 4.5 | Explicit deep copy control |
| `Skeleton3D` signal `bone_pose_updated` | `skeleton_updated` | 4.3 | Renamed |
| `AnimationPlayer.method_call_mode` | `AnimationMixer.callback_mode_method` | 4.3 | Moved to base class |
| `AnimationPlayer.playback_active` | `AnimationMixer.active` | 4.3 | Moved to base class |

## Patterns (Not Just APIs)

| Deprecated Pattern | Use Instead | Why |
|--------------------|-------------|-----|
| String-based `connect()` | Typed signal connections | Type-safe, refactor-friendly |
| `$NodePath` in `_process()` | `@onready var` cached reference | Performance: path lookup every frame |
| Untyped `Array` / `Dictionary` | `Array[Type]`, typed variables | GDScript compiler optimizations |
| `Texture2D` in `Shader.set_default_texture_parameter()` / `get_default_texture_parameter()` | `Texture` base type | Changed in 4.4; the shading language's `sampler2D` / `texture()` did not change |
| Manual post-process viewport chains | `Compositor` + `CompositorEffect` | Structured post-processing (4.3+) |
| GodotPhysics3D for new projects | Jolt Physics 3D | Default since 4.6; better stability |
