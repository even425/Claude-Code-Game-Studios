"""
对 D-074「建造流程」的守卫做**变异看红**验证。

**它守的是什么**：
  ① 开工要扣两样资源，**一样不够就一样都不扣**（I15 / I12）
  ② 工期由人力决定：人越多越快，没人就停工
  ③ **建成才生效**：半成品围栏不放开摆摊范围
  ④ 试炼塔**只加生命**、**永久**、战斗完全确定
  ⑤ 试炼塔的门槛**真的有区分度**（不是人人都能过）

⚠️ **三档特别值得验**：
  · 「建成才生效」若写成「开工就生效」⇒ 那条断言当时是装饰；
  · 「只加生命」若改成「也加攻防」⇒ D-074 的拍板内容没被守住；
  · 「门槛有区分度」若守卫档位调回普通档 ⇒ 九种原型全都能过 ⇒ 门槛形同虚设。

用法：python production/qa/mutate-d074-build.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~BuildingServiceTests"

SERVICE = ROOT / "src" / "Application" / "BuildingService.cs"
TOWER = ROOT / "src" / "Application" / "TrialTowerService.cs"
CATALOG = ROOT / "src" / "Domain" / "Village" / "BuildableBuilding.cs"
VILLAGE = ROOT / "src" / "Domain" / "Village" / "Village.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
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
        if not _restore(target, backup, before):
            print(f"!! 复原失败 [{label}]（变异可能留在源码里！）")



def _restore(target, backup: bytes, expected_sha: str) -> bool:
    """把源文件复原回去（**带重试与校验**）。

    ⚠️ **为什么需要重试**：Windows 上 `dotnet test` 的 MSBuild 节点会**复用**
    （node reuse）⇒ 编译服务器在子进程退出后**仍持有源文件的句柄**
    ⇒ 直接写会报 `OSError: [Errno 22]` ⇒ **变异就留在源码里了**
    （实测：D-079 那一档就这样残留过，把幂等机制改成了摆设）。

    ⚠️ **为什么必须校验**：复原失败还接着往下跑 ⇒ 下一个脚本的**基线是红的**
    ⇒ 矩阵「失败」但不是真失败 ⇒ 比没有守卫更糟。
    ⇒ 复原不成功就**大声报错**，让这一轮明确失败。
    """
    import hashlib
    import time

    for attempt in range(3):
        try:
            target.write_bytes(backup)
        except OSError:
            time.sleep(0.4)
            continue

        if hashlib.sha256(target.read_bytes()).hexdigest() == expected_sha:
            return True
        time.sleep(0.4)

    print(f"!! 复原失败（文件被占用？）：{target}")
    return False

def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— 那五条性质当前成立")

    # ── ① 放不下建筑时不退款 ⇒ 「扣了钱没建成」的半笔账 ───────────────
    # ⚠️ **变异点选在真正会出事的那一步**：钱与材料先扣，
    # **然后**才放建筑 ⇒ 放不下时钱已经被扣了 ⇒ 不退款就是半笔账。
    # （「钱不够 / 材料不够」在扣款**之前**就被 CanStart 挡住 ⇒ 测它们碰不到退款分支。）
    results.append(mutate(
        "放不下建筑时不退款（半笔账：扣了钱没建成）",
        SERVICE,
        "            _village.Add(_gold, definition.GoldCost);\n"
        "            _village.Add(_material, definition.MaterialCost);\n"
        "            return false;",
        "            // 变异：不退款\n"
        "            return false;",
    ))

    # ── ② 人数不影响进度 ⇒ 「人力决定工期」被破坏 ─────────────────────
    results.append(mutate(
        "人数不影响进度（工期与人无关）",
        SERVICE,
        "            double progress = c.Workers * workedHours / c.Definition.BaseBuildHours;",
        "            double progress = workedHours / c.Definition.BaseBuildHours;   // 变异：不算人数",
    ))

    # ── ③ 未建成就生效 ⇒ 「建成才生效」被破坏 ─────────────────────────
    results.append(mutate(
        "在建中的围栏也放开范围（未建成即生效）",
        VILLAGE,
        "        if (Status != BuildingStatus.Completed) return false;   // 未建成 = 不影响",
        "        // 变异：未建成也生效",
    ))

    # ── ④ 试炼塔也加攻防 ⇒ 「只加生命」被破坏 ─────────────────────────
    results.append(mutate(
        "试炼塔连攻防一起加（不再是「只加生命」）",
        TOWER,
        "        var after = won\n"
        "            ? stats with { Health = stats.Health + HealthBonusPerWin }\n"
        "            : stats;",
        "        var after = won\n"
        "            ? stats with { Health = stats.Health + HealthBonusPerWin,\n"
        "                             Attack = stats.Attack + 5 }   // 变异：连攻防也加\n"
        "            : stats;",
    ))

    # ── ⑤ 守卫档位调回普通档 ⇒ 门槛形同虚设 ───────────────────────────
    results.append(mutate(
        "试炼塔守卫调回普通档（九种原型全都能过）",
        TOWER,
        "    private const int GuardTierIndex = 1;",
        "    private const int GuardTierIndex = 0;   // 变异：门槛失效",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
