# QA 工具箱

> **一句话**：`python production/qa/verify-all.py` —— 验证整个仓库。
>
> 散落的验证步骤意味着**每次都会漏掉几个**。这里把它们收在一处。

## 目录

| 文件 | 干什么 | 什么时候跑 |
|---|---|---|
| `verify-all.py` | **一条命令**跑完所有验证（构建 ×2 → 单元测试 → 真机自检 → 参数台账） | 每次改动之后 |
| `run-selfchecks.py` | 跑 7 个**真机自检**开关，并**证明它们没碰玩家存档**（**默认固定种子** ⇒ 结果可复现；失败原因以 **annotation** 形式公开可读） | 改了游戏逻辑之后 |
| `param-inventory.py` | 生成 / 校验**参数台账**（代码里的每个数值常量）。`--sync-values` 把台账的**值**同步成代码里的当前值 | 改了任何数值之后 |
| `parameters.json` | 参数台账的**单一真源**（标量参数 + 表类参数） | 跟上面的脚本一起 |
| `shot-check.py` | 截真图并检查**画面不是空图** | 改了表现层之后（需要显示器） |
| `frame-budget.py` | 量每个真机自检**到底需要多少帧** | 改自检的帧数上限之前（别凭感觉） |
| `scan-cited-paths.py` | 扫活文档引用的路径：**在磁盘 / 在版本库 / 被 gitignore** 三态 | 想知道「哪条引用只在你的机器上成立」时 |
| `recalibrate.py` | 重跑经济标定（扫 X → 稳态 / 分布） | 改了价格 / 掉落 / 报酬之后 |
| `sync-price-fallback.py` | 把**内置兜底**价格表同步成 `config/prices.json`（两份副本不许分叉） | 改了价格之后（`recalibrate.py` 会自动调它） |
| `mutate-*.py` | **变异矩阵**：把代码改坏，确认对应的守卫会红 | 改了守卫之后 |
| `_utf8out.py` | 让本目录的脚本在**任何**控制台编码下都能把话说出来 | 不是工具，是**每个工具的前置**（见约定 ⑤） |

## 验收计划的覆盖现状（2026-10-21 核实）

| 表 | 已写 | 未写 | 未写的**原因** |
|---|---|---|---|
| 不变量 I1–I16 | **16** | 0 | （无未写项 —— **I15 已于 2026-10-21 闭合**：`SettlementAtomicityTests`，真服务 + 变异 4/4） |
| A-01…A-12 | **10** | 2 | **A-02** 取值待基线 §16 #3（结构面已写）；**A-08** 需 T-002（行动频率与耗时） |

**逐条状态看 `production/06_第一阶段验收与测试计划_V1.0.md` 的正文** ——
每个断言小节末尾都有「✅ 已写」或「为什么还写不出来」。
**没有「可写但没写」的残留**（2026-10-21 做过一次全表扫荡）。

> **上面这两个数字是机器盯着的**：守卫 `AcceptanceCoverageCountsTests`
> 每次 `dotnet test` 都会拿 `06` 的正文**逐行算一遍**，和这张表**逐个数字**比对 ——
> 改了正文忘了改这张表 ⇒ **当场红**。
>
> 真事：2026-10-21 I9 闭合之后，`06` 的正文已经写「✅ 已证明」，
> 而这张表还写着「**14** | 2（I9 受阻…）」—— **读者最先看到的那个数字是错的**。
>
> 状态怎么分类：**看格里的首个标记**（`✅` / `🟡` / `受阻` / `需`），
> **不看整格有没有那个词**。I16 的状态格**提到**过「受阻」，
> 但那是在说「原『受阻：…』是过期状态」—— **历史陈述不算现状**
> （与 `LedgerConsistencyTests` 同一条原则）。首个标记不在已知集合里 ⇒ **报错**，
> 不许猜一个类别。

## 关键约定

### ① Debug 与 Release **都要构建**

Godot headless 加载的是 `.godot/mono/temp/bin/Debug/` 的程序集。
只构建 Release ⇒ headless 跑的是**旧代码**，**而且不报错**。
症状是「跑通了、数据却对不上」—— 极难查。`verify-all.py` 因此**先 Debug 后 Release**。

### ② 自检不许碰玩家存档

`run-selfchecks.py` 跑之前记录玩家存档的 SHA-256 与时间戳，跑完再比。
**不一样就是失败。** 自检一律用**自己的端口**（`selftest-village-save.json`）。

### ③ 守卫必须**变异看红**

新写一条守卫 ⇒ 必须写一个对应的变异（`mutate-*.py`），
确认**把代码改坏时它真的会红**。

