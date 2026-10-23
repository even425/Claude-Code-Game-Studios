"""
机制覆盖探测器 —— 「哪条机制在真实游戏里从没留下痕迹」。

**它要解决什么**：D-101 那个 bug（阶段机死锁 ⇒ 大事件**从未结算**）之所以能潜伏这么久，
是因为**没有任何一条检查问过「这条机制在真实游戏里到底跑过没有」**：
单测直接调内部方法 ⇒ 跑的是「我直接驱动的那一段」；
自检也是手工推进 ⇒ 同样绕开；标定里「没事件」被归因为**合理的**过滤器 ⇒ 没人去查。

⇒ 本脚本跑一次**真实游戏**（headless 采集模式，推进 N 天），
然后**逐条机制**核对「它有没有在流水里留下痕迹」。

用法：
    python production/qa/feature-coverage.py [--days 60] [--x 0.06] [--seed 1]

退出码：0 = 全部达标；1 = 有机制没留下痕迹（**那是待查的线索**，不是失败的世界）。
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import argparse
import collections
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT = r"C:\Tools\Godot\4.7.2\godot.exe"

# 每条机制 = (名字, 流水里的行类别, 说明, 允许为 0 吗)
# ⚠️ **判据是「流水里有没有这一类行」** ⇒ 机制必须**留下痕迹**才算跑过。
# ⚠️ **「允许为 0」的那些是有理由的**（例如商队成交是**玩家操作** ⇒ 采集器里天然为 0）
# ⇒ 它们**不报**，但**会打印出来**（让人看见「它确实没跑」这件事本身）。
MECHANISMS = [
    ("副本战斗", "DungeonSettled", "派队 → 打副本 → 结算", False),
    ("副本掉落", "DungeonLooted", "战利品进背包（D-092 后进村库材料池）", False),
    ("英雄摆摊 / 互相买卖", "TradeCompleted", "市场成交（村抽 5% 进村库）", False),
    ("村庄收购站", "VillagePurchase", "村库按市价收走在村英雄的采集品（D-087）", False),
    ("大事件结算", "WorldEventSettled", "兽潮 / 商队 / 盗匪来袭**真的结算了**（D-101）", False),
    ("每日经济快照", "EconomySnapshot", "守恒律逐日核对的数据源", False),
    # ↓ 这两条**天然为 0**，但**理由必须写在这里**（否则「没跑过」与「按设计不跑」分不清）。
    ("商队成交", "CaravanTrade",
     "商队摊位的真成交（D-088）—— ⚠️ **按设计为 0**：买商队的货是**玩家操作**，采集器不代劳 ⇒ 靠 `--caravan` 真机自检覆盖", True),
    ("想买未成", "ShoppingMissed",
     "钱不够 / 背包满（D-054 的未成原因分布）—— ⚠️ **可能为 0**：样本少时一次未成都没有（40 天 0 次、200 天 2 次）", True),
]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=60)
    ap.add_argument("--x", type=float, default=0.06)
    ap.add_argument("--seed", type=int, default=1)
    args = ap.parse_args()

    out = Path(tempfile.gettempdir()) / f"ccgs-cov-{os.getpid()}.jsonl"
    if out.exists():
        out.unlink()

    cmd = [
        GODOT, "--headless", "--path", str(ROOT), "--quit-after", "8000000", "--",
        # ⚠️ **--daylen=0.15**：与 recalibrate.py 同一套参数 ⇒ 跑得快，
        # 且**测的是同一个世界**（日长不同 ⇒ 采集节奏不同 ⇒ 数据不可比）。
        "--daylen=0.15",
        f"--collect={args.days}",
        f"--econlog={out}",
        f"--goldreward={args.x}",
        f"--seed={args.seed}",
    ]
    print(f">> 跑一次真实游戏：{args.days} 天 · X={args.x} · seed={args.seed}")
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=1800)
    if not out.exists():
        print("!! 没有产出流水 —— 游戏没跑起来：")
        print((proc.stdout or "")[-2000:])
        print((proc.stderr or "")[-2000:])
        return 1

    kinds = collections.Counter()
    for line in out.open(encoding="utf-8"):
        try:
            o = json.loads(line)
        except Exception:
            continue
        kinds[o.get("Kind", "")] += 1

    out.unlink(missing_ok=True)

    print(f"\n机制覆盖（{args.days} 天真实游戏）：")
    missing = []
    expected_zero = []
    for name, kind, note, may_be_zero in MECHANISMS:
        n = kinds.get(kind, 0)
        if n > 0:
            flag = "OK "
        elif may_be_zero:
            flag = "~ "
            expected_zero.append((name, kind, note))
        else:
            flag = "!! "
            missing.append((name, kind, note))
        print(f"  {flag}{name:16} {kind:20} × {n:<6} {note}")

    print("\n流水里出现过的行类别：")
    for k, v in sorted(kinds.items()):
        print(f"    {k:22} {v}")

    if expected_zero:
        print(f"\n（{len(expected_zero)} 条**按设计为 0** —— 理由见各条后面的说明，不是异常）")
        for name, kind, note in expected_zero:
            print(f"   ~ {name}（{kind}）：{note}")

    if missing:
        print(f"\n!! 有 {len(missing)} 条机制**在真实游戏里没留下任何痕迹**：")
        for name, kind, note in missing:
            print(f"   - {name}（{kind}）：{note}")
        print("   ⇒ 每一条都是**待查的线索**（像 D-101 那样：规则在、接线也在，就是没人跑过）。")
        return 1

    print("\n✅ 全部机制在真实游戏里都留下了痕迹。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
