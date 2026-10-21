"""
导出检查 —— 证明「导出预设接对了」，而**不需要** 1.2 GB 的导出模板。

**它验的是什么**
--------------------------------------------------------------------------------
Godot 导出失败时会说两种**完全不同**的话：

  · `Unknown export preset`            ⇒ **项目配错了**（预设名/格式不对）—— 要红
  · `No export template found`（中文：
    「在预期路径处未找到导出模板」）    ⇒ **环境没准备好**（模板没装）—— 不该红

把这两者**分清**，靠的是**真的跑一次导出** —— 静态检查只能证明
「两个文件里写着同一个字符串」，证明不了 **Godot 认这个预设**。

**为什么需要 `--self-test`**
--------------------------------------------------------------------------------
「没看到 Unknown export preset」本身**不能**证明什么 —— 也许 Godot 换了措辞，
也许引擎根本没启动。⇒ `--self-test` **故意用一个错的预设名**跑一次，
断言 Godot **确实**说了「Unknown export preset」：
⇒ 这样「没看到它」才真的意味着「预设是对的」。

**它不做什么**
--------------------------------------------------------------------------------
它**不**要求导出成功（那需要 1.2 GB 的模板，属于环境准备，不是项目配置）。
它**不**改仓库里的任何文件。

**用法**
--------------------------------------------------------------------------------
    python production/qa/export-check.py            # 用真预设名跑一次
    python production/qa/export-check.py --self-test # 证明判据本身有效
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PRESETS = ROOT / "export_presets.cfg"

GODOT_CANDIDATES = [
    os.environ.get("GODOT"),
    r"C:\Tools\Godot\4.7.2\godot.exe",
]

# 「预设找不到」与「模板找不到」这两句话的判据。
#
# ⚠️ **措辞要跟引擎版本走**：Godot 4.7.2 的原话是
#    「Invalid export preset name: <名字>. The following presets were detected …」
#    —— **不是** 4.x 早期文档里的 "Unknown export preset"。
#    ⇒ 两个措辞都认；而且 `--self-test` 会**当场**验证当前引擎到底说哪句
#    ⇒ 引擎换措辞时，自测会红，而不是让「没看到错」变成假通过。
WRONG_PRESET_MARKS = ("Unknown export preset", "Invalid export preset name")
MISSING_TEMPLATE_MARKS = ("export template", "导出模板")


def says_wrong_preset(out: str) -> bool:
    return any(m in out for m in WRONG_PRESET_MARKS)


def find_godot() -> str:
    for c in GODOT_CANDIDATES:
        if c and Path(c).exists():
            return c
    from shutil import which
    found = which("godot")
    if found:
        return found
    raise SystemExit(
        "!! 找不到 Godot 可执行文件（设 GODOT 环境变量或装到 C:\\Tools\\Godot\\4.7.2）")


def preset_name() -> str:
    if not PRESETS.exists():
        raise SystemExit(f"!! 没有 {PRESETS} ⇒ 预设根本没配")
    m = re.search(r'^\s*name\s*=\s*"([^"]+)"', PRESETS.read_text(encoding="utf-8"), re.M)
    if not m:
        raise SystemExit("!! export_presets.cfg 里找不到 name=")
    return m.group(1)


def run_export(godot: str, preset: str) -> tuple[int, str]:
    """跑一次导出，返回 (退出码, 全部输出)。**不写任何产物**（模板缺失时导出在打包前就中止）。"""
    out_path = ROOT / "build" / "windows" / "__export_probe.exe"
    argv = [godot, "--headless", "--path", str(ROOT),
            "--export-release", preset, str(out_path)]
    proc = subprocess.run(argv, cwd=str(ROOT), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true",
                    help="故意用错的预设名跑一次，证明 Godot 确实会说「Unknown export preset」")
    args = ap.parse_args()

    godot = find_godot()
    real = preset_name()

    if args.self_test:
        wrong = real + "-__deliberately_wrong__"
        code, out = run_export(godot, wrong)
        if not says_wrong_preset(out):
            print(f"!! 自测失败：故意用错的预设名「{wrong}」跑，"
                  f"Godot 却**没有**说「预设找不到」这类话。")
            print("   ⇒ 判据本身失效（引擎可能换了措辞，或根本没跑起来）")
            print(out[-2000:])
            return 1
        said = [m for m in WRONG_PRESET_MARKS if m in out]
        print(f"OK  自测通过：错的预设名「{wrong}」⇒ Godot 确实说了「{said[0]}」")
        print("   ⇒ 因此「没看到这句话」就真的意味着「预设是对的」")
        return 0

    code, out = run_export(godot, real)
    if says_wrong_preset(out):
        print("!! 导出报「预设找不到」⇒ **预设名或格式不对**")
        print(out[-2000:])
        return 1

    missing = any(m in out for m in MISSING_TEMPLATE_MARKS)
    if missing:
        print(f"OK  导出命令与预设**对上了**（Godot 报的是「找不到导出模板」，不是「预设找不到」）。")
        print("   ⇒ 剩下的是**环境**问题：需要 Godot 的导出模板"
              f"（约 1.2 GB → %APPDATA%\\Godot\\export_templates\\4.7.2.stable.mono\\）")
        print(f"   预设名：{real} · 退出码 {code}（预期非 0：模板缺失）")
        return 0

    # 两种错都没出现 ⇒ 可能导出**成功**了（装了模板），也可能出了别的问题。
    if code == 0:
        print(f"OK  导出**成功**（预设 {real}）—— 本机已装导出模板。")
        print("   ⇒ 「可以正常构建」这条验收项在本机成立。")
        return 0

    print("!! 导出既没报「预设找不到」、也没报「模板找不到」，却失败了 —— 说明还有别的问题：")
    print(out[-2000:])
    return 1


if __name__ == "__main__":
    sys.exit(main())
