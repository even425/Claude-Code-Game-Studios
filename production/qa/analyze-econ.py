"""经济流水分析脚本 —— 对比多组采集样本。

用法：
    python production/qa/analyze-econ.py --new f1.jsonl f2.jsonl --old f3.jsonl

输出：
    - 每份样本的明细（副本成功率 / 掉落分布 / 成交 / 村抽守恒 / **未成原因分布**）
    - 每组样本的**区间**（min/max/均值）

为什么要区间而不是单值：
    采集用**全局随机流**，其种子每次运行都不同（`Main.BootSeed = Environment.TickCount * …`），
    所以**同一份代码两次跑出的数据也不同**。单次采集的「成交笔数」差几十个百分点是
    **噪声**，不是机制差异 —— 报告里必须给区间，否则会把噪声写成结论。

脚本只读，不修改任何数据文件。
"""

import json
import sys
from collections import Counter, defaultdict


def load(path):
    rows = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                pass   # 半行跳过
    return rows


def analyze(path, label, verbose=True):
    rows = load(path)
    kinds = Counter(r.get("Kind") for r in rows)

    settled = [r for r in rows if r.get("Kind") == "DungeonSettled"]
    won = sum(1 for r in settled if r.get("Won"))
    loot_rows = [r for r in rows if r.get("Kind") == "DungeonLooted"]

    drop_counter = Counter()
    for r in loot_rows:
        for line in r.get("Loot") or []:
            drop_counter[line.get("Item")] += 1

    trades = [r for r in rows if r.get("Kind") == "TradeCompleted"]
    trade_qty = sum(t.get("Qty") or 0 for t in trades)
    trade_amount = sum(t.get("Amount") or 0 for t in trades)
    village_cut = sum(t.get("VillageCut") or 0 for t in trades)
    qty_counter = Counter(t.get("Qty") for t in trades)
    by_item = defaultdict(lambda: {"笔": 0, "份": 0, "金": 0})
    for t in trades:
        d = by_item[t.get("Item")]
        d["笔"] += 1
        d["份"] += t.get("Qty") or 0
        d["金"] += t.get("Amount") or 0

    missed = [r for r in rows if r.get("Kind") == "ShoppingMissed"]
    reason_counter = Counter(r.get("Reason") for r in missed)

    days = max((r.get("Day") or 0) for r in rows) if rows else 0
    per100 = Counter()
    for t in trades:
        per100[((t.get("Day") or 0) - 1) // 100] += 1

    m = {
        "label": label, "path": path, "rows": len(rows), "kinds": dict(kinds), "days": days,
        "settled": len(settled), "won": won, "loot_sessions": len(loot_rows),
        "drops": dict(drop_counter), "trades": len(trades), "qty": trade_qty,
        "amount": trade_amount, "village_cut": village_cut,
        "qty_dist": dict(qty_counter), "missed": len(missed),
        "reasons": dict(reason_counter), "by_item": {k: dict(v) for k, v in by_item.items()},
        "per100": dict(per100),
        "carry_ok": (trade_amount * 5 // 100 == village_cut),
    }

    if verbose:
        print(f"----- {label} -----")
        print(f"文件          : {path}")
        print(f"总行数        : {len(rows)}   类别 {dict(kinds)}")
        print(f"副本          : {len(settled)} 场，成功 {won}  "
              f"（成功率 {won / len(settled) * 100:.2f}%）" if settled else "副本: 无")
        if loot_rows:
            total = sum(drop_counter.values())
            print("掉落分布      : " + "  ".join(
                f"{k} {v}（{v / total * 100:.2f}%）" for k, v in drop_counter.most_common()))
        print(f"成交          : {len(trades)} 笔 / {trade_qty} 份 / {trade_amount} 金"
              f"   笔均 {trade_qty / len(trades) if trades else 0:.2f} 份"
              f"   笔/百天 {len(trades) / days * 100:.1f}" if trades else "成交: 无")
        print(f"单笔份数分布  : {dict(sorted(qty_counter.items()))}")
        print(f"村抽          : {village_cut} 金   守恒 floor(5%×{trade_amount})="
              f"{trade_amount * 5 // 100} → {'一致' if m['carry_ok'] else '不一致！'}")
        if by_item:
            print("按物品        : " + "  ".join(
                f"{k} {v['笔']}笔/{v['份']}份/{v['金']}金(占{v['金'] / trade_amount * 100:.1f}%)"
                for k, v in sorted(by_item.items(), key=lambda kv: -kv[1]["金"])))
        print(f"未成行数      : {len(missed)}")
        if reason_counter:
            tot = sum(reason_counter.values())
            print("未成原因分布  : " + "  ".join(
                f"{k} {v}({v / tot * 100:.1f}%)" for k, v in reason_counter.most_common()))
        print("每100天成交   : " + "  ".join(
            f"{k * 100 + 1}-{k * 100 + 100}:{v}" for k, v in sorted(per100.items())))
        print()
    return m


def group_summary(name, samples):
    def rng(key):
        vals = [s[key] for s in samples]
        return min(vals), max(vals), sum(vals) / len(vals)

    print(f"===== 组「{name}」（{len(samples)} 份样本）=====")
    print(f"{'指标':<16}{'最小':>12}{'最大':>12}{'均值':>12}")
    for key, fmt in [
        ("rows", "{:>12}"), ("settled", "{:>12}"), ("won", "{:>12}"),
        ("loot_sessions", "{:>12}"), ("trades", "{:>12}"), ("qty", "{:>12}"),
        ("amount", "{:>12}"), ("village_cut", "{:>12}"), ("missed", "{:>12}"),
    ]:
        lo, hi, avg = rng(key)
        print(f"{key:<16}" + fmt.format(lo) + fmt.format(hi) + fmt.format(f"{avg:.1f}"))
    rates = [s["won"] / s["settled"] * 100 for s in samples if s["settled"]]
    if rates:
        print(f"{'副本成功率%':<16}{min(rates):>12.2f}{max(rates):>12.2f}{sum(rates) / len(rates):>12.2f}")
    perday = [s["trades"] / s["days"] * 100 for s in samples if s["days"]]
    if perday:
        print(f"{'笔/百天':<16}{min(perday):>12.1f}{max(perday):>12.1f}{sum(perday) / len(perday):>12.1f}")
    # 未成原因：按组汇总（样本内占比再平均）
    agg = Counter()
    for s in samples:
        agg.update(s["reasons"])
    if agg:
        tot = sum(agg.values())
        print("未成原因(合并): " + "  ".join(
            f"{k} {v}({v / tot * 100:.1f}%)" for k, v in agg.most_common()))
    print()


def main():
    args = sys.argv[1:]
    groups = {}
    i = 0
    while i < len(args):
        if args[i] == "--new" or args[i] == "--old":
            name = args[i][2:]
            files = []
            i += 1
            while i < len(args) and not args[i].startswith("--"):
                files.append(args[i])
                i += 1
            groups[name] = files
        else:
            print(__doc__)
            sys.exit(1)

    for name, files in groups.items():
        samples = [analyze(f, f"{name}#{n + 1}") for n, f in enumerate(files)]
        group_summary(name, samples)


if __name__ == "__main__":
    main()
