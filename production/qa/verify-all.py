"""
**一条命令验证整个仓库**。

**为什么需要它**：验证步骤散落在好几处（两个配置的构建、`dotnet test`、
11 个真机自检、参数台账校验、截图检查、10 个变异矩阵）——
散落的意思是**每次都会漏掉几个**。尤其：Godot headless 加载的是
`.godot/mono/temp/bin/Debug/` 的程序集 ⇒ **只构建 Release 时跑的是旧代码**，
而症状是「跑通了、没报错、数据却对不上」。

**本脚本做的事**（按依赖顺序）：
1. `dotnet build`（**Debug**）—— headless 自检加载的就是它
2. `dotnet build -c Release`
3. `dotnet test -c Release`
4. `production/qa/run-selfchecks.py` —— 11 个真机自检 + 「玩家存档未被碰」断言
5. `production/qa/param-inventory.py --check` —— 参数台账与代码一致
6. （可选 `--with-screenshot`）截图自检 —— **需要显示器**，无头机器上跑不了
7. （可选 `--with-mutations`）全部变异矩阵 —— 很慢（约 20 分钟），建议过夜跑

**跳过要说出来**：某一步因前提缺失被跳过时，脚本**明确打印「跳过了什么、为什么」**，
并可用 `--require-all` 把「跳过」也变成失败（CI 上用它比较诚实）。

用法：
  python production/qa/verify-all.py
  python production/qa/verify-all.py --with-screenshot
  python production/qa/verify-all.py --with-mutations
  python production/qa/verify-all.py --require-all
"""
import argparse
import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / "production" / "qa"


def godot_available():
    for c in (os.environ.get("GODOT"), r"C:\Tools\Godot\4.7.2\godot.exe"):
        if c and Path(c).exists():
            return True
    from shutil import which
    return bool(which("godot"))


def run(label, argv, cwd=ROOT):
    print(f"\n=== {label} ===")
    print("$ " + " ".join(str(a) for a in argv))
    t0 = time.time()
    proc = subprocess.run([str(a) for a in argv], cwd=str(cwd))
    dt = time.time() - t0
    ok = proc.returncode == 0
    print(f"--- {label}: {'OK' if ok else 'FAILED'}（{dt:.1f}s，退出码 {proc.returncode}）")
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--with-screenshot", action="store_true",
                    help="也跑截图自检（需要显示器）")
    ap.add_argument("--with-mutations", action="store_true",
                    help="也跑全部变异矩阵（很慢）")
    ap.add_argument("--require-all", action="store_true",
                    help="把「因前提缺失而跳过」也算失败")
    ap.add_argument("--skip-tests", action="store_true", help="跳过 dotnet test")
    args = ap.parse_args()

    results = []   # (名称, 状态)  状态 ∈ ok / fail / skip

    # 1. Debug —— headless 自检加载的就是这一份
    ok = run("构建 Debug（headless 自检加载的是这份）",
             ["dotnet", "build", "--nologo", "-v", "q"])
    results.append(("构建 Debug", "ok" if ok else "fail"))

    # 2. Release —— 测试跑的是这一份
    ok = run("构建 Release", ["dotnet", "build", "-c", "Release", "--nologo", "-v", "q"])
    results.append(("构建 Release", "ok" if ok else "fail"))

    # 3. 单元测试
    if args.skip_tests:
        results.append(("单元测试", "skip"))
        print("\n=== 单元测试 ===\n（--skip-tests，跳过）")
    else:
        ok = run("单元测试",
                 ["dotnet", "test", "tests/Claude-Code-Game-Studios.Tests.csproj",
                  "-c", "Release", "--nologo", "-v", "q"])
        results.append(("单元测试", "ok" if ok else "fail"))

    # 4. 真机自检
    if not godot_available():
        results.append(("真机自检", "skip"))
        print("\n=== 真机自检 ===\n（跳过：找不到 Godot —— 设 GODOT 环境变量）")
    else:
        ok = run("真机自检（含「玩家存档未被碰」断言）",
                 [sys.executable, QA / "run-selfchecks.py"])
        results.append(("真机自检", "ok" if ok else "fail"))

    # 5. 参数台账
    ok = run("参数台账与代码一致", [sys.executable, QA / "param-inventory.py", "--check"])
    results.append(("参数台账", "ok" if ok else "fail"))

    # 6. 截图（可选）
    if args.with_screenshot:
        ok = run("截图自检（画面不是空图）", [sys.executable, QA / "shot-check.py"])
        results.append(("截图自检", "ok" if ok else "fail"))
    else:
        results.append(("截图自检", "skip"))
        print("\n=== 截图自检 ===\n（跳过：需要 --with-screenshot，且需要显示器）")

    # 7. 变异矩阵（可选）
    if args.with_mutations:
        scripts = sorted(QA.glob("mutate-*.py"))
        for s in scripts:
            ok = run(f"变异矩阵 {s.name}", [sys.executable, s])
            results.append((f"变异 {s.name}", "ok" if ok else "fail"))
    else:
        results.append(("变异矩阵", "skip"))
        print("\n=== 变异矩阵 ===\n（跳过：需要 --with-mutations）")

    # ── 汇总 ──
    print("\n" + "=" * 60)
    print("汇总")
    print("=" * 60)
    for name, status in results:
        mark = {"ok": "✓", "fail": "✗", "skip": "⤼"}[status]
        print(f"  {mark} {name}")

    failed = [n for n, s in results if s == "fail"]
    skipped = [n for n, s in results if s == "skip"]
    print()
    if failed:
        print(f"!! {len(failed)} 项失败：" + "、".join(failed))
    if skipped:
        msg = f"⤼ {len(skipped)} 项跳过：" + "、".join(skipped)
        if args.require_all:
            print(msg + "（--require-all：跳过也算失败）")
            return 1
        print(msg)
    if not failed:
        print("✅ 全部必需项通过")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
