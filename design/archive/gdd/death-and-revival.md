# 死亡与复活

> **Status**: Draft
> **Author**: systems-designer
> **Last Updated**: 2026-10-17
> **Last Verified**: 2026-10-17 — 接口部分逐条对过 `src/HeroAgent.cs`、`src/VillageLife.cs`、`src/Main.cs`
> **Implements Pillar**: 村庄是活的，而住在里面的村民不知道自己活在一个真的世界里

> **术语消歧（2026-10-17）**：本文档中「玩家」二字一律读作
> **「召唤来的村民」**——他们是角色，不是网络用户。
> **这个游戏真实的玩家只有一个，就是村长（阅读者/操作者本人）。**
> 依据 `production/design-decisions.md` 术语消歧节。
> 「PvP」指角色之间的战斗，与联网无关。

> **开发者原话（本文档的唯一依据）**
> > 「死亡可以让这个玩家，过一天才能上线」
>
> 来自 `design/game-brief.md` 的展开（同样出自开发者）：
> > 「不是永久删除，是**停机一天**。但那个玩家会以为自己被删号了——他会去看客服，
> > 会去论坛发帖问『怎么注销』，会来找 GM。而村长一句也不能解释。」

## Summary

村民死了，**什么都不会被删除**——他的花名册、他的摊子、他的仇、他盖的半截房子，全都留着。
本系统只做一件事：把他的上线闸门关上，关到某个确定的时刻为止。到点他自己回来了，
没有仪式，没有按钮，没有 GM 的话。这件事唯一的重量在于：**他不在的那一天，
是他自己以为被删号的那一天。**

> **Quick reference** — Layer: `Feature` · Priority: `Vertical Slice` · Key deps: `时间与作息`（唯一）

---

## Overview

这是一个**只有一个状态量**的系统。

死亡不是角色销毁，不是存档回滚，不是地图上多一具尸体。它把一个绝对时刻写进村民身上——
**「在这个时刻之前，你不能上线」**——然后什么都不做。村庄少一个人：工地可能停，
派不出队，覆盖曲线上多一个洞。除此之外，村庄这边一切照旧。

村庄里的其他人**什么都不知道**，因为他们本来就不在场；唯一在场的是村长，而村长
一句也不能解释。本系统**不定义**那句解释、也不定义死亡那一刻村民的论坛发言——
那是 #11 杂谈与 M5「真相」的产出，本文档只在 Dependencies 里声明边界。

---

## Player Fantasy（简）

你手里握着一个人的开关，但那个开关没有标签。你知道他在哪儿，
你不知道他什么时候回来，而且**你不能告诉任何人你为什么知道**。

---

## Detailed Design

### Core Rules

**R1 — 死亡不删除任何东西。**
不得实现、不得留钩子：永久删除、存档回滚、属性重置、等级回退、装备清空。
死亡前后的村民对象是**同一个对象**，同一个 `HeroId`，同一份 `UtcOffset` /
`LocalLoginAt` / `PlayHours` / `Power` / `Grit`。

**R2 — 死亡只写两个字段。**

```
DeadAtAbsHour   ← 他倒下的绝对时刻（诊断用，本系统不消费）
ReviveAtAbsHour ← 闸门打开的绝对时刻（**唯一权威**）
```

本系统**不修改任何其他字段**——`Away` / `ToSite` / `ToDuty` / `GrudgeWith` /
`SlackDays` / `RestlessDays` / 建筑归属 / 摊位 / 抽成记录全部保持死亡瞬间的值。
死在外面的人回来时若仍被标着 `Away`，他就不出现在地图上（`Present = !Away && Online`，
见 `time-and-schedule.md` F5）——这是良定义的保守行为，**不由本系统决定**（见 Open Questions Q6）。

**R3 — 上线闸门。**
`IsOnlineAt(hour)` 追加一条前置判定：`AbsoluteHour < ReviveAtAbsHour → false`。
**闸门优先于作息窗口**：他的 `LoginAt` / `PlayHours` 一个字都不改，作息照常经过村庄时钟，
只是每次到达窗口都被拒绝。这是他「尝试上线但上不来」的机制来源。

**R4 — 没有复活动作。**
不存在复活按钮、不存在献祭、不需要村长在场、不需要队伍、不需要物品。
到点他自己回来。这是从「过一天**才能**上线」严格推出的：**条件是时刻，不是行为。**
（见 Open Questions Q4——是否给村长一个手动提前放行的权限。）

**R5 — 一次停机只能发生一次。**
闸门关闭期间他恒离线，离线者不会被抽进任何事件，因此**停机中不可能再死一次**。
`ReviveAtAbsHour` 不会被二次覆盖。这条是结构性的，不需要额外判定。

**R6 — 对村庄，停机的唯一后果是减员。**
工地可能停工（`BuildRate` 少一个人）、队伍可能凑不齐（`ReadySquad` 少一个人）、
覆盖曲线上多一个洞。**不产生任何掉落、遗物、产权变更。**

**R7 — `AbsentDays` 在停机期间是镜像，不是权威。**
死亡时把 `AbsentDays` 置为 `≥ 1`（让所有现存的缺席消费者立刻生效：
`VillageLife` 的候选池 `AbsentDays <= 0`、`WorkRate` 的 `0f`、花名册文案），
此后**每帧按剩余停机时长重算覆盖**（F6），因此 `RefreshOnline` 里的日减一
对停机中的村民不再生效。闸门本身由 `ReviveAtAbsHour` 独立保证，不依赖 `AbsentDays`。

**R8 — 死亡只可能在「在场」时发生。**
闸门关闭 ⇒ `Present == false` ⇒ 不在场 ⇒ 不会进入战斗 / 副本判定。
因此 `DeathAbsHour` **恒落在他的在线窗口内**，`DeathHour ∈ [LoginAt, LoginAt + PlayHours)`。
所有依赖这条性质的公式（F3、F7）都建立在它之上。

