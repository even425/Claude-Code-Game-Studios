# QA 工具箱

> **一句话**：`python production/qa/verify-all.py` —— 验证整个仓库。
>
> 散落的验证步骤意味着**每次都会漏掉几个**。这里把它们收在一处。

## 目录

| 文件 | 干什么 | 什么时候跑 |
|---|---|---|
| `verify-all.py` | **一条命令**跑完所有验证（构建 ×2 → 单元测试 → 真机自检 → 参数台账） | 每次改动之后 |
| `run-selfchecks.py` | 跑 11 个**真机自检**开关，并**证明它们没碰玩家存档** | 改了游戏逻辑之后 |
| `param-inventory.py` | 生成 / 校验**参数台账**（代码里的每个数值常量） | 改了任何数值之后 |
| `parameters.json` | 参数台账的**单一真源**（标量参数 + 表类参数） | 跟上面的脚本一起 |
| `shot-check.py` | 截真图并检查**画面不是空图** | 改了表现层之后（需要显示器） |
| `recalibrate.py` | 重跑经济标定（扫 X → 稳态 / 分布） | 改了价格 / 掉落 / 报酬之后 |
| `mutate-*.py` | **变异矩阵**：把代码改坏，确认对应的守卫会红 | 改了守卫之后 |

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

### ④ 跳过要说出来

`verify-all.py` 里因前提缺失被跳过的项会**明确打印**（跳过了什么、为什么），
`--require-all` 可以把「跳过」也变成失败。

## 变异矩阵一览

| 脚本 | 守的是什么 |
|---|---|
| `mutate-worldtable.py` | 世界表加载器的校验（权重 / 名字 / 数组 / 兜底） |
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

全部一起跑（很慢，约 20 分钟）：

```
python production/qa/verify-all.py --with-mutations
```

## 新增守卫时要做的事

1. 在 `tests/unit/` 写测试（**先看 `Architecture`/`acceptance` 目录里的既有写法**）。
2. 写一个 `mutate-*.py` 里的变异，确认守卫会红。
3. 在这里（本文档）的表格里加一行。
4. 若改动了参数 ⇒ 同步 `parameters.json`，然后 `python production/qa/param-inventory.py --write`。
