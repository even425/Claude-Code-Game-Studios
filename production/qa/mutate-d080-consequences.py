"""
对 D-080「大事件的具体后果」的守卫做**变异看红**验证。

**它守的是开发者拍板的四条**：
  ① 兽潮打输 ⇒ 只损资源（材料与金币扣掉），人畜无损
  ② 兽潮打赢 ⇒ 材料 + 金币
  ③ 兽潮的规模随村庄层级递增
  ④ 后果是**结构化**的（种类 + 数量）⇒ 落地层不解析中文

⚠️ **最要紧的一档**是「扣款不夹取」——
若村库里的材料不够却照扣，余额会被扣成负数 ⇒ 村库出现「负债」这个状态
⇒ 下一次结算会凭空扣掉负数、守恒律也会对不上。

用法：python production/qa/mutate-d080-consequences.py
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
FILTER = "FullyQualifiedName~WorldEventConsequenceTests"

APP = ROOT / "src" / "Application"
TEMPLATES = APP / "WorldEventTemplates.cs"
SCALE = APP / "BeastTideScale.cs"
APPLIER = APP / "WorldEventResourceApplier.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
    # ⇒ 变异改的是源码 ⇒ 每次都必须重新编译进程序集。
    try:
        r = subprocess.run(
            ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
             "--filter", FILTER],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=300, env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
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
    print("OK  基线（未变异）绿 —— D-080 四条当前成立")

    # ── ① 打赢不给资源（后果只剩说明）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "打赢不给资源（后果只剩说明）",
        TEMPLATES,
        """                        consequences["beast_tide/material"] = new WorldEventEffect(
                            WorldEventEffect.KindMaterial, values.MaterialReward,
                            $"兽潮守住：材料 +{values.MaterialReward}");""",
        """                        consequences["beast_tide/material"] = new WorldEventEffect(
                            WorldEventEffect.KindNone, 0L,
                            $"兽潮守住：材料 +{values.MaterialReward}");""",
    ))

    # ── ② 打输的符号反了（扣款变成发款）⇒ 必须红 ───────────────────────
    results.append(mutate(
        "打输反而发资源（符号反了）",
        TEMPLATES,
        """                        consequences["beast_tide/material"] = new WorldEventEffect(
                            WorldEventEffect.KindMaterial, -values.MaterialLoss,
                            $"兽潮没守住：材料 -{values.MaterialLoss}");""",
        """                        consequences["beast_tide/material"] = new WorldEventEffect(
                            WorldEventEffect.KindMaterial, values.MaterialLoss,
                            $"兽潮没守住：材料 -{values.MaterialLoss}");""",
    ))

    # ── ③ 规模不随层级递增（缩放恒等于 1）⇒ 必须红 ─────────────────────
    results.append(mutate(
        "规模不随层级递增（缩放恒为 1）",
        SCALE,
        "        if (tier <= 0) return baseStats;",
        "        return baseStats;   // 变异：不缩放",
    ))

    # ── ④ 扣款不夹取（照扣 ⇒ 村库被扣成负数）⇒ 必须红 ──────────────────
    results.append(mutate(
        "扣款不夹取（村库被扣成负数）",
        APPLIER,
        "            long actual = Math.Min(wanted, village.Get(resource));",
        "            long actual = wanted;   // 变异：不看余额",
    ))

    # ── ⑤ 回报不随层级放大（风险与回报不同步）⇒ 必须红 ─────────────────
    results.append(mutate(
        "回报不随层级放大",
        TEMPLATES,
        "        if (tier <= 0) return this;",
        "        return this;   // 变异：不放大",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
