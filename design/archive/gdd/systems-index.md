# Systems Index: 村长与冒险者

> **Status**: Under Review
> **Created**: 2026-10-17
> **Last Updated**: 2026-10-17
> **Source Concept**: `design/game-brief.md`

> ## ⚠️ TRACEABILITY RULE — applies to every GDD written from this index
>
> **每个系统都必须能追溯到开发者亲口说过的话**，下方「开发者原话」栏就是依据。
> GDD 作者**不得发明开发者没有描述的功能**。
>
> 这条规则有代价，不只是一句提醒：本项目已经因为「我加了个开发者从没提过的系统」
> 返工过一次（概念 2 的装备套装系统）。写不出来的地方，**写进 GDD 的「Open
> Questions」，不要自己补**。

---

## Overview

玩家是穿越到异世界大陆的村长，通过一个系统从地球召唤「玩家」——他们以为这是一款游戏。
村庄在 45° 俯视等距视角下真实生长（三间土屋 → 烟囱 → 井 → 围栏 → 铁匠铺），
而召唤来的村民按**地球时区的作息**上线下线、按**性格**决定效率与偏好，
村庄自己会过日子。核心一句：**村庄是活的，而村民不知道自己活在一个真的世界里。**

---

## Systems Enumeration

| # | System Name | Category | Priority | Status | Design Doc | 开发者原话（依据） | Depends On |
|---|-------------|----------|----------|--------|------------|-----------------|------------|
| 1 | 时间与作息 | Core | MVP | In Review | `design/gdd/time-and-schedule.md` | 「什么早班晚班的，不区分的，地球一边是晚上，另一边就是早上了呀」 | — |
| 2 | 召唤系统 | Core | MVP | In Review | `design/gdd/summoning.md` | 「通过英雄酒馆的方式，召唤英雄」「英雄是从异世界召唤来的人物，不同动漫，影视，游戏世界召唤来的，每发个英雄都独一无二」 | — |
| 3 | 性格系统 | Core | MVP | In Review | `design/gdd/personality.md` | 「有些喜欢打副本，有些是休闲玩家，有些是pvp玩家等等，这些都能通过性格来表现，根据他们的性格权重，他们会自行去做一些事情」「性格不是二极管…只是说效率会慢点」 | 召唤系统 |
| 4 | 配队与配合 | Gameplay | MVP | In Review | `design/gdd/squad-and-cohesion.md` | 「英雄除了让我配队外，日常也会自己根据性格去行动」 | 性格系统、时间与作息 |
| 5 | 村庄建造 | Gameplay | MVP | In Review | `design/gdd/village-building.md` | 「一开始三间土屋，招来人之后开始有烟囱、然后有井、有围栏、有铁匠铺」「这些英雄共同建造我们的村庄」 | 性格系统、时间与作息 |
| 6 | 任务与副本 | Gameplay | MVP | In Review | `design/gdd/expedition.md` | 「通过发布任务收集材料完成指定任务来发展」「身为村长，有时候也会组织一些人来攻略副本，出去做任务」 | 配队与配合、性格系统 |
| 7 | 功能性建筑与依赖网 | Progression | Vertical Slice | In Review | `design/gdd/functional-buildings.md` | 「升级村庄，给村庄配备功能性建筑，这样玩家就能去这些建筑里购物，比如武器店，服装店，又或者一些试炼塔，作用是让玩家快速升级，还有防护罩，作用是保护村庄不被外面攻击」 | 村庄建造 |
| 8 | 经济与摆摊 | Economy | Vertical Slice | In Review | `design/gdd/economy-and-stalls.md` | 「有些玩家还会在村庄周边，防护罩内自行摆摊等，我能收取摆摊费」 | 功能性建筑 |
| 9 | 交易、背叛与 PvP | Gameplay | Alpha | In Review | `design/gdd/trade-and-betrayal.md` | 「玩家间的交易也是有彩蛋的，比如有些人坏，它会交易一些不好的东西给别人，别人发现了会造成损失，然后就会出现吵架，最后pvp等等，上线就追着砍，被砍的只能跑野外升级，变强了再回来报仇」 | 经济与摆摊、性格系统 |
| 10 | 死亡与复活 | Core | Vertical Slice | In Review | `design/gdd/death-and-revival.md` | 「死亡可以让这个玩家，过一天才能上线」 | 时间与作息 |
| 11 | 杂谈 | UI | MVP | In Review | `design/gdd/chronicle.md` | 「平时村庄杂谈会记录这些英雄做的事」「只是一个功能，一个文字面板」 | 全部事件源 |
| 12 | 本土角色与本土剧情 | Narrative | Alpha | In Review | `design/gdd/native-folk.md` | 「这个异世界大陆也有本土角色，本土剧情」 | 时间与作息 |
| 13 | 村长升级线 | Progression | Vertical Slice | In Review | `design/gdd/chief-progression.md` | 「获取材料，升级村庄」「升级村长」「最终成超大城市，成为游戏里最豪华的城市」 | 村庄建造、任务与副本 |
| **14** | **大事件**（框架，含实例） | Gameplay | Vertical Slice | In Review | `design/gdd/beasts-and-raids.md`（实例①兽潮）<br>`design/gdd/caravan.md`（实例②商队，进行中） | 「添加大事件，比如兽群进攻」+「**兽潮是我举的一个例子，但不是唯一例子，需要支持后续更新更多的大事件**」 | 时间与作息、性格系统、功能性建筑、杂谈 |
| **15** | **村民关系** | Gameplay | Vertical Slice | In Review | `design/gdd/relations.md` | 「我举例两个玩家可以有仇，**但不代表你就只能有仇啊，你需要扩展**」 | 性格系统、配队与配合、经济与摆摊 |
| **16** | **玩家成长** | Progression | Vertical Slice | In Review | `design/gdd/player-progression.md` | 「试炼塔，作用是让玩家快速升级」+「**这个就需要后续设计玩家的升级机制…你需要帮我补全缺失的东西**」 | 召唤系统、任务与副本 |
| **17** | **怪兽谱系** | Gameplay | Vertical Slice | In Review | `design/gdd/monsters.md` | 「我现在想要**在村庄外，允许出现怪兽**」 | 大事件 #14 |

