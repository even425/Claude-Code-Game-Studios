"""
对「领域层脱离 Godot 也能编译**并运行**」这道门（不变量 **I9**）做**变异看红**验证。

`tests/DomainPurity/DomainPurity.csproj` 是一个**零依赖**的控制台程序：
不用 `Godot.NET.Sdk`、不引 `GodotSharp`、**不引任何 NuGet 包**、不 `ProjectReference` 主工程；
它只直接编译 `src/Domain/**` 的源文件，然后
  ① 跑几条领域规则（编译 + 运行都得到证明）
  ② 检查**本程序集引用了哪些程序集** ⇒ 必须没有 Godot（运行时证明，比静态扫描强：
     静态扫描查源码字样，这一条查**产物** ⇒ 反射、延迟加载也躲不过）

**为什么必须零依赖**：⚠️ 这是踩出来的 —— 本工程**曾经**用 `gdUnit4.api` 当测试运行器，
只引它确实能编译能跑，**看起来 gdUnit4 不需要 GodotSharp**。
**但 gdUnit4.api 传递依赖 `GodotSharp`** ⇒ 依赖图里仍然有它
⇒ 领域层里写一行 `using Godot;` **照样编译通过** ⇒ **那道门形同虚设**。
一个「看起来在守、其实不守」的守卫**比没有守卫更糟**（它让人以为有人盯着）。

四档变异：
  ① 领域层里加一行 `using Godot;`          ⇒ 编译失败（红）
  ② 领域层里用一个 Godot 类型（`Vector2`）   ⇒ 编译失败（红）
  ③ 给这个工程加一条 GodotSharp 包引用      ⇒ **编译仍过**，但**运行时**断言红
     （这正是运行时那条存在的理由：编译门抓不到它）
  ④ 什么都不改                            ⇒ 编译并运行通过（绿）

用法：python production/qa/mutate-domain-purity.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "tests" / "DomainPurity" / "DomainPurity.csproj"
TARGET = ROOT / "src" / "Domain" / "Clock" / "SimClock.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def build_and_run() -> tuple[bool, str]:
    """编译**并运行**那个「不引用 Godot」的工程。返回 (成功?, 输出)。"""
    r = subprocess.run(
        ["dotnet", "run", "--project", str(PROJECT), "--nologo", "-v", "q"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = (r.stdout or "") + (r.stderr or "")
    ok = r.returncode == 0 and "error CS" not in out
    return ok, out


def build_only() -> tuple[bool, str]:
    """只编译（更快；用于「编译必须失败」那几档）。"""
    r = subprocess.run(
        ["dotnet", "build", str(PROJECT), "--nologo", "-v", "q"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = (r.stdout or "") + (r.stderr or "")
    return r.returncode == 0 and "error CS" not in out, out


def mutate_source(label, old, new, expect_compile_fail=True):
    """改领域层源文件一处 → 编译 → 期望失败 → 复原。"""
    backup = TARGET.read_bytes()
    before = sha(TARGET)
    raw = backup.decode("utf-8")
    if old not in raw:
        print(f"!! [{label}] 模式没找到")
        return False
    TARGET.write_text(raw.replace(old, new, 1), encoding="utf-8", newline="")
    try:
        ok, out = build_only()
        red = not ok
        good = red == expect_compile_fail
        print(f"{'OK  ' if good else '!!  '}变异 [{label}] -> "
              f"{'编译失败（RED）' if red else '编译通过（GREEN）'}"
              f"（期望 {'失败' if expect_compile_fail else '通过'}）")
        if not good:
            tail = "\n".join([l for l in out.splitlines() if "error" in l.lower()][:3])
            print("      " + tail[:400].replace("\n", " | "))
        return good
    finally:
        TARGET.write_bytes(backup)
        if sha(TARGET) != before:
            print("!! 复原失败")


def mutate_project(label, old, new, expect_green=False):
    """改工程文件一处 → **编译并运行** → 期望运行红（运行时断言抓到了）→ 复原。

    `expect_green=True` 用于「加一条**没被用到**的包引用」那一档：
    程序确实不依赖它 ⇒ 仍然绿才是对的。
    """
    backup = PROJECT.read_bytes()
    before = sha(PROJECT)
    raw = backup.decode("utf-8")
    if old not in raw:
        print(f"!! [{label}] 模式没找到")
        return False
    PROJECT.write_text(raw.replace(old, new, 1), encoding="utf-8", newline="")
    try:
        ok, out = build_and_run()
        green = ok
        good = green == expect_green
        print(f"{'OK  ' if good else '!!  '}变异 [{label}] -> "
              f"{'运行绿' if green else '运行红'}（期望 {'绿' if expect_green else '红'}）")
        if not good:
            keep = [l for l in out.splitlines() if "FAIL" in l or "❌" in l][:4]
            print("      " + " | ".join(keep)[:400])
        return good
    finally:
        PROJECT.write_bytes(backup)
        if sha(PROJECT) != before:
            print("!! 复原失败")


def mutate_project_and_source(label, proj_old, proj_new, src_old, src_new):
    """**同时**改工程文件与领域层源文件 ⇒ 编译**能过**，但运行时断言抓到了。

    这是运行时那条断言的**真正用武之地**：
    只给工程加一条 `GodotSharp` 包引用 ⇒ 程序**根本没用到**它 ⇒
    编译器的 IL 里没有它 ⇒ 引用列表里也没有 ⇒ **仍然绿**（这是对的：
    「程序确实不依赖 Godot」）。
    ⇒ 要让运行时断言抓得到，必须**同时**让领域层**用上**一个 Godot 类型
    —— 而那需要 GodotSharp **可用** ⇒ 于是编译能过，但产物**依赖**了 Godot ⇒ 红。
    """
    proj_backup = PROJECT.read_bytes()
    src_backup = TARGET.read_bytes()
    proj_before = sha(PROJECT)
    src_before = sha(TARGET)
    try:
        praw = proj_backup.decode("utf-8")
        sraw = src_backup.decode("utf-8")
        if proj_old not in praw or src_old not in sraw:
            print(f"!! [{label}] 模式没找到")
            return False
        PROJECT.write_text(praw.replace(proj_old, proj_new, 1), encoding="utf-8", newline="")
        TARGET.write_text(sraw.replace(src_old, src_new, 1), encoding="utf-8", newline="")

        ok, out = build_and_run()
        red = not ok
        good = red
        print(f"{'OK  ' if good else '!!  '}变异 [{label}] -> "
              f"{'运行红' if red else '运行绿'}（期望 红）")
        if not red:
            keep = [l for l in out.splitlines() if "FAIL" in l or "❌" in l][:4]
            print("      " + " | ".join(keep)[:400])
        return red
    finally:
        PROJECT.write_bytes(proj_backup)
        TARGET.write_bytes(src_backup)
        if sha(PROJECT) != proj_before or sha(TARGET) != src_before:
            print("!! 复原失败")


def main() -> int:
    results = []

    # ── 基线：必须编译并运行通过（否则下面几档没有意义）────────────────
    ok, out = build_and_run()
    if not ok:
        print("!! 基线就跑不过 —— 先修好再谈变异")
        for line in [l for l in out.splitlines() if "error" in l.lower()][:5]:
            print("   ", line.strip()[:200])
        return 1
    print("OK  基线（领域层在零依赖的工程里）编译并运行通过")

    # ── ① 加一行 `using Godot;` ──────────────────────────────────────────
    results.append(mutate_source(
        "领域层里加一行 `using Godot;`",
        "using System;",
        "using Godot;\nusing System;",
    ))

    # ── ② 用一个 Godot 类型（全名，不加 using）─────────────────────────
    results.append(mutate_source(
        "领域层里用一个 Godot 类型（全名，不加 using）",
        "public class SimClock\n{",
        "public class SimClock\n{\n    private readonly Godot.Vector2 _v = new Godot.Vector2();\n",
    ))

    # ── ③ 给工程加一条 GodotSharp 包引用 **且** 领域层用上它 ────────────
    #    ⇒ 编译**能过**（GodotSharp 可用）⇒ 编译门抓不到
    #    ⇒ 但产物的引用列表里会冒出 GodotSharp ⇒ **运行时断言红** ✓
    results.append(mutate_project_and_source(
        "给工程加 GodotSharp 包引用 + 领域层用上 Godot 类型（编译仍过，运行时断言红）",
        "  <ItemGroup>\n    <!-- 领域层源文件",
        "  <ItemGroup>\n    <PackageReference Include=\"GodotSharp\" Version=\"4.7.2\" />\n  </ItemGroup>\n"
        "  <ItemGroup>\n    <!-- 领域层源文件",
        "public class SimClock\n{",
        "public class SimClock\n{\n    private readonly Godot.Vector2 _v = new Godot.Vector2();\n",
    ))

    # ── ④ 只加包引用、**不用**它 ⇒ 必须仍然绿 ──────────────────────────
    #    「程序确实不依赖 Godot」—— 这正是运行时那条断言要说的意思：
    #    它查的是**产物的依赖**，不是**依赖的可用性**。
    results.append(mutate_project(
        "只加一条 GodotSharp 包引用、但领域层不用它（仍然绿：程序确实不依赖它）",
        "  <ItemGroup>\n    <!-- 领域层源文件",
        "  <ItemGroup>\n    <PackageReference Include=\"GodotSharp\" Version=\"4.7.2\" />\n  </ItemGroup>\n"
        "  <ItemGroup>\n    <!-- 领域层源文件",
        expect_green=True,
    ))

    # ── ⑤ 什么都不改 ⇒ 必须仍然通过（证明前几档不是「本来就红」）─────────
    results.append(mutate_source(
        "什么都不改（证明前几档不是「本来就红」）",
        "public class SimClock\n{",
        "public class SimClock\n{",
        expect_compile_fail=False,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
