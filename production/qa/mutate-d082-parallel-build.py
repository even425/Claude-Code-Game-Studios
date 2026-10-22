"""
对 D-082「多栋同时施工」的守卫做**变异看红**验证。

**它守的是三件事**：
  ① **人力只算一次**（两栋同时盖 ⇒ 同一批人不能被算两遍）
  ② **分配是确定的**（同样的输入 ⇒ 同样的分配）
  ③ **两栋都在推进**（不是「先到先得」的排队）

⚠️ **最要紧的一档**是「人力被算两遍」——
原来每栋在建建筑都拿**全村**人力 ⇒ 围栏与试炼塔同时施工时
同一批人被算了两遍 ⇒ 人力凭空翻倍 ⇒ 两栋一起盖比一栋快一倍。

用法：python production/qa/mutate-d082-parallel-build.py
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
FILTER = "FullyQualifiedName~ParallelConstructionTests"

SERVICE = ROOT / "src" / "Application" / "BuildingService.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
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
    print("OK  基线（未变异）绿 —— D-082 三条当前成立")

    # ── ① 人力被算两遍（每栋都拿全村人力）⇒ 必须红 ───────────────────────
    results.append(mutate(
        "人力被算两遍（每栋都拿全部人力）",
        SERVICE,
        "            int workers = each + (i < rest ? 1 : 0);",
        "            int workers = availableWorkers;   // 变异：每栋都拿全村人力",
    ))

    # ── ② 余数给最后开工的（不是最早）⇒ 必须红 ──────────────────────────
    results.append(mutate(
        "余数给了最后开工的那栋",
        SERVICE,
        "            int workers = each + (i < rest ? 1 : 0);",
        "            int workers = each + (i >= ids.Length - rest ? 1 : 0);   // 变异",
    ))

    # ── ③ 分配时多给一个人（超过可用人力）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "分配时多给一个人（超过可用人力）",
        SERVICE,
        "            int workers = each + (i < rest ? 1 : 0);",
        "            int workers = each + 1;   // 变异：超发",
    ))

    # ── ④ 只推进第一栋（多栋不并行）⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "只推进第一栋（多栋不并行）",
        SERVICE,
        "        foreach (var id in _constructions.Keys.ToArray())",
        "        foreach (var id in _constructions.Keys.Take(1).ToArray())   // 变异",
    ))

    # ── ⑤ 进度不看分到的人（人手白分）⇒ 必须红 ──────────────────────────
    # ⚠️ **为什么不是「去掉『没人 ⇒ 停工』那行」**：0 人 × 小时 ÷ 工期 = 0 ⇒
    # 去掉那行**行为完全不变** ⇒ 那一档永远是绿 ⇒ 它抓不到任何东西。
    # ⇒ 变异点要挑**真的改变行为**的地方：**进度公式里去掉人数**。
    results.append(mutate(
        "进度不看分到的人（人手白分）",
        SERVICE,
        "            double progress = c.Workers * workedHours / c.Definition.BaseBuildHours;",
        "            double progress = workedHours / c.Definition.BaseBuildHours;   // 变异：不算人手",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
