"""
扫**相对价格结构**对经济的影响 —— 给 T-007 的价格拍板提供**决策依据**。

**它是什么**：把若干个候选价格表各跑一遍 1000 天模拟，把关键指标并排摆出来。
**它不是什么**：它**不决定价格** —— 价格是开发者的拍板（T-007）。

**为什么扫「相对结构」而不是「绝对值」**
--------------------------------------------------------------------------------
整体同比例缩放价格**不改变**相对结构 ⇒ 成交次数不变、只有金额变 ⇒ 信息量小。
而**驼峰位置**（在玩的钱上限 / 货物地板）是 D-054 实测出来的机制，
它由**相对价格**决定（石头贵 ⇒ 买石头的人少 ⇒ 石头占用的钱多 ⇒ 驼峰左移）。
⇒ 所以本工具扫的是「**谁相对更贵**」这一个轴。

**同种子 ⇒ 可比**：同 seed ⇒ 随机流相同 ⇒ 跨价格档的差异**只**来自价格。
**多种子 ⇒ 结论才站得住**：单样本差异可能是噪声（本仓库铁律：
「跨版本比较经济/模拟数据：先量种子方差，再下结论」）。
⇒ 所以 `--seeds` 默认给**三个**种子，且报告会明说「各档状态是否一致」。

**用法**
--------------------------------------------------------------------------------
    # 扫默认的三档（基准 / 石料贵 / 布匹贵），3 个种子
    python production/qa/price-scan.py

    # 自定义档位与种子
    python production/qa/price-scan.py --tabs "5/3/8,10/3/8,5/3/16" --seeds 4242 7777

    # 只看一个档位、跑久一点
    python production/qa/price-scan.py --tabs "8/5/12" --days 2000

**⚠️ 它不会碰仓库里的 `config/prices.json`**
--------------------------------------------------------------------------------
候选价格表写在临时目录，用 `recalibrate.py --prices <路径>` 做**一次性覆盖**
（那条路径**专门**为此设计：它会跳过同步内置兜底 ⇒ 仓库状态不被实验污染）。
仓库里的价格表与内置兜底**保持原样**，直到开发者真的拍板。

**⚠️ 结论的边界**
--------------------------------------------------------------------------------
1. **它只回答「相对结构如何影响成交与沉淀」**，不回答「哪个绝对值好」。
2. **分布退化（中位 2–4 金 vs 最大 500+）是另一个问题** —— 那是 D-054 已裁决的
   「分布约束」，与价格结构**部分独立**：本工具会把它**摆出来**，但不假装解决它。
3. **「流通金增长率 ≈ 0」不等于「经济健康」** —— 退化的稳态也是稳态。
   报告里因此**同时**给出成交笔数与金币分布，而不只给增长率。
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import argparse
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / "production" / "qa"
RECALIBRATE = QA / "recalibrate.py"

# 默认三档：只动「谁相对更贵」这一个轴，其余不动。
DEFAULT_TABS = [
    ("基准（当前占位）", "5/3/8"),
    ("石料相对贵", "10/3/8"),
    ("布匹相对贵", "5/3/16"),
]
DEFAULT_SEEDS = [4242, 7777, 31337]


def parse_tab(spec: str):
    """'5/3/8' → {'石料':5,'木料':3,'布匹':8}。"""
    parts = spec.strip().split("/")
    if len(parts) != 3:
        raise ValueError(f"档位写法应为 石料/木料/布匹 三个整数，收到：{spec!r}")
    stone, wood, cloth = (int(p) for p in parts)
    if min(stone, wood, cloth) <= 0:
        raise ValueError(f"价格必须为正（0 表示不卖）：{spec!r}")
    return {"石料": stone, "木料": wood, "布匹": cloth}


def write_candidate(tmpdir: Path, label: str, prices: dict) -> Path:
    table = {
        "_说明": "**候选档位，不是定案** —— 只为让相对价格结构的影响可见。绝对值由开发者拍板（07 的 T-007）。",
        "_来源": f"price-scan.py 实验档位「{label}」。",
        "_注意": "0 = 该物品「不卖」；负数会被拒绝。",
        **prices,
    }
    path = tmpdir / (re.sub(r"[^\w一-鿿-]+", "_", label) + ".json")
    path.write_text(json.dumps(table, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8", newline="\n")
    return path


def run_tab(label, prices_path, days, seed, x):
    r = subprocess.run(
        [sys.executable, str(RECALIBRATE),
         "--prices", str(prices_path),
         "--x", str(x), "--days", str(days), "--seeds", str(seed),
         "--skip-build"],
        cwd=ROOT, capture_output=True, text=True,
        encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    growth = re.search(r"X=[\d.]+ seed=\d+:\s*([^\n]+)", out)
    state = re.search(r"→ (稳态|仍在缩|仍在涨)", out)
    trades = re.search(r"(\d+) 笔 / (\d+) 份 / (\d+) 金", out)
    per_trade = re.search(r"笔均 ([\d.]+) 份", out)
    dist = re.search(r"最小 (\d+) / p25 (\d+) / 中位 (\d+) / p75 (\d+) / 最大 (\d+)", out)
    conserve = re.search(r"守恒律成立 (\d+)/(\d+)", out)
    return {
        "label": label, "seed": seed,
        "state": state.group(1) if state else "?",
        "growth": growth.group(1).strip() if growth else "?",
        "trades": int(trades.group(1)) if trades else None,
        "amount": int(trades.group(3)) if trades else None,
        "per_trade": per_trade.group(1) if per_trade else "?",
        "dist": dist.groups() if dist else None,
        "conserve": f"{conserve.group(1)}/{conserve.group(2)}" if conserve else "?",
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--tabs", default="",
                    help="档位，逗号分隔，每档是 石料/木料/布匹（默认三档：基准 / 石料贵 / 布匹贵）")
    ap.add_argument("--days", type=int, default=1000)
    ap.add_argument("--seeds", type=int, nargs="+", default=DEFAULT_SEEDS)
    ap.add_argument("--x", type=float, default=0.1, help="金币报酬率 X（D-054 已拍板值）")
    args = ap.parse_args()

    if args.tabs:
        tabs = []
        for i, spec in enumerate(args.tabs.split(","), 1):
            spec = spec.strip()
            tabs.append((f"档{i} {spec}", parse_tab(spec)))
    else:
        tabs = list(DEFAULT_TABS)

    tmpdir = Path(tempfile.mkdtemp(prefix="price-scan-"))
    print("=" * 78)
    print("价格结构扫描 —— **决策依据，不是拍板**（T-007 仍是开发者的决定）")
    print("=" * 78)
    print(f"天数 {args.days} · 种子 {args.seeds} · X {args.x}")
    print(f"档位：{[t[0] for t in tabs]}")
    print()

    rows = []
    for label, prices in tabs:
        path = write_candidate(tmpdir, label, prices)
        for seed in args.seeds:
            row = run_tab(label, path, args.days, seed, args.x)
            rows.append(row)
            print(f"{label:<18} seed={seed:<6} {row['state']:<5} "
                  f"{row['growth'][:40]:<42} 成交 {row['trades']} 笔 / {row['amount']} 金",
                  flush=True)

    print()
    print("=" * 78)
    print("并排对比")
    print("=" * 78)
    header = f"{'档位':<18}{'种子':<7}{'状态':<7}{'成交笔数':<10}{'总成交额':<10}{'笔均份数':<9}{'金币分布(最小/中位/最大)':<26}{'守恒律'}"
    print(header)
    for r in rows:
        d = r["dist"]
        dist = f"{d[0]}/{d[2]}/{d[4]}" if d else "?"
        print(f"{r['label']:<18}{r['seed']:<7}{r['state']:<7}"
              f"{str(r['trades']):<10}{str(r['amount']):<10}{r['per_trade']:<9}"
              f"{dist:<26}{r['conserve']}")

    print()
    # 各档的状态是否在种子上**一致** —— 不一致就不能下结论。
    print("结论（各档状态是否在种子上一致）：")
    n_seeds = len(args.seeds)
    consistent = True
    for label, _ in tabs:
        states = {r["state"] for r in rows if r["label"] == label}
        mark = "一致" if len(states) == 1 else "**不一致**"
        if len(states) != 1:
            consistent = False
        print(f"  {label:<18} {mark}  {sorted(states)}")
    if n_seeds < 2:
        # ⚠️ **一个样本证明不了「一致」** —— 单档单种子时这句话是错的，
        #    而它恰恰会出现在「我只跑了一遍」的场合 ⇒ 必须显式说清楚。
        print(f"\n  ⚠️ **只跑了 {n_seeds} 个种子 ⇒ 「一致」这句话没有意义**"
              f"（一个样本无法证明一致性，也可能是噪声）。")
        print(f"     ⇒ **不能下结论**。请至少用 2–3 个种子重跑（默认 {DEFAULT_SEEDS}）。")
    elif not consistent:
        print("\n  ⇒ **有档位的状态在种子上不一致 ⇒ 不能下结论**，需要更多种子。")
    else:
        print(f"\n  ⇒ 各档状态在 {n_seeds} 个种子上**一致** ⇒ 差异不是单样本噪声。")
    print("\n  ⚠️ 「增长率 ≈ 0」**不等于**经济健康（退化的稳态也是稳态）——")
    print("     请同时看**成交笔数**与**金币分布**（中位 vs 最大）。")
    print(f"\n  候选价格表在 {tmpdir}（**没有**碰仓库里的 config/prices.json）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
