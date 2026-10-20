#!/usr/bin/env python3
"""R-05 货币标定重跑器 —— 「开发者定价格 → 重跑标定」一条命令。

背景（D-054 阶段 7）：
    价格表是【待定 T-007】，而**货币侧的全部标定**（X 的固定点、稳态、分布）
    都随价格变 —— 货物地板 = 5 份 x 价格 x 种类，X 的固定点也随价格移动。
    所以价格一定，标定就要重跑。价格表已外部化到 ``config/prices.json``，
    这个脚本把「重跑」这件事变成一条命令。

用法：
    python production/qa/recalibrate.py                     # 默认扫一遍 X（1000 天）
    python production/qa/recalibrate.py --days 2000 --seeds 4242 7777
    python production/qa/recalibrate.py --x 0.1 0.15 0.2     # 指定要扫的 X
    python production/qa/recalibrate.py --skip-build        # 跳过构建与测试（只采集）

它做什么：
    1. 读 config/prices.json（读不到就用内置占位表）并打印 —— **先确认价格表是哪个**
    2. 构建 Debug + Release，跑全部单元测试（两个都要：Godot headless 加载的是 Debug）
    3. 按给定的 X 列表逐个采集（37 英雄 / --daylen=0.15 / 指定种子）
    4. 对每个样本跑 analyze-econ.py，把「流通金增长率 / 中位英雄 / 最大占比 / 成交」汇总成表

**它不替你判断** —— 「哪个 X 是固定点」要看这张表（增长率变号的那一档），
「分布要不要加约束」是开发者的决定。脚本只负责把数**量出来**。
"""

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT = r"C:\Tools\Godot\4.7.2\godot.exe"
PROJECT = ROOT
PRICES = ROOT / "config" / "prices.json"
QA = ROOT / "production" / "qa"
ANALYZE = QA / "analyze-econ.py"
OUT = Path(os.environ.get("TEMP", ".")) / "ccgs-recal"


def read_prices() -> dict:
    """读当前价格表（读不到就返回空 dict = 用内置占位表）。"""
    try:
        return json.loads(PRICES.read_text(encoding="utf-8"))
    except Exception:
        return {}


def price_summary() -> str:
    raw = read_prices()
    items = {k: v for k, v in raw.items() if not k.startswith("_") and isinstance(v, int)}
    if not items:
        return "(内置占位表 5/3/8 —— config/prices.json 读不到)"
    return " ".join(f"{k}={v}" for k, v in sorted(items.items()))


def run(cmd, **kw):
    print("$ " + " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run(cmd, check=True, **kw)


def build_and_test(skip: bool) -> None:
    if skip:
        print(">> 跳过构建与测试", flush=True)
        return
    print(">> 构建 Debug + Release（Godot headless 加载的是 Debug）", flush=True)
    for cfg in ("Debug", "Release"):
        run(["dotnet", "build", str(PROJECT / "Claude-Code-Game-Studios.csproj"),
             "-c", cfg, "-v", "q", "--nologo"], cwd=ROOT)
    print(">> 单元测试", flush=True)
    run(["dotnet", "test", str(PROJECT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"),
         "-c", "Release", "--nologo", "-v", "q"], cwd=ROOT)


def collect(label, days, seed, goldreward, prices_path) -> Path:
    OUT.mkdir(parents=True, exist_ok=True)
    log = OUT / f"{label}.jsonl"
    if log.exists():
        log.unlink()
    args = [
        GODOT, "--headless", "--path", str(PROJECT), "--quit-after", "8000000", "--",
        "--daylen=0.15", f"--collect={days}", f"--seed={seed}",
        f"--goldreward={goldreward}", f"--econlog={log}",
        f"--savepath={OUT / 'run-save.json'}",
    ]
    if prices_path:
        args.append(f"--prices={prices_path}")
    print(f">> 采集 {label}: {days} 天 · seed {seed} · X {goldreward}", flush=True)
    t0 = time.time()
    # Godot 是 GUI 子系统程序：subprocess 的 wait() 会真等（PowerShell 的 & 不会）
    subprocess.run(args, check=False, cwd=ROOT,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f"   用时 {time.time() - t0:.0f}s，流水 {sum(1 for _ in log.open(encoding='utf-8'))} 行"
          if log.exists() else "   **没有产出流水**", flush=True)
    return log


def analyze(log: Path) -> dict:
    """跑 analyze-econ.py 并抓出关键指标（不解析全部输出，只取需要的行）。"""
    if not log.exists():
        return {}
    out = subprocess.run([sys.executable, str(ANALYZE), "--x", str(log)],
                         capture_output=True, text=True, encoding="utf-8", cwd=ROOT)
    text = out.stdout or ""
    res = {}
    for line in text.splitlines():
        if "流通金增长率" in line:
            res["growth"] = line.split(":", 1)[1].strip() if ":" in line else line
        elif "金币分布(末次)" in line:
            res["dist"] = line.split(":", 1)[1].strip() if ":" in line else line
        elif line.strip().startswith("成交 "):
            res["trades"] = line.split(":", 1)[1].strip() if ":" in line else line
        elif "守恒律成立" in line:
            res["conserve"] = line.split(":", 1)[1].strip() if ":" in line else line
    return res


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=1000)
    ap.add_argument("--seeds", type=int, nargs="+", default=[4242])
    ap.add_argument("--x", type=float, nargs="+", default=[0.05, 0.1, 0.2, 0.4, 0.7, 1.0])
    ap.add_argument("--skip-build", action="store_true")
    ap.add_argument("--prices", default="", help="价格表路径（默认用 config/prices.json）")
    args = ap.parse_args()

    print("=" * 78)
    print("R-05 货币标定重跑")
    print("=" * 78)
    print(f"价格表          : {PRICES if not args.prices else args.prices}")
    print(f"生效价格        : {price_summary()}")
    print(f"规模            : 37 英雄（采集模式默认）")
    print(f"采集            : {args.days} 天 · 种子 {args.seeds}")
    print(f"要扫的 X        : {args.x}")
    print()

    build_and_test(args.skip_build)

    prices_path = args.prices or (str(PRICES) if PRICES.exists() else "")
    rows = []
    for x in args.x:
        for seed in args.seeds:
            label = f"rc-x{str(x).replace('.', 'p')}-s{seed}-{args.days}d"
            log = collect(label, args.days, seed, x, prices_path)
            info = analyze(log)
            rows.append((x, seed, info))
            print(f"   → X={x} seed={seed}: {info.get('growth', '(无)')}", flush=True)

    print()
    print("=" * 78)
    print("汇总（X / 种子 / 流通金增长率 / 末次分布 / 成交 / 守恒律）")
    print("=" * 78)
    for x, seed, info in rows:
        print(f"X={x:<6} seed={seed:<6} {info.get('growth', '?')}")
        print(f"        {info.get('dist', '?')}")
        print(f"        {info.get('trades', '?')} | {info.get('conserve', '?')}")
    print()
    print("**「哪个 X 是固定点」看增长率变号的那一档**；")
    print("**「分布要不要加约束」是开发者的决定** —— 脚本只负责把数量出来。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
