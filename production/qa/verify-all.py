"""
**一条命令验证整个仓库**。

**为什么需要它**：验证步骤散落在好几处（两个配置的构建、`dotnet test`、
7 个真机自检、参数台账校验、截图检查、全部变异矩阵）——
散落的意思是**每次都会漏掉几个**。尤其：Godot headless 加载的是
`.godot/mono/temp/bin/Debug/` 的程序集 ⇒ **只构建 Release 时跑的是旧代码**，
而症状是「跑通了、没报错、数据却对不上」。

**本脚本做的事**（按依赖顺序）：
1. `dotnet build`（**Debug**）—— headless 自检加载的就是它
2. `dotnet build -c Release`
3. `dotnet test -c Release`
4. `production/qa/run-selfchecks.py` —— 7 个真机自检 + 「玩家存档未被碰」断言
5. **领域层脱离 Godot 编译**（不变量 I9）—— 这道门**故意**在另一个工程里：
   测试工程引用整个游戏（含 `GodotSharp`），在那儿跑测试**证明不了**
   「领域层脱离 Godot 也能编译」；只有一个**不引用** Godot 的工程能证明。
   领域层里多一行 `using Godot;` ⇒ 这里编译失败 ⇒ CI 红。
6. `production/qa/param-inventory.py --check` —— 参数台账与代码一致
7. （可选 `--with-screenshot`）截图自检 —— **需要显示器**，无头机器上跑不了
8. （可选 `--with-mutations`）全部变异矩阵 —— 很慢，建议过夜跑；
   **或 `--mutations <子串>` 只跑其中几个**（如 `--mutations settlement`）——
   ⚠️ **为什么要有后者**：全量 19 个脚本要几小时 ⇒ **实际上没人会跑**
   ⇒ 纪律等于靠自觉。改了一条守卫，就只验它对应的那一个（几分钟）。

**跳过要说出来**：某一步因前提缺失被跳过时，脚本**明确打印「跳过了什么、为什么」**，
并可用 `--require-all` 把「跳过」也变成失败（CI 上用它比较诚实）。

用法：
  python production/qa/verify-all.py
  python production/qa/verify-all.py --with-screenshot
  python production/qa/verify-all.py --with-mutations
  python production/qa/verify-all.py --mutations settlement   # 只跑相关的变异
  python production/qa/verify-all.py --require-all
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

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
                    help="也跑变异矩阵（很慢）")
    ap.add_argument("--mutations", default="",
                    help="只跑文件名含这个子串的变异脚本（如 settlement）。"
                         "**它自己就会打开变异矩阵**，不必再写 --with-mutations。"
                         "⚠️ 为什么要它：全量 19 个脚本要几小时 ⇒ 实际上没人会跑"
                         "⇒ 纪律等于靠自觉。改了一条守卫，就只验它对应的那一个")
    ap.add_argument("--require-all", action="store_true",
                    help="把「因前提缺失而跳过」也算失败")
    ap.add_argument("--skip-tests", action="store_true", help="跳过 dotnet test")
    ap.add_argument("--skip-build", action="store_true",
                    help="跳过两个 dotnet build（**只在你刚构建过时用**）")
    ap.add_argument("--skip-checks", action="store_true",
                    help="跳过真机自检 / I9 / 参数台账（**只在你刚跑过它们时用**）")
    args = ap.parse_args()

    with_mutations = args.with_mutations or bool(args.mutations)

    results = []   # (名称, 状态)  状态 ∈ ok / fail / skip

    # 1. Debug —— headless 自检加载的就是这一份
    if args.skip_build:
        results.append(("构建 Debug", "skip"))
        print("\n=== 构建 Debug ===\n（--skip-build，跳过）")
    else:
        ok = run("构建 Debug（headless 自检加载的是这份）",
                 ["dotnet", "build", "--nologo", "-v", "q"])
        results.append(("构建 Debug", "ok" if ok else "fail"))

    # 2. Release —— 测试跑的是这一份
    if args.skip_build:
        results.append(("构建 Release", "skip"))
        print("\n=== 构建 Release ===\n（--skip-build，跳过）")
    else:
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
    if args.skip_checks:
        results.append(("真机自检", "skip"))
        print("\n=== 真机自检 ===\n（--skip-checks，跳过）")
    elif not godot_available():
        results.append(("真机自检", "skip"))
        print("\n=== 真机自检 ===\n（跳过：找不到 Godot —— 设 GODOT 环境变量）")
    else:
        ok = run("真机自检（含「玩家存档未被碰」断言）",
                 [sys.executable, QA / "run-selfchecks.py"])
        results.append(("真机自检", "ok" if ok else "fail"))

    # 5. 规则层脱离 Godot 编译**并运行**（不变量 I9）——
    #    这道门**故意**在另一个工程里：测试工程引用整个游戏（含 GodotSharp），
    #    在那儿跑测试**证明不了**「领域层脱离 Godot 也能编译运行」。
    #    领域层里多一行 `using Godot;` ⇒ 那个工程编译失败；它内部还有一条**运行时**
    #    断言（查产物的程序集引用里有没有 Godot）⇒ 反射、延迟加载也躲不过。
    #    ⚠️ 那个工程**必须零依赖** —— gdUnit4 会把 GodotSharp 传递带进来，
    #    那道门就形同虚设（详见该 csproj 的注释）。
    if args.skip_checks:
        results.append(("规则层脱离 Godot（I9）", "skip"))
        print("\n=== 规则层脱离 Godot（I9） ===\n（--skip-checks，跳过）")
    else:
        ok = run("规则层脱离 Godot 编译并运行（I9）",
                 ["dotnet", "run", "--project", "tests/DomainPurity/DomainPurity.csproj",
                  "--nologo", "-v", "q"])
        results.append(("规则层脱离 Godot（I9）", "ok" if ok else "fail"))

    # 6. 参数台账
    if args.skip_checks:
        results.append(("参数台账", "skip"))
        print("\n=== 参数台账 ===\n（--skip-checks，跳过）")
    else:
        ok = run("参数台账与代码一致", [sys.executable, QA / "param-inventory.py", "--check"])
        results.append(("参数台账", "ok" if ok else "fail"))

    # 7. 截图（可选）
    if args.with_screenshot:
        ok = run("截图自检（画面不是空图）", [sys.executable, QA / "shot-check.py"])
        results.append(("截图自检", "ok" if ok else "fail"))
    else:
        results.append(("截图自检", "skip"))
        print("\n=== 截图自检 ===\n（跳过：需要 --with-screenshot，且需要显示器）")

    # 8. 变异矩阵（可选）
    if with_mutations:
        scripts = sorted(QA.glob("mutate-*.py"))
        if args.mutations:
            needle = args.mutations.lower()
            picked = [s for s in scripts if needle in s.name.lower()]
            if not picked:
                # ⚠️ 打印 **ASCII 标记**：中文在某些控制台下会被按系统代码页解码成乱码
                #    （本机 GBK）⇒ 靠中文断言的守卫会**永远红**（或永远绿，看运气）。
                #    标记用 ASCII ⇒ 谁读都读得对。
                print(f"\n[QA] NO-MUTATION-SCRIPTS-MATCH: no mutate-*.py contains "
                      f"'{args.mutations}'")
                print(f"!! 没有名字含「{args.mutations}」的变异脚本。"
                      f"现有的：{', '.join(s.name for s in scripts)}")
                results.append(("变异矩阵", "fail"))
                picked = []
            scripts = picked
        for s in scripts:
            ok = run(f"变异矩阵 {s.name}", [sys.executable, s])
            results.append((f"变异 {s.name}", "ok" if ok else "fail"))
        if args.mutations and scripts:
            print(f"\n（只跑了名字含「{args.mutations}」的 {len(scripts)} 个变异脚本）")
    else:
        results.append(("变异矩阵", "skip"))
        print("\n=== 变异矩阵 ===\n（跳过：需要 --with-mutations 或 --mutations）")

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
