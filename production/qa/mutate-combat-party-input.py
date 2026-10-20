"""
对「战斗能看到队伍配置」这条守卫（`CombatPartyInputTests`）做**变异看红**验证。

它守的是 **IT-31（队伍配置按规则影响副本结果）的**前置**：
战斗解算器拿得到参战者的**状态快照**（成员 / 特质修正 / 偏好权重），
而不只是**一串标识**。

⚠️ 这个通道**没有任何测试盯着**时，会被下一次「顺手重构接口」悄悄删掉 ——
而删掉它的后果**不会立刻报错**：战斗照样跑、照样出结果，
只是**再也不可能用队伍配置**。等 T-004 拍板时才发现，就得连接口带调用方一起改一遍。

四档变异：

  ① 快照变成**活引用**（不再复制字典）⇒ 解算后再改状态，
     **已经算出来的战斗就变了**（基线 §8.5 D5：战斗结果要能离线复现）⇒ 红
  ② `DungeonFlow` 把它换成**只有标识的空状态快照**（真实快照被丢掉）⇒ 红
  ③ 快照**不复制**偏好权重 ⇒ 同 ①，但走另一本字典 ⇒ 红
  ④ `Resolve` 收到的是**重新造的等价物**而不是同一份快照 ⇒ 红

用法：python production/qa/mutate-combat-party-input.py
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
FILTER = "FullyQualifiedName~CombatPartyInputTests"
SNAPSHOT = ROOT / "src" / "Domain" / "Combat" / "CombatantSnapshot.cs"
FLOW = ROOT / "src" / "Application" / "DungeonFlow.cs"


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


# ① 特质字典不再复制（活引用）
MUT1_OLD = """    private static IReadOnlyDictionary<string, double> Copy(
        IReadOnlyDictionary<string, double>? source)
        => source is null
            ? new Dictionary<string, double>(StringComparer.Ordinal)
            : new Dictionary<string, double>(source, StringComparer.Ordinal);"""
MUT1_NEW = """    private static IReadOnlyDictionary<string, double> Copy(
        IReadOnlyDictionary<string, double>? source)
        => source ?? new Dictionary<string, double>(StringComparer.Ordinal);   // 变异：活引用"""

# ③ 偏好权重不再复制
MUT3_OLD = """        => new(id ?? string.Empty,
               name ?? string.Empty,
               Copy(traitModifiers),
               Copy(preferenceWeights));"""
MUT3_NEW = """        => new(id ?? string.Empty,
               name ?? string.Empty,
               Copy(traitModifiers),
               preferenceWeights ?? new Dictionary<string, double>(StringComparer.Ordinal));  // 变异：偏好权重活引用"""

# ② DungeonFlow 把真实快照换成只有标识的空状态快照
MUT2_OLD = """        _party = party ?? CombatPartySnapshot.FromIds(_run.Participants);"""
MUT2_NEW = """        _party = CombatPartySnapshot.FromIds(_run.Participants);   // 变异：真实快照被丢掉"""

# ④ Resolve 收到重新造的等价物
MUT4_OLD = """        var log = _resolver.Resolve(encounterName, _party)"""
MUT4_NEW = """        var log = _resolver.Resolve(encounterName,
            CombatPartySnapshot.FromIds(_run.Participants))   // 变异：换成只有标识的等价物"""


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

    results.append(mutate(
        "快照的特质字典变成活引用（解算后再改状态，已算出的战斗就变了）",
        SNAPSHOT, MUT1_OLD, MUT1_NEW))

    results.append(mutate(
        "DungeonFlow 把真实快照换成只有标识的空状态快照（战斗看不到队伍配置了）",
        FLOW, MUT2_OLD, MUT2_NEW))

    results.append(mutate(
        "快照的偏好权重变成活引用",
        SNAPSHOT, MUT3_OLD, MUT3_NEW))

    results.append(mutate(
        "Resolve 收到重新造的等价物而不是同一份快照",
        FLOW, MUT4_OLD, MUT4_NEW))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