**R9 — 两条缺席闸门取「与」。**
停机闸门（`ReviveAtAbsHour`，绝对时刻）与日常缺席闸门（`AbsentDays`，日减一计数，
来源：生病、昨天打架）**互相独立，都要打开才算能上线**。
两道闸门同时关闭时，花名册显示剩余时间较长的一条。

### 本系统明确不做（开发者没有说过，不得实现）

| 不做 | 理由 |
|---|---|
| 永久死亡 / 存档删除 | brief 原文：「不是永久删除，是**停机一天**」 |
| 尸体、墓碑、复活点 | 开发者从未提及。M5 也没提过 |
| 本土人救援、谁把他背回来 | 开发者从未提及。本土角色属 #12，其职责本系统无权定义 |
| 死亡掉落、装备遗失、经验损失 | 开发者从未提及 |
| 玩家的手动复活 / 献祭 / 消耗品 | 与「过一天才能上线」矛盾：条件是时刻不是行为（R4） |
| 死亡是玩家可见的表演（镜头、黑屏、慢动作） | 表现需求见 Open Questions Q5 |
| 死亡对 M5「怀疑值 / 真相揭露」的任何数值影响 | 本系统是 M5 的**地基**，不是 M5 的一部分 |

### States and Transitions

村民在本系统里只有一个状态维度：**闸门开 / 闸门关**。

| 状态 | 进入条件 | 退出条件 | 行为 |
|---|---|---|---|
| **闸门开（正常）** | 初始；或 `AbsoluteHour ≥ ReviveAtAbsHour` | 被来源系统判定死亡 | 作息照常（F3 上游），`Present = !Away ∧ Online` |
| **闸门关（停机）** | 死亡事件写入 `ReviveAtAbsHour` | `AbsoluteHour ≥ ReviveAtAbsHour` | `IsOnlineAt` 恒 `false`；`WorkRate = 0`；不在任何候选池；不出现在地图上；**不产生任何事件** |

```
                 死亡事件（来源：副本 / PvP / 本土威胁 —— 本系统不定义来源）
   ┌──────────┐   写入 DeadAtAbsHour / ReviveAtAbsHour   ┌──────────┐
   │ 闸门 开  │ ───────────────────────────────────────→ │ 闸门 关  │
   └──────────┘                                            └──────────┘
         ↑                                                         │
         │            AbsoluteHour ≥ ReviveAtAbsHour                │
         └─────────────────────────────────────────────────────────┘
                    无按钮、无事件、无 GM 介入
```

### Interactions with Other Systems

