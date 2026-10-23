"""
对 D-096「盗匪来袭」的守卫做**变异看红**验证。

**它守的是五件事**：
  ① 打赢 ⇒ **缴获**（材料 +N、金币 +N）
  ② 打输 ⇒ **被抢物资**（材料 −N，**金币不动**）
  ③ 没人出击 ⇒ 必败（一样被抢）
  ④ 阶段**比兽潮少一个**（没有「后续影响」）⇒ 证明「阶段可跳过」
  ⑤ **不被**兽潮那道「守不住就不触发」的闸拦住

⚠️ **为什么这一档重要**：基线 §11.5 原话是「框架的可扩展性需要至少两个实例来验证」。
现在有**三个**（兽潮 / 商队 / 盗匪）⇒ 若盗匪的形状其实和兽潮一样、
或者「缴获 / 被抢」的符号反了，那这条通用性证据就是**假绿**。

用法：python production/qa/mutate-d096-bandit.py
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
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~BanditRaidTests"

TEMPLATES = ROOT / "src" / "Application" / "WorldEventTemplates.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
    # ⇒ `MSBUILDDISABLENODEREUSE=1`：禁用 MSBuild 节点复用 ⇒ 编译服务器不留文件句柄
    #   （否则下一步的「复原」会失败 ⇒ **变异残留在源码里**）。
    try:
        r = subprocess.run(
            ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
             "--filter", FILTER],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=300,
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    except subprocess.TimeoutExpired:
        _kill_dotnet()
        return -2, "TIMEOUT: 测试超过 300 秒未结束（守卫挂死，不是跑红）"
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def _kill_dotnet():
    """清掉挂死的测试宿主（否则它会一直占着 CPU 与文件锁）。"""
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"], capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"], capture_output=True, text=True)


def _restore(target, backup: bytes, expected_sha: str) -> bool:
    """把源文件复原回去（**带重试与校验**）。"""
    for _ in range(3):
        try:
            target.write_bytes(backup)
        except OSError:
            time.sleep(0.4)
            continue

        if sha(target) == expected_sha:
            return True
        time.sleep(0.4)

    print(f"!! 复原失败（文件被占用？）：{target}")
    return False


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
        if failed == -2:
            print(f"!!  变异 [{label}] -> TIMEOUT（守卫挂死，不是跑红）")
            return False
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


def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— D-096 五条当前成立")

    # ── ① 打赢不缴获材料（奖励反向）⇒ 必须红 ──────────────────────────
    results.append(mutate(
        "打赢不缴获材料（奖励符号反了）",
        TEMPLATES,
        """                    consequences["bandit_raid/material"] = new WorldEventEffect(
                        WorldEventEffect.KindMaterial, values.MaterialReward,""",
        """                    consequences["bandit_raid/material"] = new WorldEventEffect(
                        WorldEventEffect.KindMaterial, -values.MaterialReward,   // 变异：奖励变罚""",
    ))

    # ── ② 打赢不给金币 ⇒ 必须红 ────────────────────────────────────────
    results.append(mutate(
        "打赢不给金币（缴获只剩材料）",
        TEMPLATES,
        """                    consequences["bandit_raid/gold"] = new WorldEventEffect(
                        WorldEventEffect.KindGold, values.GoldReward,""",
        """                    consequences["bandit_raid/gold"] = new WorldEventEffect(
                        WorldEventEffect.KindGold, 0L,   // 变异：不给金币""",
    ))

    # ── ③ 打输也扣金币（被抢的应该是物资不是钱）⇒ 必须红 ────────────────
    results.append(mutate(
        "打输也扣金币（被抢的是货不是钱）",
        TEMPLATES,
        """                    consequences["bandit_raid/gold"] = new WorldEventEffect(
                        WorldEventEffect.KindGold, 0L, "金币没被抢走");""",
        """                    consequences["bandit_raid/gold"] = new WorldEventEffect(
                        WorldEventEffect.KindGold, -values.MaterialLoss, "金币没被抢走");   // 变异：钱也被抢""",
    ))

    # ── ④ 没人出击也算赢 ⇒ 必须红 ──────────────────────────────────────
    results.append(mutate(
        "没人出击也算赢（白捡一份缴获）",
        TEMPLATES,
        """                    consequences["bandit_raid/defended"] = new WorldEventEffect(
                        WorldEventEffect.KindNone, 0L, "被抢");""",
        """                    consequences["bandit_raid/defended"] = new WorldEventEffect(
                        WorldEventEffect.KindNone, 0L, "被抢");
                    won = true;   // 变异：没人出击也算赢""",
    ))

    # ── ⑤ 阶段补上「后续影响」（形状与兽潮一样）⇒ 必须红 ────────────────
    results.append(mutate(
        "阶段补上「后续影响」（形状与兽潮一样）",
        TEMPLATES,
        """                WorldEventPhase.Settlement,    // 结算：缉获或被抢
                WorldEventPhase.Finished,      // 结束（没有「后续影响」阶段）""",
        """                WorldEventPhase.Settlement,    // 结算：缉获或被抢
                WorldEventPhase.Aftermath,     // 变异：多了一个阶段
                WorldEventPhase.Finished,      // 结束""",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
