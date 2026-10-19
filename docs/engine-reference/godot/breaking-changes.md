# Godot — Breaking Changes

Last verified: 2026-10-17 (the 4.6 → 4.7 section was tested against both engine binaries, not the docs — read its header)

Changes between Godot versions, focused on post-LLM-cutoff changes (4.4+).

## 4.6 → 4.7 (June 2026 — POST-CUTOFF, HIGH RISK)

> **SOURCE: engine binaries, 2026-10-17.** WebFetch was blocked for
> `docs.godotengine.org`, `godotengine.org` and `raw.githubusercontent.com`, so
> the migration guide could not be read. Instead each claim below was tested
> headlessly against **both** installed engines — 4.6 (`89cea1439`) and 4.7.2
> (`ed1daf0bf`). 6 checks: **4 confirmed, 2 refuted.** The refuted ones are
> marked and must not be acted on.
>
> Re-verify anything here that matters to you, using the same method:
> `godot --headless --path . -s <probe>.gd` on each version.

### CONFIRMED by differential test

| Subsystem | Change | Evidence | Affects this 2D project? |
|-----------|--------|----------|---------------------------|
| Input | **`InputEvent.DEVICE_ID_MOUSE` and `DEVICE_ID_KEYBOARD` added** — `32` and `16` | **4.6: parse error** ("Cannot find member DEVICE_ID_MOUSE in base InputEvent"). **4.7.2: both resolve**, to 32 and 16. | **YES** — never hardcode device id `0`; use the named constants |
| 3D | **`AreaLight3D` class added** | `ClassDB.class_exists("AreaLight3D")` → `false` on 4.6, `true` on 4.7.2 | No — 2D project |
| Animation | **`AnimationNodeBlendSpace2D` gained a `SyncMode` enum** | Integer constants on 4.6: `BLEND_MODE_INTERPOLATED`, `BLEND_MODE_DISCRETE`, `BLEND_MODE_DISCRETE_CARRY`. On 4.7.2 those three **plus** `SYNC_MODE_NONE`, `SYNC_MODE_INDEPENDENT`, `SYNC_MODE_CYCLIC_MUTABLE`, `SYNC_MODE_CYCLIC_CONSTANT`. | Only if used |

### REFUTED — do not act on these

| Claim that circulated | What the engines actually show |
|---|---|
| "`AudioStreamPlayer.area_mask` default changed from `1` to `0`, silently breaking `audio_bus_override` on Area2D/Area3D" | **The property does not exist in either version.** `ClassDB.class_get_property_list("AudioStreamPlayer")` on 4.7.2 returns exactly: `stream, volume_db, volume_linear, pitch_scale, playing, autoplay, stream_paused, mix_target, max_polyphony, bus, playback_type`. No `area_mask` on 4.6 either. This is not a default change and not a removal — the premise is false. If you hit a bug described this way, the diagnosis is wrong somewhere else. |
| "New projects now default to stretch mode `canvas_items` / aspect `expand`" | **Unchanged.** A fresh project reports `display/window/stretch/mode` = `disabled` and `aspect` = `keep` on **both** 4.6 and 4.7.2. |

> The stretch row was read from a project with no stretch settings, i.e. the
> engine default in effect — not copied from official documentation, which
> remains unreachable. It is labelled accordingly rather than presented as
> documented fact.

### NOT SOURCEABLE — could not be tested headlessly

Not observable through `ClassDB` / `ProjectSettings` from a script, and the
migration guide is unreachable. **Do not assume these are false — they are
unchecked.** All are 3D, web, or platform areas this project does not use.

- Jolt `SoftBody3D` mass default change
- Jolt `WorldBoundaryShape3D.plane.d` sign flip
- Vulkan push-constant padding rules for custom compositor effects
- Android OBB support removal
- `class_name` in built-in scripts being re-enforced as a parse error
- Shader preprocessor macro restrictions
- Audio spectrum analyzer API changes; particle angular velocity corrections

If one of these becomes relevant, consult
`https://docs.godotengine.org/en/4.7/tutorials/migrating/upgrading_to_godot_4.7.html`
when WebFetch works again.

## 4.5 → 4.6 (Jan 2026 — POST-CUTOFF, HIGH RISK)