| 系统 | 数据流向 | 约定 |
|---|---|---|
| **时间与作息** (#1) | 本系统**读** `AbsoluteHour` / `Day` / `Hour`；本系统**写** `ReviveAtAbsHour` | 本系统**不得**自己重算在线窗口，**不得**缓存 `LoginAt`。唯一需要上游改动的是 `IsOnlineAt` 增加一条前置 `false`（见 Open Questions Q7） |
| **杂谈** (#11) | 本系统**产出**一个一次性事件 `JustDied` / `JustRevived` | 本系统**不提供任何文案**。文本归 #11 |
| **真相 / 村长的原罪** (M5) | 本系统**产出**这两个事件 + 停机时长 | **本系统不消费 M5 的任何东西**，也不写任何怀疑度/揭露度数值。边界到此为止 |
| **任务与副本** (#6) / **交易、背叛与 PvP** (#9) | 它们**产出**死亡事件 | 死亡**来源**不在本系统。本系统对来源一无所知，也不记录 |
| **村庄建造** (#5) / **配队与配合** (#4) | 无数据流 | 它们只通过 `OnDuty` / `Present` **间接**感知减员。本系统不直接改它们的任何量 |
| **召唤系统** (#2) | 无 | 停机中的村民**绝不能被回收进池子**重抽——R1 的直接后果 |

---

## Formulas

> ⚠️ **本节两条读法并列，因为开发者没有拍板。**
> 「过一天」的「天」属于谁是**未决问题 Q1**，见文末 Open Questions。
> 下面 A / B 两套公式各自完整，可单独实现与测试；本文档末尾给出**建议，不是决定**。

约定：`DayHours = 24`（常量，一个村庄日的小时数）；`Wrap(h) = ((h mod 24) + 24) mod 24`。

### F1 · 停机时长（读法总表）

```
读法 A · 村庄日      DowntimeVillageHours = DayHours
读法 B1· 地球日(墙钟) DowntimeVillageHours = 86400 / DayLength × DayHours
读法 B2· 地球日(会期) DowntimeVillageHours = Wrap(LoginAt − DeathHour, (0,24])
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `DayHours` | int | `24`（常量） | 上游 `time-and-schedule.md` | 一个村庄日的小时数 |
| `DayLength` | float | `(0.5, ∞)` | 上游，默认 `75` | 一个村庄日的**真实秒数** |
| `LoginAt` | float | `[0, 24)` | 上游 F2 | 换算到村庄时钟的上线时刻 |
| `DeathHour` | float | `[LoginAt, LoginAt+PlayHours)` | R8 保证 | 他倒下的村庄钟点 |

**输出范围**

| 读法 | `DowntimeVillageHours` | `DowntimeRealSeconds` |
|---|---|---|
| A | 恒 `24` | `= DayLength`，随 `DayLength` 无上界 |
| B1 | `= 86400/DayLength × 24`；`DayLength=75` 时 `= 27648` 村庄小时 = **1152 个村庄日** | 恒 `86400` s（24 真实小时） |
| B2 | `∈ (24 − PlayHours, 24]`；`PlayHours ∈ [1.5, 9]` → **`[15, 24)`** | `∈ [0.625, 1.0) × DayLength` |

**边界情况**

| 输入 | 结果 | 原因 |
|---|---|---|
| `PlayHours = 9`（上限，肝 × 莽） | B2 的 `DowntimeVillageHours ∈ (15, 24]` | 会期法**永远不会停满也不少于 15 村庄小时**，因为他的窗口最长 9 小时 |
| `PlayHours = 1.5`（下限，挂机 × 傲） | B2 `∈ (22.5, 24]` | 窗口越短，B2 越接近读法 A |
| 村民被 `PinOnlineAt` 钉住（无 `UtcOffset`） | **B2 无定义**，`LoginAt` 是村庄钟点而「他自己那边」不存在 | 开局三人没有自己的时区。见 Edge Cases E7 |
| `DeathHour == LoginAt`（死在窗口第一刻） | A 与 B2 **都**停 24 村庄小时（`Wrap(0)` → 24，不取 0） | `Wrap` 必须取**开区间** `(0,24]`，取到 0 会得到「零停机」 |
| `DayLength → ∞`（调试拉满） | B1 的 `DowntimeVillageHours → 0` | 量纲反了。**B1 与 `DayLength` 强耦合，这是它不可行的根因** |

**工作示例**（`DayLength = 75`，`UtcOffset = +8`，`LocalLoginAt = 19`，`PlayHours = 4`）
→ `LoginAt = Wrap(19 − 8) = 11`，窗口村庄 `[11, 15)` / 本地 `[19:00, 23:00)`。
死在村庄 Day 3 的 13:30（窗口内，符合 R8）：

| 读法 | 停机时长 | 真实秒数 |
|---|---|---|
| A | `24` 村庄小时 | `75` |
| B2 | `Wrap(11 − 13.5, (0,24]) = 21.5` 村庄小时 | `67.2` |
| B1 | `27648` 村庄小时 = **1152 个村庄日** | `86400` |

### F2 · 读法 A 的恢复时点（村庄日）

```
ReviveAtAbsHour = DeathAbsHour + DayHours
DeathAbsHour    = (DeathDay − 1) × DayHours + DeathHour
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `DeathAbsHour` | float | `≥ 0` | 上游 `AbsoluteHour` | 他倒下的绝对时刻。**单调不减**（上游 E7） |
| `DeathDay` | int | `≥ 1` | 上游 `Day` | 村庄天数 |
| `DeathHour` | float | `[0, 24)` | 上游 `Hour` | 村庄钟点 |
| `ReviveAtAbsHour` | float | `≥ DayHours` | 计算 | 闸门打开的绝对时刻 |

**输出范围**：`ReviveAtAbsHour − DeathAbsHour ≡ 24`，**恒等于**一个村庄日，无抖动、无取整、无钳制。
还原到村庄钟点：`Day = ⌊ReviveAtAbsHour / 24⌋ + 1`，`Hour = ReviveAtAbsHour mod 24`
——**他回到死亡时那个村庄钟点，整整晚一天**。

**边界情况**：`DeathAbsHour` 用 `AbsoluteHour` 而非 `Day × 24 + Hour` 重算，
因为 `Hour` 跨午夜会从 23.9 跳回 0.0（上游 E7 的原始教训）。
`AbsoluteHour` 恒单调，所以 `+24` 永远落在未来。

**工作示例**：`Day 3, Hour 13.5` → `DeathAbsHour = 2×24 + 13.5 = 61.5` → `ReviveAt = 85.5`
→ `Day 4, Hour 13.5`。真实停机 `24/24 × 75 = 75 s`。✅

### F3 · 读法 B2 的恢复时点（他的下一个会期）

```
Delta          = LoginAt + DayHours − DeathHour        （因 R8，DeathHour ≥ LoginAt，故 Delta ∈ (0, 24]）
ReviveAtAbsHour = DeathAbsHour + Delta
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `Delta` | float | `(0, 24]`，实测落点 `[15, 24)` | 计算 | 从倒下到他自己下一次上线钟点的村庄小时数 |
| `ReviveAtAbsHour` | float | `≥ DeathAbsHour + 15` | 计算 | 闸门打开的绝对时刻 |

**输出范围**：`ReviveAtAbsHour` 恒落在**他下一个在线窗口的第一刻**。可证：
`Delta ≤ 24` ⇒ `ReviveAtAbsHour ≤ DeathAbsHour + 24 < LoginAt + PlayHours + DayHours`，
而 `Delta > DayHours − PlayHours` ⇒ `ReviveAtAbsHour > LoginAt + DayHours`，
故 `DayHours + LoginAt < ReviveAtAbsHour < DayHours + LoginAt + PlayHours`——
**恰好一个窗口，且是完整的那个窗口**。因此钉住三人（`PlayHours ≈ 11–12.8`）不在此区间内，需特判（E7）。

**边界情况**

| 输入 | 结果 | 原因 |
|---|---|---|
| `DeathHour == LoginAt` | `Delta = 24`，与读法 A **相同** | 死在窗口第一刻时，两种读法给出同一个恢复时刻 |
| `DeathHour → LoginAt + PlayHours⁻` | `Delta → 24 − PlayHours`（**下限，不可达**） | 他刚好在窗口最后一刻倒下，会整个错过这一程 |
| 帧率极低 / `--daylen` 极小，一帧跳过整个停机窗口 | 复活在**当帧**立即判定完成 | 复活不是一次世界事件，不需要「他回来的那一刻」被观测到 |

**工作示例**：`LoginAt = 11`，`DeathHour = 13.5` → `Delta = 11 + 24 − 13.5 = 21.5`
→ `ReviveAt = 61.5 + 21.5 = 83.0` → `Day 4, Hour 11.0` = **他的上线钟点整点**，本地 `19:00`。✅

### F4 · 读法 B1 的量纲论证（为什么当前不可行）

```
DowntimeRealSeconds = 86400
DowntimeVillageHours = 86400 / DayLength × DayHours
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `86400` | int | 常量 | 定义 | 一个地球日 = 24 × 3600 秒 |
| `DowntimeVillageHours` | float | `DayLength → ∞` 时 `→ 0`；`DayLength → 0.5⁺` 时 `→ 4,147,200` | 计算 | 与游戏的时间压缩**强耦合且方向反了** |

**输出范围**：`DayLength = 75`（当前默认）时 `27648` 村庄小时 = **1152 个村庄日**。
在这个尺度上「一天」已经不能被任何一次游戏观测到——`DayLength = 75` 下整个游戏才跑几分钟。

**结论**：B1 与「村庄的一天 = `DayLength` 秒」这条**全局时间压缩**正面冲突。
它引入第二个时间单位，而引擎里没有它的位置。要实现 B1，等价于要求
`DayLength = 86400`，即**取消整个游戏的时间压缩**。这不是本系统能单独做的决定。

### F5 · 时区还原（跨时区）

```
LocalReviveHour = Wrap(ReviveAtAbsHour + UtcOffset)      （仅对未被钉住的村民有定义）
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `ReviveAtAbsHour` | float | `≥ 0` | F2 / F3 | 闸门打开的绝对时刻。**时区无关** |
| `UtcOffset` | float | `−11 .. +13` | 上游 F2 | 他来自地球哪一带 |
| `LocalReviveHour` | float | `[0, 24)` | 计算 | 换算回**他自己的钟** |

这是上游 F2 `LoginAt = Wrap(LocalLoginAt − UtcOffset)` 的**逆运算**，两者互逆。

**输出范围**：`[0, 24)`，由 `Wrap` 双重取模保证，**不存在负的恢复时刻**。
存档/恢复时（若将来做）**只存 `ReviveAtAbsHour`，不存本地时刻**——本地时刻是导出的，
每帧从 `UtcOffset` 重算，这样换时区不会让闸门漂移。

**工作示例**（`UtcOffset = +8`）

| 读法 | `ReviveAtAbsHour` | 村庄钟点 | `LocalReviveHour` | 读法 |
|---|---|---|---|---|
| A | `85.5` | Day 4, `13:30` | `Wrap(13.5 + 8) = 21.5` → **21:30** | 同一面钟，但**不是**他惯常的上线时刻 |
| B2 | `83.0` | Day 4, `11:00` | `Wrap(11 + 8) = 19.0` → **19:00** | **恒等于** `LocalLoginAt`，与他几点上线无关 |

> **这是两种读法在玩家可见层面唯一、也是最干净的区别。**
> B2 下，无论他几点几分倒下，他回来时**永远是本地 19:00**——他自己那边的时间表一个字没变。
> A 下他回来时是 21:30，那是他这一天的中段，不是他上线的时间。

### F6 · `AbsentDays` 镜像（每帧重算）

```
AbsentDays = (AbsoluteHour < ReviveAtAbsHour) ? 1 : 0
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `AbsoluteHour` | float | `≥ 0` | 上游 | 当前绝对时刻 |
| `ReviveAtAbsHour` | float | `≥ 0` | F2 / F3 | 闸门时刻 |
| `AbsentDays` | int | `{0, 1}` | 计算 | 停机中恒 1，闸门打开后恒 0 |

**输出范围**：`{0, 1}` 二值。**故意不做递减计数**——日减一计数（`RefreshOnline` 里
`AbsentDays--`）会让停机时长随倒下时刻抖动（见 Open Questions Q8），
而 `VillageLife` 的候选池只判 `AbsentDays <= 0`，用二值即可正确排除。

**边界情况**：本题材的两种缺席闸门独立存在（R9）——
生病/打架的 `AbsentDays` 由 `VillageLife` 直接赋值并按日减一，本公式**不得覆盖它**。
实现上须区分「谁写的这个 `AbsentDays`」：本系统停机期间每帧写回 `1`，
其余时刻本系统**不碰**该字段。

**工作示例**：`ReviveAt = 83.0`。`AbsoluteHour = 70` → `AbsentDays = 1`（在候选池外、`WorkRate = 0`）；
`AbsoluteHour = 84` → `AbsentDays = 0`（闸门开，走正常的 `IsOnlineAt` 分支）。✅

### F7 · 停机造成的在线窗口损失（量化两种读法的代价）

```
LostVillageHours   = PlayHours − RetainedVillageHours
读法 A :  RetainedVillageHours = max(0, LoginAt + PlayHours − ReviveHour)
读法 B2:  RetainedVillageHours = PlayHours                （恒等，F3 已证）
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `RetainedVillageHours` | float | A: `(0, PlayHours]`；B2: `= PlayHours` | 计算 | 他回来那一程还能玩多少 |
| `LostVillageHours` | float | A: `[0, PlayHours)`，均值 `PlayHours/2`；B2: `= 0` | 计算 | 他**真正**损失的上线时间 |
| `PlayHours` | float | `[1.5, 9]` | 上游 F2 clamp 后 | 一次在线时长 |

**输出范围与分布（读法 A）**：`DeathHour` 在窗口内近似均匀 ⇒
`RetainedVillageHours` 在 `(0, H]` 上近似均匀 ⇒ **`E[Retained] = H/2`**，
`E[Lost] = H/2`。以 `H = 4`（贪/挂机）计：他平均只损失 **2 村庄小时**，最坏情况损失 0。

**这条量化结果是 A 与 B2 之间的全部分歧所在**，也是 Q1 的决策依据。

### F8 · 村庄侧的减员（诊断量）

```
ΔBuildRate = −WorkRate(dead)      若死者被派在工地（ToSite = true）
ΔReadySquad = −1                   若死者在当前队伍中
```

| 变量 | 类型 | 范围 | 来源 | 说明 |
|---|---|---|---|---|
| `WorkRate` | float | `[0, ∞)` | 上游 | 死者的干活速率；停机期间恒 `0` |
| `ΔBuildRate` | float | `[−WorkRate, 0]` | 计算 | 若他是工地上最后一个活人，工地**冻结**（上游 F6） |
| `ΔReadySquad` | int | `{0, −1}` | 计算 | 他不在时队伍可能直接凑不齐 |

**输出范围**：`ΔBuildRate ∈ [−0.014, 0]`（按当前 `WorkRate` 上界估）；
`ΔReadySquad ∈ {0, −1}`。停机**只降不加**——本系统没有任何一项会让村庄变好的量。

---

## Edge Cases

| # | 场景 | 期望行为 | 理由 |
|---|---|---|---|
| **E1** | 死者被派在工地上（`ToSite = true`） | 他离开劳动力池，`BuildRate` 减少他那一项；若他是最后一个，工地冻结（进度**不倒退**） | 上游 F6 的既有行为，本系统不新增任何处理 |
| **E2** | 停机中他又被日常事件抽中（生病、想走、结仇） | **不抽**。闸门关 ⇒ 离线 ⇒ 不在候选池（`VillageLife` 按 `AbsentDays <= 0` 过滤） | 「事件必须改状态」的反面：状态已经是非在状态，不该再叠加 |
| **E3** | 一帧内跨过整个停机窗口（掉帧 / `--daylen=8`） | 复活当帧判定完成，**不产生任何观测事件** | 对齐上游 E6「宁可少跑，不可刷屏」。复活不是一次世界事件 |
| **E4** | 停机中又得病 / 昨天打了架 | 两道闸门**都**开才上线；`AbsentDays` 取较大者显示 | R9。两类缺席的来源独立，不能互相顶替 |
| **E5** | 停机中村庄遭遇袭击 | 他不出现在地图上，`OnDuty = false`，不能被临时拉来 | 减员就是减员。本系统不提供任何临时解 |
| **E6** | 他停机期间离线时长已经超过他的 `PlayHours` | 闸门开的那一瞬他就上线，**与作息窗口无关**；若那一刻不在窗口内，仍照常判离线 | 闸门和作息是两道独立的门（R3）。开闸不等于上线 |
| **E7** | 被 `PinOnlineAt` 钉住的开局三人（无 `UtcOffset`） | **读法 B2 无定义**（他「自己那边」不存在）→ 强制回落读法 A | 他们是村长亲手召来的人（上游 R10），在世界里没有地球时区；`DayHours` 就是他的「一天」 |
| **E8** | 死者仍是 `Away = true`（死在外派途中） | 闸门开 ⇒ `Present = !Away ∧ Online = false` ⇒ **他回来但不出现在地图上** | R2「本系统不改任何字段」的必然结果。良定义，但不是玩家想要的——见 Q6 |
| **E9** | 读法 A 下 `DeathHour` 恰好等于 `LoginAt + PlayHours`（理论上不可达） | 照常按 A 计算 | R8 保证 `<`；此行只为函数良定义 |
| **E10** | 停机期间玩家反复点击他的花名册条目 | 显示他什么时候回来，**不给任何提前放行的入口**（R4） | 见 Q4 |
| **E11** | 全村只剩一个人，然后他死了 | 村庄按上游 E9 处理：「村里一个人也没有。天亮了，什么也没发生。」 | 本系统不新增杂谈条目 |
| **E12** | `ReviveAtAbsHour` 写入时 `AbsoluteHour` 恰好等于它 | 闸门**开**（判据是 `≥`，与上游窗口的左闭右开约定一致） | 边界包含在上线一侧 |
| **E13** | 同一村民被写入 `ReviveAtAbsHour` 两次 | 第二次**取 `max`**，不覆盖 | R5 使其结构上不可能发生；此行只为函数良定义 |
| **E14** | 存档恢复（当前 MVP 不做存档） | 无规则。若将来做：只存 `ReviveAtAbsHour`（绝对值），**不存本地时刻** | 见 F5 与上游 Q7 |

---

## Player-visible 文案约束

**玩家可见文案不得出现系统词：成功 / 失败 / 获得 / 消耗 / 属性 / 奖励。**
`ReviveAtAbsHour` / `DeadAtAbsHour` / `AbsentDays` / `AbsoluteHour` 等内部量名一律不得出现。

| 位置 | 文案 | 说明 |
|---|---|---|
| 花名册（停机中） | `○名字 性格 投入 还在路上` | 与「病了」「昨天打了一架」并列的第四种缺席 |
| 花名册点击回应 | `他不在。明天19时（东亚）才上来。` | **写他自己的时间**（上游 R12） |
| 花名册（钉住三人停机中） | `○名字 性格 投入 还在路上` + 点击 `他不在。明天村里常驻的时候才上来。` | E7 的显示分支 |

**停顿的措辞规则**：花名册**只说他在哪儿、什么时候回**，不说「死了」也不说「病了」。
但**不宣称一个假原因**——写「病了」等于让系统替村长撒谎，而那属于 M5 的账（Q3）。

> **本文档不提供死亡与复活的杂谈文案。** 那属 #11 与 M5。本系统只发出
> `JustDied(hero)` / `JustRevived(hero)` 两个一次性事件，**不带任何字符串**。

---

## Dependencies

| 系统 | 方向 | 依赖性质 |
|---|---|---|
| **时间与作息** (#1) | 本系统 → 时间与作息 | **状态触发**：本系统写 `ReviveAtAbsHour`，`IsOnlineAt` 读 `AbsoluteHour` 与之比较。**本系统是 `AbsentDays` 的一个生产者**（与 `VillageLife` 并列） |
| **杂谈** (#11) | 本系统 → 杂谈 | **数据依赖**：本系统产出 `JustDied` / `JustRevived` 两个事件，杂谈消费它们并自行决定文案 |
| **真相 / 村长的原罪** (M5) | 本系统 → M5 | **数据依赖**：死亡事件是 M5 的一个素材来源。**本系统不消费 M5 的任何量**——不写怀疑度、不写揭露度、不做知情判定。**边界到此为止** |
| **任务与副本** (#6) | 该系统 → 本系统 | **State trigger**：死亡**来源**由它判定并触发本系统；本系统对来源无知 |
| **交易、背叛与 PvP** (#9) | 该系统 → 本系统 | **State trigger**：同上，PvP 致死的来源判定 |
| **村庄建造** (#5) / **配队与配合** (#4) | 本系统 → 两者 | **Rule dependency**：它们通过 `OnDuty` / `Present` / `BuildRate` 间接感知减员；本系统不直接写它们的任何字段 |
| **召唤系统** (#2) | 本系统 → 召唤 | **Rule dependency**：R1 保证停机中的村民不被回收重抽——本系统对此无接口，靠「不删除对象」自然成立 |

**接口约定**：本系统对外只暴露 `DeadAtAbsHour` / `ReviveAtAbsHour` 与
`JustDied` / `JustRevived` 两个一次性事件（消费后清除，沿用上游 `JustLoggedIn` 的模式）。
其余系统**不得**自己判断「他死了没有」。

---

## Tuning Knobs

| 参数 | 当前值 | 安全区间 | 调高的影响 | 调低的影响 |
|---|---|---|---|---|
| `DayHours` | 24 | 12 – 48 | 停机更久，减员更痛；与上游所有「一天」口径继续一致 | 停机更短，M5 的种子变弱；低于 `PlayHours` 上界 9 时 A 的损失窗口会跨过整个会期 |
| `DowntimeUnit` | **未决（Q1）** | `VillageDay` / `OwnSession` | — | — |
| `AbsentDays` 停机镜像值 | 1 | 只能是 1 | — | 改成计数会让停机时长随倒下时刻抖动（Q8） |
| 复活是否需要一次世界事件 | 否 | — | 若改成「需要」（例如必须在他上线时判定），E3 的当帧完成会成为规则漏洞 | 当前选择：不需要。它不是一次事件 |

> 本系统只有**一个**真正的旋钮：`DayHours`。这是有意的——开发者只给了一句话，
> 一个旋钮是那句话能承载的上限。

---

## Acceptance Criteria

> 每条可由一次运行或一个测试独立判定。所有测试默认**确定性**。

**核心机制**

- [ ] **AC-1（不删除）** 死亡前后对同一 `HeroId` 断言 `UtcOffset` / `LocalLoginAt` /
      `PlayHours` / `Power` / `Grit` / 建筑归属 / 摊位记录**逐项相等**。
      **R1 的红线测试——任何永久删除实现都会让它失败。**
- [ ] **AC-2（只写两个字段）** 死亡事件后做一次全字段 diff，断言变化集合**恰为**
      `{DeadAtAbsHour, ReviveAtAbsHour, AbsentDays}`。
- [ ] **AC-3（闸门优先于作息）** 对 `LoginAt = 11, PlayHours = 4`、
      `DeathAbsHour` 使 `ReviveAt = 83.0` 的村民，断言 `IsOnlineAt`
      在 `[11, 15)` 的**下一个窗口**里：`AbsoluteHour < 83` 时为 `false`（含 `82.99`），
      `≥ 83` 时按正常窗口判定为 `true`。
- [ ] **AC-4（读法 A 的时点）** `DeathDay = 3, DeathHour = 13.5` → 断言
      `ReviveAtAbsHour == 85.5`，还原得 `Day == 4 ∧ Hour == 13.5`，且
      `ReviveAtAbsHour − DeathAbsHour == 24` **精确相等**（非近似）。
- [ ] **AC-5（读法 B2 的时点）** 同一死亡，`LoginAt = 11` → 断言
      `ReviveAtAbsHour == 83.0`，还原得 `Day == 4 ∧ Hour == 11.0`，且
      `LocalReviveHour == 19.0`（= 他的 `LocalLoginAt`）。
- [ ] **AC-6（B2 恒落在下一个窗口内）** 对 `PlayHours ∈ {1.5, 4, 9}` ×
      `DeathHour ∈ {LoginAt, LoginAt+PlayHours/2, LoginAt+PlayHours−0.01}` 共 9 组，
      断言还原后的 `Hour` 落在 `[LoginAt, LoginAt+PlayHours)` 内。
      **这是 B2 的核心不变量。**
- [ ] **AC-7（无手动放行）** 在 `src/` 与全部玩家可见文案中检索
      `复活|放行|提前|召唤回|unrevive|revive_now`，命中数为 **0**（R4）。
- [ ] **AC-8（停机中不可能再死）** 对一个 `AbsoluteHour < ReviveAtAbsHour` 的村民跑满
      一个完整游戏日，断言 `DeadAtAbsHour` **未改变**，`JustDied` 计数为 0（R5）。

**跨时区与镜像**

- [ ] **AC-9（时区还原互逆）** 对 `UtcOffset ∈ {−11, −8, −5, −3, 0, 1, 3, 5, 8, 9, 13}`
      断言 `LocalReviveHour = Wrap(ReviveHour + UtcOffset)`，且读法 B2 下
      它 **恒等于** `LocalLoginAt`。
- [ ] **AC-10（无负时刻）** 对 `UtcOffset = −11, ReviveHour = 3.0` 断言
      `LocalReviveHour == 16.0 ∈ [0, 24)`，不抛异常。
- [ ] **AC-11（钉住三人回落 A）** 对被 `PinOnlineAt(10, 11)` 钉住的村民，
      在读法 B2 配置下断言 `ReviveAtAbsHour − DeathAbsHour == 24`（**不是** B2 的 21.5）（E7）。
- [ ] **AC-12（`AbsentDays` 镜像）** `ReviveAt = 83.0`：断言 `AbsoluteHour = 70` 时
      `AbsentDays == 1`（且 `WorkRate == 0f`、`VillageLife` 候选池不含他）；
      `AbsoluteHour = 84` 时 `AbsentDays == 0`。跨两个 `newDay` 断言镜像值**不提前归零**。
- [ ] **AC-13（闸门不依赖镜像）** 手动把 `AbsentDays` 置 0 而 `AbsoluteHour < ReviveAtAbsHour`，
      断言 `IsOnlineAt` 仍为 `false`（证明权威是绝对时刻，不是计数器）。

**边界与红线**

- [ ] **AC-14（停机中不被抽中）** 停机中的村民连续跑 3 个游戏日，断言
      没有任何事件为其增加 `SlackDays` / `RestlessDays` / `GrudgeWith`（E2）。
- [ ] **AC-15（死亡只在在场时）** 断言本系统不存在「离线时被判定死亡」的代码路径——
      对 `Present == false` 的调用为 no-op 且不写 `ReviveAtAbsHour`（R8）。
- [ ] **AC-16（一帧跨过窗口）** `--daylen=0.6`，一帧推进 5 秒（跨越 8 个村庄日），
      断言他在**当帧**即被判定为闸门开，且 `JustRevived` **不产生**（E3）。
- [ ] **AC-17（两次写入取 max）** 对 `ReviveAt = 83.0` 再写入 `90.0`，
      断言结果为 `90.0`；再写入 `70.0`，断言结果仍为 `90.0`（E13）。
- [ ] **AC-18（`Away` 不被本系统清除）** 对 `Away = true` 时死亡的村民，
      闸门开后断言 `Present == false`（E8）。
- [ ] **AC-19（无系统词）** 在全部玩家可见字符串中检索
      `成功|失败|获得|消耗|属性|奖励`，命中数为 **0**。
- [ ] **AC-20（无内部量名）** 玩家可见文案中检索
      `ReviveAt|DeadAt|AbsentDays|AbsoluteHour|Downtime`，命中数为 **0**。
- [ ] **AC-21（数据驱动）** `DayHours` 与 `DowntimeUnit` 不出现在
      `HeroAgent.cs` / `WorldClock.cs` 的方法体内——来自配置。

**性能**

- [ ] **AC-22（帧预算）** 100 名村民时，单帧的「推进时钟 + 全部 `RefreshOnline` +
      闸门重算」耗时 `< 0.5 ms`。闸门判定是一次浮点比较，应可忽略。

---

## Cross-References

> 由 `/review-all-gdds` Phase 2c 机器核对。

| 本文档引用 | 目标 GDD | 具体元素 | 性质 |
|---|---|---|---|
| 停机落在 `AbsoluteHour` 上 | `design/gdd/time-and-schedule.md` | `AbsoluteHour`（单调不减，E7） | Data dependency |
| `IsOnlineAt` 追加前置 `false` | `design/gdd/time-and-schedule.md` | F3 `IsOnlineAt` / R6「缺席优先于作息」 | Rule dependency |
| `AbsentDays` 是缺席的既有通道 | `design/gdd/time-and-schedule.md` | R6 / F3 第一分支 / `AbsentWhy` | Rule dependency |
| 日减一不适用于停机镜像 | `design/gdd/time-and-schedule.md` | `RefreshOnline` 的 `newDay` 分支 | Ownership handoff |
| 停机中不算劳动力 | `design/gdd/village-building.md` | `BuildRate` 只累加 `OnDuty` 的人（F6） | Rule dependency |
| 停机中不能进队 | `design/gdd/squad-and-cohesion.md` | `ReadySquad` 依赖 `OnDuty` | Rule dependency |
| `JustDied` / `JustRevived` 进入杂谈 | `design/gdd/chronicle.md` | 一次性事件的消费模式 | Data dependency |
| 死亡事件由副本 / PvP 产生 | `design/gdd/squad-and-cohesion.md` / `design/gdd/trade-and-betrayal.md` | 死亡判定（尚未撰写） | State trigger |

> ⚠ **文件名不一致**：`time-and-schedule.md` 的 Cross-References 指向
> 上游 `time-and-schedule.md` 的引用已统一为 `death-and-revival.md`，断链已修。
> **路径需在 `/consistency-check` 时统一**（见 Open Questions Q9）。

---

## Open Questions

> 以下全部是**开发者没有说过**的问题。**Q1 尤其重要——本 GDD 之所以存在，就是为了把它摆到台面上。
> 本文给出的是建议，不是决定。**

| # | 问题 | Owner | 为什么重要 |
|---|---|---|---|
| **Q1** 🔴 | **「过一天」的「天」属于谁？** 读法 **A**（村庄日，24 村庄小时，停机恒为 24，`--daylen=75` 下 = 75 秒）还是读法 **B2**（他的下一个会期，停机 `[15, 24)` 村庄小时，**恒等于他惯常的上线时刻**）？B1（24 真实小时）在 `DayLength = 75` 下等于 **1152 个村庄日**，与全局时间压缩正面冲突，本文建议**排除**。**开发者原话未指定。** 详见下方「建议」 | **game-designer + creative-director**（涉及 M5 的赌注，不只是数值） | **两条读法的唯一分歧已量化（F7）：** 读法 A 下，他平均只损失 `PlayHours/2`（`H=4` 时是 2 村庄小时），最坏情况损失 **0**——**A 经常根本没有让「他」过掉一天**。B2 下他恒定损失整整一程，且回来时本地钟点恒为 `LocalLoginAt`。 |
| **Q2** | 若采纳 B2：**钉住的开局三人**（无 `UtcOffset`，`PlayHours ≈ 11–12.8` 超出 F3 的证明区间）如何定义自己的「一天」？本文暂定回落 A（E7） | game-designer | 三人是村长亲手召来的人，「自己那边」在世界里不存在。但让三个人和其余人规则不同是可读性隐患 |
| **Q3** | 花名册写什么？**「还在路上」**（本文暂定）还是直说？又或者——**由 M5 决定村长能不能说** | game-designer + narrative-director | 直说会让全村立刻知道「有人死了」；说假话（复用「病了」）等于让系统替村长撒谎。两件事都属于 M5 的账，本系统不该替它决定 |
| **Q4** | 村长有没有**手动提前放行**的权限？（任务简报提到「村长手里握着复活键」，但**开发者原话里没有这一句**，`game-brief.md` 只写了「村长一句也不能解释」） | game-designer + creative-director | 有则变成 M5 的核心筹码（可换人情、可隐瞒、可被村民看见）；无则本系统保持 R4 的「无按钮」。**本文按「开发者没说过」处理，暂定无** |
| **Q5** | 死亡与复活的**表现**要不要做？（镜头、黑屏、地图上的变化） | art-director | 当前村庄里下线是静默的（只有「最后一个也走了」）。若死亡完全静默，村长可能根本没注意到少了人——那正好是 M5 的味道；但也可能被读成 bug |
| **Q6** | 死在外派途中时 `Away` 谁来清？本系统不改它（E8），所以他会「回来了但不出现」 | systems-designer + game-designer（#6 / #9 的 Owner） | 依赖尚未撰写的 #6 / #9。在它们写完前这条无法定 |
| **Q7** | `IsOnlineAt` 的前置 `false` 由谁实现？上游是时间与作息的接口，**改动落在 `src/HeroAgent.cs`**，不在死亡系统自己的代码里 | technical-director | 这是一次**跨系统接口改动**，需走 `propagate-design-change`。AC-3 / AC-13 依赖它 |
| **Q8** | `AbsentDays` 停机期间用二值镜像（F6）还是日减一计数？本文选二值，因为计数会让停机时长随倒下时刻抖动 | systems-designer | 计数更「像」别的缺席，但 `RefreshOnline` 的 `newDay` 分支会提前把它减到 0 |
| **Q9** | 文件名统一：本文件 `death-and-revival.md` vs 上游引用的 `death-and-respawn.md` | systems-designer | 机器核对的 Cross-References 会因此失配 |
| **Q10** | 死亡是否要计入村庄的**永久性损失统计**（花名册上标出「这个人死过」）？ | game-designer | 与 Q3 同源。标了就无法隐藏，不标就 M5 少一个筹码 |

---

## 建议（不是决定）

> ⚠️ **开发者没有对 Q1 拍板。以下是我的建议，写在文档里是为了让它可以被明确否决，而不是被默默采纳。**

**建议采纳读法 B2（他的下一个会期），但仅作为目标语义；Vertical Slice 阶段先落读法 A。**

理由：

1. **原话是从 villager 的视角说的。**「死亡可以让**这个玩家**，过一天才能上线」——
   主语是他，不是村庄。用村庄的尺去量他的损失，量的不是他承受的东西。
2. **A 经常根本没让「他」过掉一天**（F7）。他死在窗口末尾时损失接近 0，
   第二天照常在 21:30 登录玩半程——一个这样被对待的人不会去看客服。
   brief 里「他会去看客服，会去论坛发帖问『怎么注销』」这一句，**在 A 下有一半概率不成立**。
3. **B2 的显示是免费的**（F5）：恢复时刻的本地钟点恒等于 `LocalLoginAt`，
   花名册永远写「明天19时（东亚）才上来」——一句文案，不需要任何新系统词。
4. **但 B2 今天落不了地**：`design/game-brief.md` 的 Out of scope 明写「**无联机**」。
   没有任何真实存在的会话可以被拒绝——按术语消歧，他是角色，本就不是真人——
   「一天」在模拟层没有对应的观测对象。
   **B2 的全部意义都要等到有真人登录的那一天，而那一天不会来。**

因此：**Vertical Slice 落 A（统一、零特例、与 `AbsentDays` 同口径、可在 75 秒内被 playtest 观测），
把 `DowntimeUnit` 做成一个配置项；联机已由术语消歧判为彻底出局（不是「暂时不做」），故 B2 不切。**
切换成本是一行——因为闸门时刻**本来就是绝对值、时区无关**（F5），
两种读法的差别只在 `Delta` 怎么算。

**需要开发者拍板的只有一件事：「他」失去的那一天，是村庄的一天，还是他自己的一天。**

---

## Registry

本文档定义了若干跨系统事实，**待登记到 `design/registry/entities.yaml`**：

- `formulas`: `revive_at_abs_hour`（F2 / F3 两条实现，由 `DowntimeUnit` 选一）
- `constants`: `death_downtime_village_hours`（= 24，随 Q1 可能改为 `[15,24)` 的会话式）

> `entities.yaml` 的 `entities` / `items` / `formulas` / `constants` 四节目前**均为空数组**，
> 上游 `time-and-schedule.md` 也有一组待登记的同类条目。
> 登记前需用户确认（见本次会话的收尾提问）。
