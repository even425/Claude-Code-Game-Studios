"""经济流水分析脚本 —— 对比多组采集样本。

用法：
    python production/qa/analyze-econ.py --new f1.jsonl f2.jsonl --old f3.jsonl

输出：
    - 副本成功率 / 掉落分布
    - 成交笔数、成交份数、成交金额、**各商品成交占比**
    - 村抽合计与**税务口径**核对（Σ村抽 + 在途余数 = 5% × 成交额，**无 floor**）
    - **货币守恒**逐日核对（不含税额余数、不含待发零头 —— 两者都不是钱包里的钱）
    - **待发零头自洽性**（每英雄 < 100（1/100 金））
    - **未成原因分布**
    - 每日经济快照轨迹（流通金 / 金库 / 升级 / 施工人手）

守恒律的口径（**流通口径**）：
    Σ英雄钱包金币 + 村库金币 + 累计销毁金币 = 初始金币 + 累计发放金币

为什么**不含税额余数**：
    税额余数是「5% 税取整后还没凑够 1 金的**记账零头**」——
    `StallFee.Compute` 里 `total = sellerNet + villageCut`，钱在买家与卖家之间
    **完整转移**，余数只进「下次累计」，**从没从任何钱包里被扣走**。
    把它加进守恒律左边 = 凭空多算那部分零头。
    余数归**税务口径**管（另一条等式），不归货币守恒管。

为什么**不含待发零头**（分数报酬 X = 0.5 金时它不为 0）：
    待发零头是「已经挣到、但**还没进任何钱包**」的**应收**——
    钱包只收整金，零头只是个计数器。
    它不在等式的**任何一边**：左边是「钱在哪」（它哪都不在），
    右边是「钱从哪来 + 已经兑现了多少」（兑现的那部分才在右边的「累计发放」里）。
    只把它加在右边 = 凭空多算那部分钱 —— 2026-10-20 的 37 人探针实测差出
    整整一个零头（3810 vs 3820），就是踩了这个坑。
    它要单列成一个**独立科目**跟着存档走（存档 0.6 的 `GoldCarry`），
    并单独核对自洽性（每英雄 < 100）。

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

    # ── 副本 ──
    settled = [r for r in rows if r.get("Kind") == "DungeonSettled"]
    won = sum(1 for r in settled if r.get("Won"))
    loot_rows = [r for r in rows if r.get("Kind") == "DungeonLooted"]
    drop_counter = Counter()
    for r in loot_rows:
        for line in r.get("Loot") or []:
            drop_counter[line.get("Item")] += 1

    # ── 交易 ──
    trades = [r for r in rows if r.get("Kind") == "TradeCompleted"]
    trade_qty = sum(t.get("Qty") or 0 for t in trades)
    trade_amount = sum(t.get("Amount") or 0 for t in trades)
    village_cut = sum(t.get("VillageCut") or 0 for t in trades)
    by_item = defaultdict(lambda: {"笔": 0, "份": 0, "金": 0})
    for t in trades:
        d = by_item[t.get("Item")]
        d["笔"] += 1
        d["份"] += t.get("Qty") or 0
        d["金"] += t.get("Amount") or 0

    # ── 未成原因 ──
    missed = [r for r in rows if r.get("Kind") == "ShoppingMissed"]
    reason_counter = Counter(r.get("Reason") for r in missed)

    # ── 每日快照 ──
    snaps = [r for r in rows if r.get("Kind") == "EconomySnapshot"]
    snaps.sort(key=lambda r: r.get("Day") or 0)

    # 货币守恒（流通口径）：Σ英雄金币 + 村库 + 已销毁 = 初始 + 累计发放
    #   ⚠️ **不含「待发零头」** —— 它是「已挣到但还没进钱包」的**应收**，
    #      钱**还没在任何钱包里**，所以它不在这条等式的任何一边。
    #      （把它只加在右边 = 凭空多算那部分钱；2026-10-20 的 37 人探针
    #        正是这样差出了整整一个零头：3810 vs 3820。）
    #   ⚠️ **不含「税额余数」** —— 余数是记账零头，不是钱（钱在买卖双方之间完整转移）。
    #   全部换算到「1/100 金」单位做整数比较，避免分数带来的浮点误差。
    conserve_ok = 0
    conserve_skip = 0     # 早期样本没有账本科目（存档 < 0.6），不参与核对
    conserve_bad = []
    for s in snaps:
        if "GoldGranted" not in s:
            conserve_skip += 1
            continue
        lhs = ((s.get("HeroGoldTotal") or 0) + (s.get("VillageGold") or 0)
               + (s.get("GoldSpent") or 0)) * 100
        rhs = ((s.get("HeroCount") or 0) * 100 + (s.get("GoldGranted") or 0)) * 100
        if lhs == rhs:
            conserve_ok += 1
        else:
            conserve_bad.append((s.get("Day"), lhs / 100.0, rhs / 100.0))

    # 待发零头的自洽性：每英雄的零头必须 < 100（1/100 金），
    # 合计必须 < 100 × 人数。越界说明「该发的没发出去」—— 那会是一笔悬空的应收。
    pending_bad = []
    for s in snaps:
        pend = s.get("GoldPending")
        if pend is None:
            continue
        if pend < 0 or pend >= 100 * (s.get("HeroCount") or 0):
            pending_bad.append((s.get("Day"), pend))

    # 税务口径：Σ村抽 + 在途余数 = 5% × 总成交额（**精确，无 floor**）
    #   推导：accumulated_i = carry_{i-1} + 5·total_i
    #         ΣvillageCut = (5·Σtotal - carry_n)/100  ⇒  ΣvillageCut + carry_n/100 = 5·Σtotal/100
    #   ⚠️ D-052 文档里写的 `floor(5% × 总成交额)` **只在乘积恰为整数时成立**（旧基线 3280×5%=164.0 恰好整数），
    #      乘积带小数时就会差 0.1 —— 新数据（702 金）把它照出来了。
    #
    # ⚠️ **窗口必须对齐快照**（2026-10-20 的 37 人探针踩过，差出整整 0.2 金）：
    #   快照是在**日界那一刻**取的 —— 副本 → 摆摊 → 升级 → 快照 →（之后）当天的自主购物。
    #   所以「标记为第 N 天的快照」里的税额余数，反映的是**到第 N-1 天为止**的成交。
    #   拿「全部成交」去配「最后一份快照的余数」，就会多算最后一天的成交 → 恒差一点。
    #   ⇒ 税务核对只用 **Day ≤ 最后快照日 − 1** 的成交，与该快照的余数配对。
    last_carry = snaps[-1].get("TaxCarry") if snaps else 0
    last_snap_day = (snaps[-1].get("Day") or 0) if snaps else 0
    tax_trades = [t for t in trades if (t.get("Day") or 0) <= last_snap_day - 1]
    tax_amount = sum(t.get("Amount") or 0 for t in tax_trades)
    tax_cut = sum(t.get("VillageCut") or 0 for t in tax_trades)
    tax_exact = tax_amount * 5 / 100.0
    tax_ok = abs((tax_cut + (last_carry or 0) / 100.0) - tax_exact) < 1e-6
    tax_floor_would_be = tax_amount * 5 // 100

    days = max((r.get("Day") or 0) for r in rows) if rows else 0
    per100 = Counter()
    for t in trades:
        per100[min(9, ((t.get("Day") or 1) - 1) // 100)] += 1

    m = {
        "label": label, "path": path, "rows": len(rows), "kinds": dict(kinds), "days": days,
        "settled": len(settled), "won": won, "loot_sessions": len(loot_rows),
        "drops": dict(drop_counter), "trades": len(trades), "qty": trade_qty,
        "amount": trade_amount, "village_cut": village_cut,
        "missed": len(missed), "reasons": dict(reason_counter),
        "by_item": {k: dict(v) for k, v in by_item.items()},
        "per100": dict(per100),
        "snaps": len(snaps), "conserve_ok": conserve_ok, "conserve_skip": conserve_skip,
        "conserve_bad": conserve_bad, "pending_bad": pending_bad,
        "tax_ok": tax_ok, "last_carry": last_carry,
        "tax_amount": tax_amount, "tax_cut": tax_cut,
        "last_snap": snaps[-1] if snaps else None,
    }

    if verbose:
        print(f"----- {label} -----")
        print(f"文件          : {path}")
        print(f"总行数        : {len(rows)}   类别 {dict(kinds)}")
        print(f"副本          : {len(settled)} 场，成功 {won}"
              + (f"（成功率 {won / len(settled) * 100:.2f}%）" if settled else ""))
        if loot_rows:
            total = sum(drop_counter.values())
            print("掉落分布      : " + "  ".join(
                f"{k} {v}({v / total * 100:.2f}%)" for k, v in drop_counter.most_common()))
        if trades:
            print(f"成交          : {len(trades)} 笔 / {trade_qty} 份 / {trade_amount} 金"
                  f"   笔均 {trade_qty / len(trades):.2f} 份"
                  f"   笔/百天 {len(trades) / days * 100:.1f}")
            print("按物品        : " + "  ".join(
                f"{k} {v['笔']}笔/{v['份']}份/{v['金']}金(占{v['金'] / trade_amount * 100:.1f}%)"
                for k, v in sorted(by_item.items(), key=lambda kv: -kv[1]["金"])))
        print(f"村抽          : {village_cut} 金（全量）   税务口径"
              f"（对齐窗口 Day≤{last_snap_day - 1}：Σ村抽 {tax_cut} + 在途余数 {(last_carry or 0) / 100:.2f}"
              f" == 5%×{tax_amount} = {tax_exact:.2f}）→ {'一致' if tax_ok else '不一致'}"
              f"   [floor 版会得 {tax_floor_would_be}，**只在该乘积恰为整数时成立**]")
        print(f"未成原因      : {len(missed)} 条"
              + ("  " + "  ".join(f"{k} {v}({v / sum(reason_counter.values()) * 100:.1f}%)"
                                 for k, v in reason_counter.most_common())
                 if reason_counter else ""))
        if snaps:
            checked = conserve_ok + len(conserve_bad)
            print(f"每日快照      : {len(snaps)} 天   守恒律成立 {conserve_ok}/{checked}"
                  + (f"（另有 {conserve_skip} 天无账本科目，跳过）" if conserve_skip else "")
                  + ("" if not conserve_bad
                     else f"   不一致 前3天差异 {conserve_bad[:3]}"))
            if pending_bad:
                print(f"待发零头自洽  : 不一致（{len(pending_bad)} 天越界），前3个 {pending_bad[:3]}")
            else:
                print("待发零头自洽  : 一致（每英雄 < 100，合计 < 100x人数）")
            s = m["last_snap"]
            print(f"末次快照      : 流通金 {s.get('HeroGoldTotal')} / 金库 {s.get('VillageGold')}"
                  f" / 材料 {s.get('VillageMaterial')} / 累计发放 {s.get('GoldGranted')}"
                  f" / 累计销毁 {s.get('GoldSpent')} / 升级 {s.get('Upgrades')}"
                  f" / 待发零头 {(s.get('GoldPending') or 0) / 100:.2f} 金（应收，不在任何钱包里）"
                  f" / 在途余数 {s.get('TaxCarry')} / 英雄 {s.get('HeroCount')}"
                  f" / 在工地 {s.get('Builders')}")
        print("每100天成交   : " + "  ".join(
            f"{k * 100 + 1}-{k * 100 + 100}:{v}" for k, v in sorted(per100.items())))
        print()
    return m


def group_summary(name, samples):
    print(f"===== 组「{name}」（{len(samples)} 份样本）=====")
    print(f"{'指标':<16}{'最小':>12}{'最大':>12}{'均值':>12}")
    for key in ["rows", "settled", "won", "loot_sessions", "trades", "qty",
                "amount", "village_cut", "missed", "snaps", "conserve_ok",
                "conserve_skip"]:
        vals = [s[key] for s in samples]
        print(f"{key:<16}{min(vals):>12}{max(vals):>12}{sum(vals) / len(vals):>12.1f}")
    rates = [s["won"] / s["settled"] * 100 for s in samples if s["settled"]]
    if rates:
        print(f"{'副本成功率%':<16}{min(rates):>12.2f}{max(rates):>12.2f}"
              f"{sum(rates) / len(rates):>12.2f}")
    perday = [s["trades"] / s["days"] * 100 for s in samples if s["days"]]
    if perday:
        print(f"{'笔/百天':<16}{min(perday):>12.1f}{max(perday):>12.1f}"
              f"{sum(perday) / len(perday):>12.1f}")
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
        if args[i].startswith("--"):
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
