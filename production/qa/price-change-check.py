"""
「改价格 ⇒ 自检仍然全绿」—— 把这句话从**承诺**变成**机器可核对的事实**。

**它守的是什么**：价格是 `07` §2 的临时参数（**P-14**，占位值 5 / 3 / 8），
而开发者随时会改 `config/prices.json`。

🔴 **真事（2026-10-21）**：真机自检里**把石料单价写死成 5**
（`--trade` 期望买家付 10、`--market` 期望买家付 25 / 卖家净得 24、
`--dungeon` 期望买家付 5）。
⇒ 开发者一改价格，那几个自检**立刻红** ⇒ CI 跟着红
⇒ 而「**改价格 → 重跑标定**」这条承诺**恰恰要在改价之后才用得上**
—— 承诺在最需要它的地方断了（与 14 条单元测试栽过的同一类坑：
「占位价被当成事实」）。

**它怎么验**：用**另一张价格表**（临时文件：石料 7 / 木料 4 / 布匹 9）跑那几个自检。
`Main.LoadPriceBook` 的查找顺序 ① 就是 `--prices=<路径>`
⇒ **不用改仓库里的任何文件**（不碰 `config/prices.json`，也不碰内置兜底）。
⇒ 那几个自检全绿 = 「自检不依赖占位价」这条承诺成立。

**为什么值得单独做成工具**：这个承诺**没法用单元测试验**（自检只在**真实游戏**里跑，
`dotnet test` 够不到它）⇒ 只能端到端跑一次真机。
⇒ 而它恰恰是「开发者答完 P-14 之后**第一件**要做的事」的前置条件。

用法：python production/qa/price-change-check.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SELFCHECKS = ROOT / "production" / "qa" / "run-selfchecks.py"
PROJECT = ROOT / "Claude-Code-Game-Studios.csproj"

# 与价格有关的那几个自检（摆摊 / 面板 / 买卖 / 副本战利品开摊）
PRICE_SENSITIVE = "market,shop,trade,dungeon"

# 另一张价格表：故意与占位值（5 / 3 / 8）**都不同** ⇒ 占位价一变就露馅
ALT_PRICES = {"石料": 7, "木料": 4, "布匹": 9}


def write_alt_prices(path: Path) -> None:
    data = {
        "_说明": "QA 用的另一张价格表 —— 单价与占位值不同，"
                 "用来证明真机自检不依赖占位价。",
        "_来源": "P-014 待定（本文件仅供 QA 验证，不进版本库）",
    }
    data.update(ALT_PRICES)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    # ⚠️ **先构建**：自检跑的是**编译产物**（Godot headless 加载
    # `.godot/mono/temp/bin/Debug/`）⇒ 不构建就等于验**旧代码** ⇒ 假绿。
    print("构建 Debug（自检加载的是这份）…")
    build = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"], cwd=ROOT,
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
    if build.returncode != 0:
        print("!! 构建失败 —— 先把编译弄绿再谈这条承诺。")
        for line in (build.stdout + build.stderr).splitlines()[-12:]:
            print("   ", line)
        return 1

    tmp = Path(tempfile.mkdtemp(prefix="price-change-"))
    alt = tmp / "alt-prices.json"
    write_alt_prices(alt)

    print(f"替代价格表: {alt}")
    print(f"  石料 {ALT_PRICES['石料']} / 木料 {ALT_PRICES['木料']} / 布匹 {ALT_PRICES['布匹']}"
          f"（占位值是 5 / 3 / 8）")
    print()

    argv = [sys.executable, str(SELFCHECKS),
            f"--only={PRICE_SENSITIVE}", f"--prices={alt}"]
    proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    out = proc.stdout + proc.stderr

    # 失败原因要**看得见**（自检脚本自己会打；这里再打一遍摘要）
    tail = [ln for ln in out.splitlines() if ln.strip()][-12:]
    for ln in tail:
        print("  " + ln)

    print()
    if proc.returncode != 0:
        print("!! 换一张价格表之后，自检**没有全绿**")
        print("   ⇒ 自检把占位价当成了事实 ⇒ 开发者一改价格，CI 就会红。")
        print("   ⇒ 修法：期望值与夹具金额一律从**生效价格表**算出来"
              "（`_priceBook.PriceOf(item)`）。")
        return 1

    print(f"OK  换一张价格表（{ALT_PRICES}）之后，{PRICE_SENSITIVE} 四个自检仍然全绿"
          " ⇒ 自检不依赖占位价。")
    print("   ⇒ 「改价格 → 重跑标定」这条承诺现在**有机器背书**。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
