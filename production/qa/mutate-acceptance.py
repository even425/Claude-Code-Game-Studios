"""
对 AcceptancePlanTests（UT-02 / UT-04 / UT-10）做**变异看红**验证。

规矩：<b>不养烧不着的守卫</b>。这三条断言守的是「领域层看不见帧率」
「行动只完成一次」「空转不留痕」—— 全是<b>看不见的坏</b>，
只有把代码改坏、看见它们变红，才算真的在守。

用法：python production/qa/mutate-acceptance.py
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
FILTER = "FullyQualifiedName~AcceptancePlanTests"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    m = re.search(r"失败:\s*(\d+)", out)
    if m:
        return int(m.group(1)), out
    m2 = re.search(r"Failed:\s*(\d+)", out)
    if m2:
        return int(m2.group(1)), out
    return -1, out


def mutate(label, rel_path, old, new):
    p = ROOT / rel_path
    backup = p.read_bytes()
    before = sha(p)
    raw = p.read_text(encoding="utf-8")
    # 源文件是 CRLF、模式里写的是 LF ⇒ 先归一化再匹配，
    # 写回时再还原成原换行符（否则这个「临时改动」会把整个文件的换行搅了）。
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    if old not in text:
        print(f"!! [{label}] 模式没找到")
        return False
    text = text.replace(old, new, 1)
    if crlf:
        text = text.replace("\n", "\r\n")
    p.write_text(text, encoding="utf-8", newline="")
    try:
        failed, out = run_tests()
        color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        p.write_bytes(backup)
        if sha(p) != before:
            print(f"!! 复原失败 [{label}]")


def main():
    results = []

    # ── 1. 让模拟变得依赖帧率（把现实帧增量漏进模拟步）────────────────
    # 这正是 A-04 那条技术债（表现层驱动领域）被「不小心」 reintroduce 的形态。
    results.append(mutate(
        "把现实帧增量漏进模拟步（帧率依赖）",
        "src/Application/SimulationRunner.cs",
        "            _clock.AdvanceSimSeconds(stepLength);\n            RunSteps(stepLength);",
        "            _clock.AdvanceSimSeconds(stepLength);\n            RunSteps(simDeltaThisFrame);",
    ))

    # ── 2. 让同一个行动实例能被「完成」两次 ──────────────────────────
    # 形态：完成时忘了把当前行动清空 ⇒ 再推一帧就又完成一次。
    results.append(mutate(
        "行动完成后忘了清空当前活动（可重复完成）",
        "src/Domain/Heroes/HeroState.cs",
        "        var finished = CurrentActivity;\n        CurrentActivity = ActivityId.None;\n        RemainingActionSeconds = 0.0;\n        return finished;",
        "        var finished = CurrentActivity;\n        RemainingActionSeconds = 0.0;\n        return finished;",
    ))

    # ── 3. 让「占用中」的英雄也能重新决策 ─────────────────────────────
    # 形态：S1 的资格检查被绕过 ⇒ 还在干活的英雄会开始新行动。
    results.append(mutate(
        "占用中的英雄也能重新决策（绕过 S1 资格检查）",
        "src/Domain/Heroes/DecisionFlow.cs",
        "        if (!hero.IsEligibleForDecision())",
        "        if (false && !hero.IsEligibleForDecision())",
    ))

    # ── 4. 让「候选为空」不再是空转（偷偷开始一项行动）──────────────
    # 形态：候选集空 ⇒ 回退去启动某项活动（基线 §3.5 明写不许回退）。
    results.append(mutate(
        "候选为空时偷偷开始一项行动（空转变成行动）",
        "src/Domain/Heroes/DecisionFlow.cs",
        "                return new DecisionResult(DecisionOutcome.NoCandidates, ActivityId.None);",
        "                hero.BeginAction(_activities[0].Id, _activities[0].DurationSeconds);\n"
        "                return new DecisionResult(DecisionOutcome.Started, _activities[0].Id);",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
