"""
对呈现断言 MT-04（存档重载后的可见状态）做**变异看红**验证。

MT-04 问的是：**读档之后，玩家在屏幕上看到的，和存档前看到的，是不是同一个世界**。
自检（`--save`）把界面上那几块**文字**拍成快照，存读档往返前后各拍一张，逐字比。

**为什么要单独一个变异脚本**：MT-04 活在 **Godot 真机自检**里，不在 `dotnet test` 里
⇒ 变异后必须**重新构建**再跑引擎（headless 加载的是 `.godot/mono/temp/bin/Debug/`）
⇒ 只构建 Release 时跑的是**旧代码**，症状是「跑通了、没报错、判据却没红」。

四档变异分别打「读档后还看得见的东西」：

  ① 读档不恢复模拟时间 → 顶栏还停在搅乱后的第 8 天
  ② 读档不恢复村庄层级 → 顶栏的村子名变了
  ③ 读档不恢复编成     → 花名册行首的「▸」标记没了、队伍栏回到「没派人」
  ④ 读档后**不刷新界面** → 状态全对，但屏幕上还是被搅乱的世界

**每一档都标了覆盖范围**：④ 是 MT-04 **独有**的覆盖（别处没人查界面）；
①② 会连带别的守卫一起红（**等价位**，不是漏判）——
  ① 顺带让 M4 红（M4 那条断言拿「当前天数」当参照，天数没恢复它跟着红）；
  ② 顺带让 0.6 红（0.6 直接断言村庄层级）。

**基线先绿**：脚本先在**未变异**的代码上跑一遍，要求 MT-04 PASS。
否则「变异后看到 FAIL」可能只是因为它**本来就红** —— 那这个矩阵就是在测空气。

用法：python production/qa/mutate-mt04-visible.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT_CANDIDATES = [
    r"C:\Tools\Godot\4.7.2\godot.exe",
]
SAVE_SRC = "src/Main.Save.cs"


def find_godot():
    for c in [os.environ.get("GODOT")] + GODOT_CANDIDATES:
        if c and Path(c).exists():
            return c
    from shutil import which
    found = which("godot")
    if found:
        return found
    raise SystemExit("!! 找不到 Godot 可执行文件（设 GODOT 环境变量或装到 C:\\Tools\\Godot\\4.7.2）")


GODOT = find_godot()


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def build_debug() -> bool:
    """重建 Debug —— **headless 加载的就是这一份**（只构建 Release 等于跑旧代码）。"""
    r = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"],
                       cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if r.returncode != 0:
        print(r.stdout[-3000:])
        print(r.stderr[-3000:])
        return False
    return True


def run_save_selftest(tmp_dir: Path):
    """跑一遍 `--save` 真机自检，返回 (stdout+stderr, 是否出现 MT-04 PASS)。

    帧数上限取 **60**：存档自检全在**第一帧**里同步跑完，
    而 `--quit-after 8000` 要跑满 8000 帧 ≈ 56 秒（变异矩阵要跑 4 遍 ⇒ 光等就 4 分钟）。
    「帧数够不够」这件事本身由**基线检查**兜底：MT-04 PASS 没打出来就是没跑完。
    """
    save_path = tmp_dir / "selftest-village-save.json"
    argv = [GODOT, "--headless", "--path", str(ROOT), "--quit-after", "60", "--",
            "--save", f"--savepath={save_path}"]
    proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=300)
    out = proc.stdout + proc.stderr
    return out, ("SAVETEST MT-04 PASS" in out)


def write_text(path: Path, text: str, crlf: bool):
    path.write_text(text.replace("\n", "\r\n") if crlf else text,
                    encoding="utf-8", newline="")


def mutate(label, rel_path, old, new, expect_only_mt04=False, note=""):
    """改一处 → 重建 → 跑自检 → 要求 MT-04 变红 → 复原文件并重建。

    返回 True 表示「符合预期」。
    """
    p = ROOT / rel_path
    backup = p.read_bytes()
    before = sha(p)
    raw = p.read_text(encoding="utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old_n = old.replace("\r\n", "\n")
    new_n = new.replace("\r\n", "\n")
    if old_n not in text:
        print(f"!! [{label}] 模式没找到")
        return False

    tmp_dir = Path(tempfile.mkdtemp(prefix="mt04-mutate-"))
    try:
        write_text(p, text.replace(old_n, new_n, 1), crlf)
        if not build_debug():
            print(f"!! [{label}] 变异后构建失败")
            return False

        out, passed = run_save_selftest(tmp_dir)
        if passed:
            print(f"!! 变异 [{label}] -> 仍是 GREEN（MT-04 PASS 还在）⇒ 守卫是空转的")
            for line in out.splitlines():
                if "MT-04" in line:
                    print("      " + line.strip()[:200])
            return False

        # 除了 MT-04 之外还有谁红了？（用来区分「独有覆盖」与「等价位」）
        others = sorted({m for m in re.findall(r"SAVETEST ([0-9A-Za-z.\-]+) FAIL", out)
                         if m != "MT-04"})
        detail = f"另有 {others} 也红" if others else "**只有 MT-04 红**（独有覆盖）"
        print(f"OK  变异 [{label}] -> RED（{detail}）")
        if note:
            print(f"      {note}")
        if expect_only_mt04 and others:
            print(f"!! [{label}] 预期只有 MT-04 红，实际还有 {others} 也红")
            return False
        return True
    finally:
        p.write_bytes(backup)
        if sha(p) != before:
            print(f"!! 复原失败 [{label}]")
        # 复原后必须重建 —— 否则磁盘上的 DLL 还是变异的，下一个人跑自检会看到假红。
        build_debug()


def main():
    results = []

    with tempfile.TemporaryDirectory(prefix="mt04-base-") as base_dir:
        out, passed = run_save_selftest(Path(base_dir))
        if not passed:
            print("!! 基线就是红的 —— 先修好再谈变异（否则矩阵在测空气）")
            for line in out.splitlines():
                if "MT-04" in line or "FAIL" in line:
                    print("      " + line.strip()[:200])
            return 1
        print("OK  基线（未变异）MT-04 PASS —— 判据确实能绿")
        if not build_debug():
            return 1

    # ── ① 读档不恢复模拟时间 ───────────────────────────────────────
    results.append(mutate(
        "读档后不恢复模拟时间（顶栏还停在搅乱后的第 8 天）",
        SAVE_SRC,
        "        _clock.SetSimTime(save.SimTime);",
        "        // 变异：读档不恢复模拟时间",
        note="等价位：M4 也会红（M4 那条断言拿「当前天数」当参照，天数没恢复它跟着红）。",
    ))

    # ── ② 读档不恢复村庄层级 ───────────────────────────────────────
    results.append(mutate(
        "读档后不恢复村庄层级（顶栏的村子名变了）",
        SAVE_SRC,
        "        _tier = save.VillageTier;",
        "        _tier = 0;  // 变异：读档不恢复村庄层级",
        note="等价位：0.6 也会红（0.6 直接断言村庄层级）。",
    ))

    # ── ③ 读档不恢复编成 ───────────────────────────────────────────
    results.append(mutate(
        "读档后不恢复编成（花名册的「▸」标记没了、队伍栏回到「没派人」）",
        SAVE_SRC,
        "            if (save.SquadMemberIds.Contains(_states[i].Definition.Id.Value))",
        "            if (false && save.SquadMemberIds.Contains(_states[i].Definition.Id.Value))",
        expect_only_mt04=True,
        note="MT-04 独有：自检里「读档后队伍（应为 …）」只是打印、不断言 ⇒ 别处没人抓。",
    ))

    # ── ④ 读档后不刷新界面（状态全对，屏幕还是旧世界）──────────────
    results.append(mutate(
        "读档后不刷新界面（状态全对，但屏幕上还是被搅乱的世界）",
        SAVE_SRC,
        "        Refresh();\n    }\n\n    /// <summary>\n    /// 存档里的摊位状态串",
        "        // 变异：读档后不刷新界面\n    }\n\n    /// <summary>\n    /// 存档里的摊位状态串",
        expect_only_mt04=True,
        note="MT-04 独有：这一档是 MT-04 存在的理由 —— 状态字段全对、别的断言全绿，"
             "而玩家看到的是上一个世界。",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
