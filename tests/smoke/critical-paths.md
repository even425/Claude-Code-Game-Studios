# Smoke Test: Critical Paths

**Purpose**: Run these checks in under 15 minutes before any QA hand-off.
**Run via**: `/smoke-check` (which reads this file)
**Update**: Add new entries when new core systems are implemented.

## Core Stability (always run)

1. Game launches without crash
2. A new session can be started
3. UI responds to all inputs without freezing

## Core Mechanic — 村庄与装备

4. 村民自动出村打副本（玩家没有「派他去哪」的按钮）
5. 掉落进入仓库，点击可穿给指定村民
6. 同一套装备穿在同一人身上才触发套装加成（战力分解里能看到）
7. 拆开穿 vs 凑成套，战力差异与界面显示一致
8. 材料在「建村庄」和「打造装备」之间构成取舍——两边都抢同一份

## Data Integrity

9. Save completes without error *(once a save system exists — the MVP brief
   deliberately excludes it)*
10. Load restores correct state *(same)*

## Performance

11. No visible frame-rate drops on target hardware (60 fps target)
12. No memory growth over 5 minutes of play

## Known not-yet-applicable

- 村庄等级表现（Lv.0 → Lv.5）— `/dev-story` 尚未产出这些 story
- 任何 Godot C# 运行时验证 — 本项目此前两轮原型走的是 HTML 路径，绕开了 C#；
  C# 工具链本身已于 2026-10-17 单独验证可编译可测试，但**游戏代码一行都还没写**。
