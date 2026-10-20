"""
对 DeathHasNoPersistentCostTests（IT-16 / I6 死亡无持久代价）做**变异看红**验证。

死亡代价是**很容易被「顺手加上」**的东西：实现战斗时想到「英雄死了该有个标记」，
加一个 IsDead / Wounds 字段 —— 当时看完全合理，但那一行代码**永久改变了规则**
（存档要存它、界面要显示它、平衡要为它留位置），而规则说它**根本不该存在**。
所以它必须由机器守。

四档变异：
  ① 加一个 IsDead 布尔
  ② 加一个 Wounds 整数
  ③ 加一个中文名「死亡」布尔
  ④ 加一个 IsAlive 布尔（不是「死亡」但是「生死位」）

用法：python production/qa/mutate-death-cost.py
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
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~DeathHasNoPersistentCostTests"
HERO_STATE = ROOT / "src" / "Domain" / "Heroes" / "HeroState.cs"


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1)), out
    return -1, out


def report(label, failed):
    color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
    ok = failed > 0
    print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, 期望 RED)")
    return ok


def add_member(member):
    """在 HeroState 里加一个公开成员（临时变异）。"""
    p = HERO_STATE
    backup = p.read_bytes()
    text = backup.decode("utf-8")
    anchor = "public sealed class HeroState\n{\n"
    idx = text.index(anchor) + len(anchor)
    mutated = text[:idx] + member + "\n" + text[idx:]
    p.write_text(mutated, encoding="utf-8", newline="")
    try:
        failed, _ = run_tests()
        return failed
    finally:
        p.write_bytes(backup)
        if sha(p) != hashlib.sha256(backup).hexdigest():
            print("!! 复原失败")


def main():
    results = []

    results.append(report("加一个 IsDead 布尔",
                          add_member("    /// <summary>【变异用】</summary>\n"
                                     "    public bool IsDead { get; set; }")))

    results.append(report("加一个 Wounds 整数",
                          add_member("    /// <summary>【变异用】</summary>\n"
                                     "    public int Wounds { get; set; }")))

    results.append(report("加一个中文名「死亡」布尔",
                          add_member("    /// <summary>【变异用】</summary>\n"
                                     "    public bool 死亡 { get; set; }")))

    results.append(report("加一个 IsAlive 布尔（生死位）",
                          add_member("    /// <summary>【变异用】</summary>\n"
                                     "    public bool IsAlive { get; set; }")))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
