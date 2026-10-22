"""
对导出预设守卫（`ExportPresetTests` + `export-check.py`）做**变异看红**验证。

它守的是「导出预设名**两处一致**」与「**Godot 真的认这个预设**」。

**为什么「Godot 认不认」需要真跑一次引擎**：静态检查只能证明
「两个文件里写着同一个字符串」，证明不了引擎认它。
⇒ 判据必须把两种失败**分开**：
  · 「预设找不到」（**Invalid export preset name** / Unknown export preset）⇒ **项目配错了** ⇒ 要红
  · 「找不到导出模板」（No export template found）⇒ **环境没准备好** ⇒ 不该红

四档变异：

  ① 判据里删掉「预设找不到」那个措辞 ⇒ 预设配错时**不再被发现** ⇒ 红
  ② 脚本不再真的调引擎（只读文件）⇒ 它什么也证明不了 ⇒ 红
  ③ `--self-test` 不再检查「错预设名会被拒」⇒ 判据失效也没人知道 ⇒ 红
  ④ `export_presets.cfg` 被 `.gitignore` 排除 ⇒ 干净克隆里没有它 ⇒ 红

用法：python production/qa/mutate-export-preset.py
"""
import hashlib
import os
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
FILTER = "FullyQualifiedName~ExportPresetTests"
CHECK = ROOT / "production" / "qa" / "export-check.py"
GITIGNORE = ROOT / ".gitignore"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
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

    # ── ① 判据里删掉「预设找不到」那个措辞 ───────────────────────────────
    #    预设**配错**时（名字写错、格式写错）就再也发现不了了。
    results.append(mutate(
        "判据里删掉「预设找不到」的措辞（预设配错时不再被发现）",
        CHECK,
        'WRONG_PRESET_MARKS = ("Unknown export preset", "Invalid export preset name")',
        'WRONG_PRESET_MARKS = ("",)',
    ))

    # ── ② 脚本不再真的调引擎 ───────────────────────────────────────────
    results.append(mutate(
        "脚本不再真的调引擎（只读文件 ⇒ 什么也证明不了）",
        CHECK,
        '    argv = [godot, "--headless", "--path", str(ROOT),\n'
        '            "--export-release", preset, str(out_path)]',
        '    argv = [godot, "--version"]  # 变异：不再真的导出',
    ))

    # ── ③ --self-test 不再检查「错预设名会被拒」 ─────────────────────────
    results.append(mutate(
        "--self-test 不再检查「错预设名会被拒」（判据失效也没人知道）",
        CHECK,
        "        if not says_wrong_preset(out):",
        "        if False:",
    ))

    # ── ④ export_presets.cfg 被 .gitignore 排除 ──────────────────────────
    #    干净克隆里没有它 ⇒ 预设名必然与 project.yaml 分叉。
    results.append(mutate(
        "export_presets.cfg 又被 .gitignore 排除（干净克隆里没有它 ⇒ 两处必然分叉）",
        GITIGNORE,
        "# （**导出模板**约 1.2 GB 仍在版本库之外 —— 那是引擎的运行时依赖，不是项目源码。）",
        "# （**导出模板**约 1.2 GB 仍在版本库之外 —— 那是引擎的运行时依赖，不是项目源码。）\n"
        "export_presets.cfg",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