> **不养烧不着的守卫。** 一条永远绿的守卫不是保护，是装饰 ——
> 它让人以为「这一段有人盯着」，而实际盯着的可能是另一条更容易被改弱的规则。

### ④ 想知道「CI 为什么红」，就在本地拉一份**干净检出**跑一遍

CI 跑的是**干净检出**：只有**版本库里**的东西 + 一个**没有被 gitignore 的**工作副本。
而你的工作副本里可能有别的东西（本地工具、被 ignore 的文件、没提交的改动）
⇒ **「本机全绿、CI 全红」是完全可能的**，而且日志上看起来一模一样。

复现方式（真的用过，好几回了）：

```bash
git clone --depth 1 --branch mytown https://github.com/<你>/<仓库>.git ci-check
cd ci-check
dotnet test tests/Claude-Code-Game-Studios.Tests.csproj -c Release
```

**本轮就靠它定位了一次 CI 失败**：某个提交上 `06` 引用了 `tests/_spike_test.cs`，
而那个文件**被 .gitignore 排除** ⇒ 本机有、干净检出里没有 ⇒ 守卫在 CI 上红、本机绿。
（根因是守卫当时查的是「磁盘上的文件」；现在改成查 **`git ls-files`**（版本库里的）
**并豁免被 gitignore 的路径** ⇒ 两边答案一致了。）

### ⑤ 跳过要说出来

`verify-all.py` 里因前提缺失被跳过的项会**明确打印**（跳过了什么、为什么），
`--require-all` 可以把「跳过」也变成失败。

### ⑥ 脚本必须**在控制台上说得出来**

本机 Windows 控制台默认是 **GBK（cp936）**，而脚本里满是「✓ / ✗ / ⇒ / ✅」这类符号 ——
**GBK 里没有它们** ⇒ `print` 抛 `UnicodeEncodeError` ⇒ 脚本**直接崩**。

这曾真实发生：`run-selfchecks.py` 跑到**第一个通过项**就炸，
后面 6 个自检**一个都没跑**，而它最后那句「全部 7 个自检通过」压根没机会打印。
**「工具跑不动」和「工具跑了但没发现问题」在日志上看起来一模一样** —— 所以这是守卫级别的事。

修法：每个脚本开头

```python
try:
    import _utf8out  # noqa: F401
except ImportError:
    pass
```

由 `QaToolingTests`（`tests/unit/architecture/`）守住 —— **新脚本忘了加就红**。

### ⑦ 规则层必须**在没有 Godot 的情况下**编译**并运行**得出来

`tests/DomainPurity/DomainPurity.csproj` 是一个**零依赖**的控制台程序
（不用 `Godot.NET.Sdk`、**不引任何 NuGet 包**、不 `ProjectReference` 主工程），
它只直接编译 `src/Domain/**`，然后自己跑规则断言、并检查**产物的程序集引用里没有 Godot**。

**为什么不能靠测试工程**：测试工程引用整个主工程（因而引用 `GodotSharp`），
在那边跑测试**证明不了**「领域层脱离 Godot 也能编译运行」。这正是不变量 **I9** 的原文。

⚠️ **零依赖是硬要求，而且是踩出来的**：本工程**曾经**用 `gdUnit4.api` 当测试运行器，
只引它确实能编译能跑，**看起来 gdUnit4 不需要 GodotSharp** ——
**但 `gdUnit4.api` 传递依赖 `GodotSharp`** ⇒ 依赖图里仍有它 ⇒
领域层写一行 `using Godot;` **照样编译通过** ⇒ **那道门形同虚设**。
**一个「看起来在守、其实不守」的守卫比没有守卫更糟**（它让人以为有人盯着）。

### ⑧ CI 红了也要能**一眼看出原因**

CI 的 job 日志**要登录才能看**（`/logs` 端点还要求 admin）⇒ 对自动化环境来说，
「CI 红了」等于「只知道红了，**不知道哪一条、为什么**」。

⇒ `run-selfchecks.py` 在 GitHub Actions 上会把每个失败的自检写成
**workflow command**（`::error title=…::…`）⇒ runner 把它转成
**check-run annotation** ⇒ 公开仓库用**无认证的 API** 就能读到失败原因。
全部通过时也留一条 `::notice`，这样「自检跑到哪一步了」不必翻日志。
CI 同时把结果表写进 **job summary**（网页上直接看得到）。

> **这不是锦上添花**：run #21 的 Godot 作业红了，而当时能读到的 annotation
> 只有一句「Process completed with exit code 1.」—— **零信息量**。
> 一个**说不出原因**的红色信号，等于一个**需要人工到场**的信号。

⚠️ 消息必须**单行**：GitHub 的 workflow command 是单行的，换行会被截断，
甚至可能注入一条新的 command ⇒ `_gh_command` 把换行**折叠成空格**。

