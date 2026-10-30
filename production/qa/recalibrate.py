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
    0. **同步内置兜底价格表**（`config/prices.json` → `PriceBook.Default`）——
       ⚠️ 这一步是**必须**的，不是锦上添花：价格表是同一份数据的**两个副本**，
       守卫 `PriceBookConfigConsistencyTests` 要求两者严格相等。
       开发者改了 JSON 却没同步兜底 ⇒ 守卫红 ⇒ **第 2 步的 `dotnet test` 当场抛错退出，
       采集根本跑不起来** —— 也就是说，「改一个文件 + 跑一条命令」这条承诺
       **恰好在最需要它的地方断了**。同步掉这一步，承诺才是真的。
       （用 `--prices` 指定别的价格表时**不同步** —— 那是一次性的实验覆盖，
       不该把提交在仓库里的兜底表改掉。）
    1. 读 config/prices.json（读不到就用内置占位表）并打印 —— **先确认价格表是哪个**
    2. 构建 Debug + Release，跑全部单元测试（两个都要：Godot headless 加载的是 Debug）
    3. 按给定的 X 列表逐个采集（37 英雄 / --daylen=0.15 / 指定种子）
    4. 对每个样本跑 analyze-econ.py，把「流通金增长率 / 中位英雄 / 最大占比 / 成交」汇总成表

**它不替你判断** —— 「哪个 X 是固定点」要看这张表（增长率变号的那一档），
「分布要不要加约束」是开发者的决定。脚本只负责把数**量出来**。
"""

try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

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
SYNC = QA / "sync-price-fallback.py"
OUT = Path(os.environ.get("TEMP", ".")) / "ccgs-recal"


def sync_fallback() -> None:
    """把内置兜底价格表同步成 `config/prices.json` 的内容。

    **为什么必须在构建测试之前做**：价格表是同一份数据的两个副本，
    守卫要求两者严格相等 ⇒ 开发者改了 JSON 忘了同步 ⇒ 守卫红 ⇒
    下面的 `dotnet test` 当场抛错退出 ⇒ **采集根本跑不起来**。
    """
    print(">> 同步内置兜底价格表（config/prices.json → PriceBook.Default）", flush=True)
    proc = subprocess.run([sys.executable, str(SYNC)], cwd=ROOT)
    if proc.returncode != 0:
        raise SystemExit(
            "!! 同步内置兜底失败 —— 上面那一行是原因。"
            "（价格表必须两份一致，否则副本一致性守卫会红。）")


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


def collect(label, days, seed, goldreward, prices_path, upkeep: int = -1) -> Path:
    OUT.mkdir(parents=True, exist_ok=True)
    log = OUT / f"{label}.jsonl"
    if log.exists():
        log.unlink()
    args = [
        GODOT, "--headless", "--path", str(PROJECT), "--quit-after", "8000000", "--",
        "--daylen=0.15", f"--collect={days}", f"--seed={seed}",
        f"--goldreward={goldreward}", f"--econlog={log}",
        # ⚠️ **upkeep < 0 ⇒ 不传这个参数** ⇒ 用游戏里的默认值（D-126：8 金/天）。
        f"--savepath={OUT / 'run-save.json'}",
    ]
    if upkeep >= 0:
        args.append(f"--upkeep={upkeep}")
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
        elif "①分布健康" in line:
            res["health"] = line.split(":", 1)[1].strip() if ":" in line else line
        elif "②村庄花得出去" in line:
            res["unspent"] = line.split(":", 1)[1].strip() if ":" in line else line
        elif "③市场还在转" in line:
            res["market"] = line.split(":", 1)[1].strip() if ":" in line else line
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
    ap.add_argument("--upkeep", type=int, default=-1, help="村庄每天的日常开销（金，D-126；默认 -1 = 用游戏默认值 8）")
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

    # 用 --prices 指定了别的价格表 ⇒ 那是一次性的实验覆盖，
    # **不能**把提交在仓库里的内置兜底改掉（否则仓库状态被实验污染）。
    if args.prices:
        print(">> 跳过同步内置兜底（--prices 是一次性覆盖，不该改仓库里的兜底表）", flush=True)
    else:
        sync_fallback()

    build_and_test(args.skip_build)

    prices_path = args.prices or (str(PRICES) if PRICES.exists() else "")
    rows = []
    for x in args.x:
        for seed in args.seeds:
            label = f"rc-x{str(x).replace('.', 'p')}-s{seed}-{args.days}d"
            log = collect(label, args.days, seed, x, prices_path, args.upkeep)
            info = analyze(log)
            rows.append((x, seed, info))
            print(f"   → X={x} seed={seed}: {info.get('health', '(无)')}", flush=True)

    print()
    print("=" * 78)
    print("汇总（X / 种子 / 分布健康 / 村庄花得出去 / 末次分布 / 成交 / 守恒律）")
    print("=" * 78)
    for x, seed, info in rows:
        print(f"X={x:<6} seed={seed:<6} {info.get('health', '?')}")
        print(f"        {info.get('unspent', '?')}")
        print(f"        {info.get('market', '?')}")
        print(f"        {info.get('dist', '?')}")
        print(f"        {info.get('trades', '?')} | {info.get('conserve', '?')}")
    print()
    print("**判据（D-109 拍板 + D-111 补第三条）**：")
    print("① **分布健康**（最富的人 ≤ 人均的 4 倍）")
    print("＋② **村庄花得出去**（可花未花的钱不一路囤）")
    print("＋③ **市场还在转**（后半段仍有成交 —— D-111：没有这一条，前两条会绿得没有意义）")
    print("**「流通金增长率」只作观察，不再是判据** —— D-106 已证明 X 是弱旋钮"
          "（每 +0.01 X ≈ +0.2 金/天，而钱每天涨 3~11 金）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