> ⚠️ **#14 是「框架 + 实例」的复合系统**，与其它 16 个不同：
> 它要能被后续的大事件复用。**九槽位表在 `beasts-and-raids.md` § A。**
> **框架本身尚未抽象** —— 按裁定⑳，要等实例②（商队来访）落地之后才做。

---

## Categories（仅本项目用到的）

| Category | 本项目中的含义 |
|----------|----------------|
| **Core** | 一切的地基：时间、人、性格 |
| **Gameplay** | 玩家实际操作的玩法 |
| **Progression** | 村庄与村长的两条成长线 |
| **Economy** | 三层经济与抽成 |
| **Narrative** | 杂谈与本土剧情 |
| **UI** | 玩家看得见的信息 |

---

## Priority Tiers

| Tier | Definition | Target |
|------|------------|--------|
| **MVP** | 没有这些就验证不了核心 | 第一个可玩原型 ✅ 已达成 |
| **Vertical Slice** | 一个完整、精细的区域 | 垂直切片 |
| **Alpha** | 功能齐备、允许占位内容 | Alpha |
| **Full Vision** | 打磨与内容完备 | Beta / Release |

---

## Dependency Map

### Foundation Layer（无依赖）

1. **时间与作息** —— 一切按它推进。村庄的「营业时间」由人口增长撑长，这是免费的设计
2. **召唤系统** —— 村民从哪来。没有它后面全空

### Core Layer（依赖 Foundation）

3. **性格系统** —— 改的是**效率系数**，不是许可
4. **配队与配合** —— 谁搭伙；配合系数 + 结仇
5. **村庄建造** —— 看得见的成长；人不在就停工
6. **任务与副本** —— 材料的唯一来源

### Feature Layer（依赖 Core）

7. **功能性建筑与依赖网** —— 建筑互相咬合，不是等级条
8. **经济与摆摊** —— 三层经济
9. **交易、背叛与 PvP** —— 因果链，不是剧本
10. **死亡与复活** —— 停机一天
11. **本土角色与本土剧情** —— 不依赖玩家的第二条内容线