### ⑨ 自检必须**可复现**

自检的随机源来自**英雄生成**（`HeroFactory.Seed`）⇒ 每次运行的英雄构成
（名字 / 世界 / 性格 / 属性）都不同 ⇒ **判定结果会随「这次抽到哪组英雄」而变**。

⇒ `run-selfchecks.py` **默认传固定种子** ⇒ 同一份代码每次跑的判定**逐条一致**（实测）。
想跑随机构成用 `--seed=random`。

⚠️ **诚实标注**：这**不是**已证实的 CI 偶发根因 —— 20 个种子 ＋ 9 轮随机**全部通过**，
所以「英雄构成随机」**只是假设**。固定种子的价值是：① 判定本身**可复现**
（这是测试的基本要求）；② 万一再出现偶发，它**稳定复现**而不是随机出现 ——
而**可复现是修复的前提**。

> **取舍**：固定种子意味着真机自检只覆盖**一组**英雄构成。
> **组合覆盖由单元测试负责** —— 它自己造数据、不需要真机、且一次能覆盖很多组。
> （真机自检的职责是「整套跑得通」，不是「穷举各种组合」。）

## 变异矩阵一览

| 脚本 | 守的是什么 |
|---|---|
| `mutate-structure.py` | UT-01 结构断言（职责分布 / 穷尽分类 / 自检文件纯净） |
| `mutate-acceptance.py` | UT-02 / UT-04 / UT-10 验收断言 |
| `mutate-static-defs.py` | UT-07 静态定义不得被运行时写回 |
| `mutate-player-commands.py` | IT-03 玩家不能指定英雄做什么 |
| `mutate-freeze-village.py` | IT-30 副本不冻结村庄 |
| `mutate-hero-ids.py` | IT-25 唯一 Id + 撞号防护 |
| `mutate-write-points.py` | IT-27 领域层没有公开写入口 |
| `mutate-doc-guard.py` | 文档卫生（控制字符 / 已判死设计线的标记） |
| `mutate-param-ledger.py` | 参数台账与代码一致（标量 + 四张表） |
| `mutate-config-fallback.py` | 外部配置与内置兜底不许分叉（价格表 / 世界表） |
| `mutate-death-cost.py` | IT-16 死亡代价：领域层不许有「永久损失」字段 |
| `mutate-mt04-visible.py` | MT-04 存档重载后的可见状态（**含「界面没跟着刷新」那一档**） |
| `mutate-qa-tooling.py` | QA 工具箱自身（脚本能说话 / README 与磁盘一致） |
| `mutate-ledger-consistency.py` | `07` 台账自身（两处状态表不许互相矛盾 / 现状必须是真的） |
| `mutate-doc-citations.py` | 活文档点名的测试必须真的存在（文件 / 类 / 方法） |
| `mutate-price-fallback.py` | 价格表两份副本不许分叉 + 同步脚本不许帮倒忙 |
| `mutate-ledger-crossref.py` | 两张参数台账之间的链接不许悬空 / 占位值不许无主 |
| `mutate-domain-purity.py` | 领域层脱离 Godot 编译（不变量 I9）：加一行 `using Godot;` ⇒ 编译失败 |
| `mutate-acceptance-coverage.py` | 验收覆盖摘要不许过期（`06` 正文 ↔ 本文档「覆盖现状」表的两个数字） |
| `mutate-settlement-atomicity.py` | I15 结算要么全应用要么不应用（三处一起动 / 三处都不动） |
| `mutate-dungeon-party.py` | IT-05 队伍不满足参与条件时既不建任务也不开副本（+ 正向条件） |
| `mutate-combat-party-input.py` | 战斗能拿到队伍配置（IT-31 的前置）：快照是值、且真的传到了解算器 |

全部一起跑（**很慢** —— 每个脚本都要重建 + 重跑测试，建议过夜跑）：

```
python production/qa/verify-all.py --with-mutations
```

## 新增守卫时要做的事

1. 在 `tests/unit/` 写测试（**先看 `Architecture`/`acceptance` 目录里的既有写法**）。
2. 写一个 `mutate-*.py` 里的变异，确认守卫会红。
3. 在这里（本文档）的表格里加一行。
4. 若改动了参数 ⇒ 同步 `parameters.json`，然后 `python production/qa/param-inventory.py --write`。
5. 若守卫守的是一条**通道**（写文件、输出 annotation、发请求）⇒
   **通道本身也要有断言** —— 「跑得通」不等于「写对了」：
   `--summary` 那条路径一开始**没有任何覆盖**，写坏了也不会红；
   而它恰恰是「CI 红了时人第一眼看到的东西」。
