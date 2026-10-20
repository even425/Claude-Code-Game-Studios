"""
给「审查对象已判死」的旧审查文档加状态横幅。

**背景**：2026-10-17 那一天，项目先写了 13 份 GDD（`design/gdd/`），
做了交叉一致性审查、内容缺口审计、体验缺口审计、未决问题合并清单……，
**同一天**，开发者拍板 D-002「完全作废，一切以总纲为准」，GDD 全部归档到
`design/archive/gdd/`。

但这 8 份审查报告**留在 `production/`（现行文档目录）**里，且**没有任何「已判死」标记** ——
读者会把 26 条交叉冲突当成现行待办，
甚至照着**已被明令禁止引用**的公式与常数去改代码（D-002 原文）。

**本脚本**只做一件事：给这些文件插一段状态横幅。**不改任何正文。**

用法：python production/qa/mark-voided-audits.py [--check]
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROD = ROOT / "production"

MARKER = "【2026-10-21 补状态标记 · 审查对象已判死】"

COMMON = f""">
> 本文件审查 / 汇总的是**第一条设计线**的产物（`design/gdd/` 下的 GDD，另有 `design/game-brief.md`）。
> 那条线已于 **2026-10-17 由 `production/07` 的 D-002 判死** ——
> 开发者原话：「**完全作废，一切以总纲为准**」。文档现归档在 `design/archive/gdd/`。
>
> ⇒ **本文列出的冲突 / 缺口 / 待确认项**不是**现行待办**。
> ⇒ **引用其中的公式、常数、实体定义是被明令禁止的**（D-002 原文：
> 「编码与设计时**不得**引用其中的公式、常数或实体定义。任何看起来『已经定过』的说法，
> 若只在该注册表中有记载，**一律视为未确认**」）。
> ⇒ 保留本文是为了留存**方法与教训**（怎么审、怎么找矛盾），**不是待办清单**。
>
> **现行有效设计文档**（清单见 `design/archive/README.md`）：
> `production/异世界村庄经营与英雄冒险游戏_设计基线_V1.2.md`（唯一权威基线）
> ＋ `production/07_技术决策与待确认事项.md`（决策台账）。
> **要动设计，请看这两份。**
"""

# 文件 → 横幅正文（逐份写清楚「本文到底审的是什么」，避免千篇一律）
FILES = {
    "cross-doc-consistency.md": "本文件是 13 份 GDD 的**交叉一致性审查报告**（数值 / 规则 / 概念 / 依赖 / 未决问题五类比对，26 条）。",
    "content-gap-audit.md": "本文件是 18 份 GDD 摘要的**内容缺口审计**（「一年之后的村庄长什么样」）。",
    "experience-gap-audit.md": "本文件是**玩家体验缺口审计**（「玩起来是什么感觉」），输入是开发者口述 + `design/game-brief.md`。",
    "design-review-findings.md": "本文件是 GDD 作者提出的、**需要开发者拍板的问题**的汇总。",
    "open-questions-consolidated.md": "本文件是 17 份 GDD 的 `## Open Questions` 的**归组与登记**（它引用的 `design-decisions.md` 与 `cross-doc-consistency.md` 同属这条线）。",
    "design-decisions.md": "本文件是 13 份 GDD 写完后的 **19 条裁定**（2026-10-17）。",
    "terminology-sweep.md": "本文件是 17 份 GDD 的**术语消歧横扫报告**（它给 GDD 批量加了声明、改了 7 句正文）。",
    "project-stage-report.md": "本文件是 **2026-10-17 的项目阶段快照**（stage / confidence / 孤儿文件统计）。",
}


def banner_for(name):
    head = FILES[name]
    return f"> ⚠️ **{MARKER}**\n>\n> {head}\n{COMMON}"

def main():
    check_only = "--check" in sys.argv
    problems = []

    for name, _ in FILES.items():
        p = PROD / name
        if not p.exists():
            problems.append(f"{name} 不存在")
            continue

        text = p.read_text(encoding="utf-8")
        if MARKER in text:
            if check_only:
                print(f"  [已有横幅] {name}")
            continue

        if check_only:
            problems.append(f"{name} 缺状态横幅")
            continue

        # 插在标题行之后（标题 + 空行）
        lines = text.split("\n")
        if not lines or not lines[0].startswith("#"):
            problems.append(f"{name} 首行不是标题，跳过")
            continue
        insert_at = 1
        while insert_at < len(lines) and lines[insert_at].strip() == "":
            insert_at += 1
        block = [banner_for(name).rstrip("\n"), ""]
        lines[insert_at:insert_at] = block
        p.write_text("\n".join(lines), encoding="utf-8", newline="")
        print(f"  [已加横幅] {name}")

    if problems:
        print("!! 问题：")
        for x in problems:
            print("   ", x)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
