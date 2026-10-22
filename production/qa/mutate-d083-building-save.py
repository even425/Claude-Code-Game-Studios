"""
对 D-083「建筑入档」的守卫做**变异看红**验证。

**它守的是三件事**：
  ① 已建成的建筑，读档后**还在**（围栏/试炼塔不能凭空消失）
  ② **影响区**跟着走（围栏没了摆摊范围 = 白盖了）
  ③ 在建的建筑读档后**会继续推进**（不能永远停在 40%）

⚠️ **最要紧的一档**是「建筑列表不存」——
那正是原来的实现（`Buildings = new List<BuildingSave>()`）⇒
读档后玩家花 150 金 + 70 材料 + 工期盖的试炼塔**凭空消失**。

用法：python production/qa/mutate-d083-building-save.py
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
FILTER = "FullyQualifiedName~BuildingSaveTests|FullyQualifiedName~SaveVersion09Tests"

SAVE = ROOT / "src" / "data" / "BuildingPersistence.cs"
SERVICE = ROOT / "src" / "Application" / "BuildingService.cs"
SAVEGAME = ROOT / "src" / "data" / "SaveGame.cs"


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
    print("OK  基线（未变异）绿 —— D-083 三条当前成立")

    # ── ① 建筑列表不存 ⇒ 读档后建筑凭空消失 ⇒ 必须红 ─────────────────────
    # ⚠️ **变异必须能编译**：直接删掉整段会留下孤儿代码 ⇒ 编译失败 ⇒
    # 脚本会数到「0 个失败」而不是「红」⇒ **假通过**。
    # ⇒ 改成「什么都不导出」但**语法完整**（`Where(_ => false)`）。
    results.append(mutate(
        "建筑列表不存（读档后建筑凭空消失）",
        SAVE,
        "        return village.Buildings.Select(b => new BuildingSave",
        "        return village.Buildings.Where(_ => false).Select(b => new BuildingSave   // 变异：什么都不存",
    ))

    # ── ② 影响区不存 ⇒ 围栏没了摆摊范围 ⇒ 必须红 ────────────────────────
    results.append(mutate(
        "影响区不存（围栏没了摆摊范围）",
        SAVE,
        "            AffectsAreaW = b.AffectsAreaW,\n            AffectsAreaH = b.AffectsAreaH,",
        "            AffectsAreaW = 0,\n            AffectsAreaH = 0,",
    ))

    # ── ③ 读档不恢复「施工中」⇒ 那栋楼永远停住 ⇒ 必须红 ─────────────────
    results.append(mutate(
        "读档不恢复施工状态（那栋楼永远停住）",
        SERVICE,
        "            _constructions[instance.Id.Value] = new Construction(",
        "            // 变异：不登记施工状态\n            if (false) _constructions[instance.Id.Value] = new Construction(",
    ))

    # ── ④ 校验器不查建筑字段 ⇒ 坏存档也能读 ⇒ 必须红 ────────────────────
    results.append(mutate(
        "校验器不查建筑字段",
        SAVEGAME,
        "            if (double.IsNaN(b.Progress) || b.Progress < 0.0 || b.Progress > 1.0)",
        "            if (false)",
    ))

    # ── ⑤ 存档版本不升（0.8 的档也能读）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "存档版本不升（0.8 的旧档也能读）",
        SAVEGAME,
        "    public static SaveVersion Current => new(0, 9);",
        "    public static SaveVersion Current => new(0, 8);",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
