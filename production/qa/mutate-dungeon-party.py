"""
对 IT-05 / F-02 的守卫（`DungeonPartyConditionTests`）做**变异看红**验证。

这条守卫守的是：**队伍不满足参与条件时，既不创建任务、也不开副本**；
以及它的**正向条件**——队伍够格时，副本**真的跑起来**（战斗被解算）。

⚠️ 它补的是**单元层给不出的那一块**：`Mission.Create` 拒绝不合法队伍这件事，
`QuestsTests` 已经验过（任务侧）。但「**副本那一侧**」从来没人验 ——
`DungeonFlow` 是一条**独立于任务**的入口，它自己就会 `DungeonRun.Create` + `Enter`。
若某条路径绕过任务校验直接开副本，就会出现「**没有任务的副本**」：
战斗真打了、结算真发了，而日志里查不到这次出发的依据。

三档变异：

  ① `Mission.Create` 不再检查队伍合法性 ⇒ 不够格也能建任务 ⇒ 红
  ② `IsRosterLegal` 永远返回 true ⇒ 队伍完备性判定失效 ⇒ 红
  ③ `Squad.TryJoin` 允许加入 ⇒ ……（加入侧不参与本守卫，另见 QuestsTests）

用法：python production/qa/mutate-dungeon-party.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~DungeonPartyConditionTests"
QUESTS = ROOT / "src" / "Domain" / "Quests" / "Quests.cs"


def sha(p: Path) -> str:
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


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求红 → 复原文件。"""
    backup = target.read_bytes()
    before = sha(target)
    raw = backup.decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old_n = old.replace("\r\n", "\n")
    new_n = new.replace("\r\n", "\n")
    if old_n not in text:
        print(f"!! [{label}] 模式没找到")
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


# ① Mission.Create 不再检查队伍合法性
MUT1_OLD = """        if (!squad.IsRosterReady)
        {
            throw new ArgumentException(
                $"队伍不满足编队合法性规则，拒绝为其创建任务 {id}。"
                + "无效队伍不能启动任务（02 F-01）。",
                nameof(squad));
        }
"""
MUT1_NEW = """        // 变异：不检查队伍合法性（不够格也能建任务 ⇒ 副本会被开起来）
"""

# ② IsRosterLegal 永远返回 true
MUT2_OLD = """    public bool IsRosterLegal(IReadOnlyList<HeroState> members) => _isRosterLegal(members);"""
MUT2_NEW = """    public bool IsRosterLegal(IReadOnlyList<HeroState> members) => true;   // 变异：完备性判定失效"""


def main():
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）全绿 —— 判据确实能绿")

    # ── ① Mission.Create 不再校验队伍 ────────────────────────────────────
    results.append(mutate(
        "Mission.Create 不再检查队伍合法性（不够格也能建任务 ⇒ 副本被开起来）",
        QUESTS,
        MUT1_OLD,
        MUT1_NEW,
    ))

    # ── ② IsRosterLegal 永远返回 true ───────────────────────────────────
    results.append(mutate(
        "IsRosterLegal 永远返回 true（队伍完备性判定失效）",
        QUESTS,
        MUT2_OLD,
        MUT2_NEW,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
