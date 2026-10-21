"""
对 D-071 / D-072 / D-073 三条新守卫做**变异看红**验证。

**它守的是什么**：
  ① D-072 首领档胜率必须落在 70–90%（`BossStage_WinRateAcrossAllTwoHeroCrews_…`）
  ② D-071 一次纠纷 = −10、一次成功 = +10（`RelationshipServiceTests`）
  ③ D-073 回合数读不出就失败、换算是纯函数（`StageDurationTests`）

⚠️ **为什么必须变异看红**：这三条断言里有两类特别容易「装饰」——
  · **区间断言**：写成 `rate >= 0 && rate <= 100` 就永远绿 ⇒ 必须证明它真的卡在 70/90；
  · **读不出就失败**：如果实现悄悄回落成 0，守卫自己可能也是绿的
    ⇒ 必须把「读不出」改成「返回 0」看它会不会红。

**五档变异**：
  ① 首领血量调回 180 ⇒ 胜率掉到 37.8% ⇒ 区间断言必须红
  ② 区间放宽成 0–100 ⇒ 断言变成装饰 ⇒ 必须红
  ③ 纠纷幅度改成 −1（不等幅）⇒ D-071 必须红
  ④ 读不出回合数时返回 0 而不是抛 ⇒ D-073 必须红
  ⑤ 每回合小时数改成 1.0 ⇒ 量级断言（≈一天）必须红

用法：python production/qa/mutate-d071-d073.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~FormulaCombatTests|FullyQualifiedName~RelationshipServiceTests|FullyQualifiedName~StageDurationTests"

PARAMS = ROOT / "src" / "Domain" / "Combat" / "CombatParameters.cs"
RULES = ROOT / "src" / "Domain" / "Relationships" / "Relationship.cs"
READER = ROOT / "src" / "Domain" / "Combat" / "BattleLogReader.cs"
DURATION = ROOT / "src" / "Domain" / "Combat" / "StageDuration.cs"
COMBAT_TEST = ROOT / "tests" / "unit" / "combat" / "FormulaCombatTests.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求**红** → 复原文件（逐字节）。"""
    backup = target.read_bytes()
    before = sha(target)
    raw = backup.decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old_n = old.replace("\r\n", "\n")
    new_n = new.replace("\r\n", "\n")
    if old_n not in text:
        print(f"!! [{label}] 模式没找到 —— 变异脚本自己过期了")
        return False
    mutated = text.replace(old_n, new_n, 1)
    if crlf:
        mutated = mutated.replace("\n", "\r\n")
    target.write_text(mutated, encoding="utf-8", newline="")
    try:
        failed, out = run_tests()
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        target.write_bytes(backup)
        if sha(target) != before:
            print(f"!! 复原失败 [{label}]")


def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— 那三条性质当前成立")

    # ── ① 首领血量调回 180 ⇒ 胜率 37.8% ⇒ 区间断言红 ────────────────
    results.append(mutate(
        "首领血量调回 180（胜率掉到 37.8%）",
        PARAMS,
        '        new("首领", 8, 4, 120),',
        '        new("首领", 8, 4, 180),',
    ))

    # ── ② 首领血量调到 80 ⇒ 胜率 100%（人人必过）⇒ 区间上界必须红 ──────
    results.append(mutate(
        "首领血量调到 80（胜率 100%，人人必过）",
        PARAMS,
        '        new("首领", 8, 4, 120),',
        '        new("首领", 8, 4, 80),',
    ))

    # ── ③ 纠纷幅度改成 −1（与 +10 不等幅）⇒ D-071 红 ───────────────────
    results.append(mutate(
        "纠纷幅度改成 −1（与成功的 +10 不等幅）",
        RULES,
        "    public const int DisputeDelta = -10;",
        "    public const int DisputeDelta = -1;",
    ))

    # ── ④ 读不出回合数时返回 0 而不是抛 ⇒ D-073 红 ───────────────────
    results.append(mutate(
        "读不出回合数时返回 0（不抛）",
        READER,
        "        var match = RoundsPattern.Match(deciding[0].Summary ?? string.Empty);\n"
        "        if (!match.Success)",
        "        var match = RoundsPattern.Match(deciding[0].Summary ?? string.Empty);\n"
        "        if (!match.Success) return 0;   // 变异：悄悄当成 0 回合\n"
        "        if (false)",
    ))

    # ── ⑤ 每回合小时数改成 1.0 ⇒ 满回合约 3.75 天 ⇒ 量级断言红 ────────
    results.append(mutate(
        "每回合小时数改成 1.0（量级不再是「一天」）",
        DURATION,
        "    public const double GameHoursPerRound = 0.25;",
        "    public const double GameHoursPerRound = 1.0;",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