### Progression Layer

12. **村长升级线** —— 视野变大，不是拳头变大
13. **杂谈** —— 自主行为的唯一读数器

---

## Recommended Design Order

| Order | System | Priority | Layer | Agent | Effort |
|-------|--------|----------|-------|-------|--------|
| 1 | 时间与作息 | MVP | Foundation | systems-designer | M |
| 2 | 召唤系统 | MVP | Foundation | game-designer | M |
| 3 | 性格系统 | MVP | Core | game-designer | M |
| 4 | 配队与配合 | MVP | Core | game-designer | S |
| 5 | 村庄建造 | MVP | Core | game-designer | M |
| 6 | 任务与副本 | MVP | Core | systems-designer | M |
| 7 | 杂谈 | MVP | Presentation | narrative-director | S |
| 8 | 功能性建筑与依赖网 | VS | Feature | game-designer | L |
| 9 | 经济与摆摊 | VS | Economy | economy-designer | L |
| 10 | 死亡与复活 | VS | Feature | systems-designer | S |
| 11 | 村长升级线 | VS | Progression | game-designer | M |
| 12 | 交易、背叛与 PvP | Alpha | Feature | game-designer | L |
| 13 | 本土角色与本土剧情 | Alpha | Narrative | narrative-director + world-builder | L |

---

## Circular Dependencies

- **性格系统 ↔ 配队与配合**：配合系数依赖性格，编队又决定谁触发性格事件。
  用 interface 断开——配队只读性格的**系数**，不读性格的**事件权重**。
- **村庄建造 ↔ 任务与副本**：材料来自副本，人力来自建造。
  断开方式：建造只消耗「在场劳动力」，不关心材料的来源。

---

## High-Risk Systems

| System | Risk Type | 风险 | Mitigation |
|--------|-----------|------|------------|
| 性格系统 | Design | 「性格不是二极管」很难量化——效率系数和偏好权重怎么分离，会不会退化成两个换皮 | 最早做，且必须能被 playtest 证伪 |
| 交易、背叛与 PvP | Scope | 因果链很长（坏→次品→发现→损失→吵架→PvP→追砍→跑野→复仇），极易膨胀成一个独立的 PvP 游戏 | 明确切到 Alpha；先做前两环 |
| 功能性建筑 | Scope | 建筑互相咬合，很容易变成一堆积压公式 | 先画依赖图再填数值 |
| 本土角色 | Scope | 本土人一旦写深就是第二个游戏的剧本量 | 只定义「他们承担什么功能」，不写剧情 |

---

## Progress Tracker

| Metric | Count |
|--------|-------|
| 系统总数 | **17**（原 13 + 大事件 / 村民关系 / 玩家成长 / 怪兽谱系） |
| GDD 文档已落地 | **17 / 17** |
| 已按 20 条裁定回填 | 17 / 17 |
| 交叉验证 | ✅ 26 条冲突已出报告 |
| 未决问题总数 | **165 条** |
| 需开发者拍板 | 见 `production/design-decisions.md`（已闭 20 条） |
| 术语消歧 | ✅ 已横扫 |

---

## Next Steps

- [x] 系统枚举（按开发者原话提取，非发明）
- [x] 13 份原始 GDD
- [x] 裁定回填（`personality` / `squad-and-cohesion` / `chronicle` / `trade-and-betrayal` / `summoning` / `chief-progression`）
- [x] 新增 4 份（`relations` / `player-progression` / `monsters` / `beasts-and-raids`）
- [x] 交叉验证（26 条冲突 → `production/cross-doc-consistency.md`）
- [x] 术语消歧（→ `production/terminology-sweep.md`）
- [ ] **大事件实例②：商队来访** — 进行中
- [ ] **大事件框架抽象** — 等实例②落地（裁定⑳）
- [ ] 补 `registry/entities.yaml` 新实体（`BondPair` / `Practice`·`Edge`·`Deed` / `Wear`）
- [ ] 把 165 条未决问题整理成一次性过目清单