| Subsystem | Change | Details |
|-----------|--------|---------|
| Physics | Jolt is now the DEFAULT 3D physics engine | New projects use Jolt automatically. Existing projects keep their setting. Some HingeJoint3D properties (like `damp`) only work with GodotPhysics. |
| Rendering | Glow processes BEFORE tonemapping | Was after tonemapping. Scenes with glow will look different. Adjust intensity/blend in WorldEnvironment. |
| Rendering | D3D12 default on Windows | Was Vulkan. For better driver compatibility. |
| Rendering | AgX tonemapper new controls | White point and contrast parameters added. |
| Core | Quaternion initializes to identity | Was zero. Unlikely to affect most code but technically breaking. |
| UI | Dual-focus system | Mouse/touch focus now separate from keyboard/gamepad focus. Visual feedback differs by input method. |
| Animation | IK system fully restored | CCDIK, FABRIK, Jacobian IK, Spline IK, TwoBoneIK via SkeletonModifier3D nodes. |
| Editor | New "Modern" theme default | Grayscale replaces blue-tint. Restore: Editor Settings → Interface → Theme → Style: Classic |
| Editor | "Select Mode" keybind changed | New "Select Mode" (v key) prevents accidental transforms. Old mode renamed "Transform Mode" (q key). |
| 2D | TileMapLayer scene tile rotation | Scene tiles can now be rotated like atlas tiles. |
| Localization | CSV plural form support | No longer requires Gettext for plurals. Context columns added. |
| C# | Automatic string extraction | Translation strings auto-extracted from C# code. |
| Plugins | New EditorDock class | Specialized container for plugin docks with layout control. |

## 4.4 → 4.5 (Late 2025 — POST-CUTOFF, HIGH RISK)

| Subsystem | Change | Details |
|-----------|--------|---------|
| GDScript | Variadic arguments added | Functions can accept `...` arbitrary params — new language feature |
| GDScript | `@abstract` decorator | Abstract classes and methods now enforceable |
| GDScript | Script backtracing | Detailed call stacks available even in Release builds |
| Rendering | Stencil buffer support | New capability for advanced visual effects |
| Rendering | SMAA 1x antialiasing | New post-processing AA option |
| Rendering | Shader Baker | Pre-compiles shaders — reportedly 20x faster startup on some demos |
| Rendering | Bent normal maps, specular occlusion | New material features |
| Accessibility | Screen reader support | Control nodes work with accessibility tools via AccessKit |
| Editor | Live translation preview | Test GUI layouts in different languages in-editor |
| Physics | 3D interpolation rearchitected | Moved from RenderingServer to SceneTree. API unchanged but internals differ. |
| Animation | BoneConstraint3D | New: AimModifier3D, CopyTransformModifier3D, ConvertTransformModifier3D |
| Resources | `duplicate_deep()` added | New explicit method for deep duplication of nested resources |
| Navigation | Dedicated 2D navigation server | No longer a proxy to 3D navigation; smaller export for 2D games |
| UI | FoldableContainer node | New accordion-style container for collapsible UI sections |
| UI | Recursive Control behavior | Disable mouse/focus interactions across entire node hierarchies |
| Platform | visionOS export support | New platform target |
| Platform | SDL3 gamepad driver | Delegated gamepad handling to SDL library |
| Platform | Android 16KB page support | Required for Google Play targeting Android 15+ |

## 4.3 → 4.4 (Mid 2025 — NEAR CUTOFF, VERIFY)

| Subsystem | Change | Details |
|-----------|--------|---------|
| Core | `FileAccess.store_*` return `bool` | Was `void`. Methods: `store_8`, `store_16`, `store_32`, `store_64`, `store_buffer`, `store_csv_line`, `store_double`, `store_float`, `store_half`, `store_line`, `store_pascal_string`, `store_real`, `store_string`, `store_var` |
| Core | `OS.execute_with_pipe` | Added optional `blocking` parameter |
| Core | `RegEx.compile/create_from_string` | Added optional `show_error` parameter |
| Rendering | `RenderingDevice.draw_list_begin` | Many parameters removed; `breadcrumb` parameter added |
| Rendering | `Shader.set_default_texture_parameter()` / `get_default_texture_parameter()` | Parameter/return type changed from `Texture2D` to `Texture`; the shading language did not change |
| Particles | `.restart()` method | Added optional `keep_seed` parameter (CPU/GPU 2D/3D) |
| GUI | `RichTextLabel.push_meta` | Added optional `tooltip` parameter |
| GUI | `GraphEdit.connect_node` | Added optional `keep_alive` parameter |

## 4.2 → 4.3 (In Training Data — LOW RISK)

| Subsystem | Change | Details |
|-----------|--------|---------|
| Animation | `Skeleton3D.add_bone` returns `int32` | Was `void` |
| Animation | `bone_pose_updated` signal | Replaced by `skeleton_updated` |
| TileMap | `TileMapLayer` replaces `TileMap` | One node per layer instead of multi-layer single node |
| Navigation | `NavigationRegion2D` | Removed `avoidance_layers`, `constrain_avoidance` properties |
| Editor | `EditorSceneFormatImporterFBX` | Renamed to `EditorSceneFormatImporterFBX2GLTF` |
| Animation | AnimationMixer base class | AnimationPlayer and AnimationTree now extend AnimationMixer |
