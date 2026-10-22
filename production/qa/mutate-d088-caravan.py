"""
对 D-088「商队货款：村库抽成 10% + 90% 记为销毁」的守卫做**变异看红**验证。

**它守的是三件事**：
  ① 货款**分两路**：村库抽成 10% / 其余 90% 记为销毁
  ② **两笔加起来正好是买家付的钱**（钱没有凭空多，也没有凭空少）
  ③ **累计销毁要能累加**（守恒律的「累计销毁」那一项要对得上）

⚠️ **为什么这一档最要紧**：商队是**外面来的钱**。
若这一笔的记账错了 ⇒ 守恒律要么**假绿**（钱消失了但等式仍然成立 ⇒ 掩盖了真问题），
要么**假红**（每次标定都失败 ⇒ 大家以为经济坏了，其实是账记错了）。

用法：python production/qa/mutate-d088-caravan.py
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
FILTER = "FullyQualifiedName~CaravanMarketTests"

SERVICE = ROOT / "src" / "Application" / "CaravanMarketService.cs"


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
    for attempt in range(3):
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
    print("OK  基线（未变异）绿 —— D-088 三条当前成立")

    # ── ① 抽成 100%（钱全进村库 ⇒ 没有销毁）⇒ 必须红 ──────────────────
    results.append(mutate(
        "抽成 100%（钱全进村库，没有销毁）",
        SERVICE,
        "    public const int VillageCutPercent = 10;",
        "    public const int VillageCutPercent = 100;   // 变异：全进村库",
    ))

    # ── ② 抽成 0%（钱全销毁，村库拿不到）⇒ 必须红 ────────────────────
    results.append(mutate(
        "抽成 0%（钱全销毁，村库拿不到）",
        SERVICE,
        "    public const int VillageCutPercent = 10;",
        "    public const int VillageCutPercent = 0;   // 变异：全销毁",
    ))

    # ── ③ 不记累计销毁（钱消失了但账上没记 ⇒ 守恒律假红）⇒ 必须红 ────
    results.append(mutate(
        "不记累计销毁（钱消失但账上没记）",
        SERVICE,
        "        TotalDestroyedGold += destroyed;",
        "        // 变异：不记销毁",
    ))

    # ── ④ 销毁额算错（抽成与销毁加起来 ≠ 买家付的钱）⇒ 必须红 ────────
    results.append(mutate(
        "销毁额算错（两笔加起来 ≠ 买家付的钱）",
        SERVICE,
        "        long destroyed = totalAmount - villageCut;",
        "        long destroyed = villageCut;   // 变异：算错",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
