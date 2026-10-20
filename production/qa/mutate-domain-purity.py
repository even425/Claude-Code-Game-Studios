"""
对「领域层脱离 Godot 也能编译」这道门（不变量 **I9**）做**变异看红**验证。

`tests/DomainPurity/DomainPurity.csproj` 是一个**故意什么都不引**的工程：
不用 `Godot.NET.Sdk`、不引用 `GodotSharp`、不 `ProjectReference` 主工程 ——
它只直接编译 `src/Domain/**` 的源文件。
⇒ 领域层里多一行 `using Godot;` ⇒ **这个工程编译失败**。

**为什么要有这道门**：测试工程通过 `ProjectReference` 引用整个主工程，
而主工程是 `Godot.NET.Sdk` 且引用 `GodotSharp` ⇒ **在那个测试工程里跑测试，
证明不了「领域层脱离 Godot 也能编译运行」** —— GodotSharp 已在引用链上，
`using Godot` 一定能编译成功。
`DomainLayerPurityTests` 守的是「领域层**没有引用** Godot」；
本工程守的是「领域层**在没有 Godot 的情况下真的能编译**」—— 两者不是一回事。

三档变异：
  ① 领域层里加一行 `using Godot;`        ⇒ 编译失败（红）
  ② 领域层里用一个 Godot 类型（`Vector2`） ⇒ 编译失败（红）
  ③ 不变（基线）                          ⇒ 编译通过（绿）

用法：python production/qa/mutate-domain-purity.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "tests" / "DomainPurity" / "DomainPurity.csproj"
TARGET = ROOT / "src" / "Domain" / "Clock" / "SimClock.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def build() -> tuple[bool, str]:
    """编译那个「不引用 Godot」的工程。返回 (成功?, 输出尾部)。"""
    r = subprocess.run(
        ["dotnet", "build", str(PROJECT), "--nologo", "-v", "q"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = (r.stdout or "") + (r.stderr or "")
    ok = r.returncode == 0 and "error CS" not in out
    return ok, out


def mutate(label, old, new, expect_compile_fail=True):
    """改一处 → 编译 → 期望失败 → 复原。"""
    backup = TARGET.read_bytes()
    before = sha(TARGET)
    raw = backup.decode("utf-8")
    if old not in raw:
        print(f"!! [{label}] 模式没找到")
        return False
    TARGET.write_text(raw.replace(old, new, 1), encoding="utf-8", newline="")
    try:
        ok, out = build()
        red = not ok
        good = red == expect_compile_fail
        print(f"{'OK  ' if good else '!!  '}变异 [{label}] -> "
              f"{'编译失败（RED）' if red else '编译通过（GREEN）'}（期望 {'失败' if expect_compile_fail else '通过'}）")
        if not good:
            tail = "\n".join([l for l in out.splitlines() if "error" in l.lower()][:3])
            print("      " + tail[:400].replace("\n", " | "))
        return good
    finally:
        TARGET.write_bytes(backup)
        if sha(TARGET) != before:
            print("!! 复原失败")


def main() -> int:
    results = []

    # ── 基线：必须编译通过（否则下面三档没有意义）────────────────────────
    ok, out = build()
    if not ok:
        print("!! 基线就编译不过 —— 先修好再谈变异")
        for line in [l for l in out.splitlines() if "error" in l.lower()][:5]:
            print("   ", line.strip()[:200])
        return 1
    print("OK  基线（领域层在无 Godot 的工程里）编译通过")

    # ── ① 加一行 `using Godot;` ──────────────────────────────────────────
    results.append(mutate(
        "领域层里加一行 `using Godot;`",
        "using System;",
        "using Godot;\nusing System;",
    ))

    # ── ② 用一个 Godot 类型（全名，不加 using）─────────────────────────
    results.append(mutate(
        "领域层里用一个 Godot 类型（全名，不加 using）",
        "public class SimClock\n{",
        "public class SimClock\n{\n    private readonly Godot.Vector2 _v = new Godot.Vector2();\n",
    ))

    # ── ③ 不变 ⇒ 必须仍然编译通过（证明前两档不是「本来就红」）────────────
    results.append(mutate(
        "什么都不改（证明前两档不是「本来就红」）",
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
