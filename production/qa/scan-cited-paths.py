"""扫一遍：活文档里引用的**所有**仓库路径，是不是都在版本库里。

`DocCitationTests` 目前只查 `tests/...cs` 那一种引用。
但「本机有、干净检出里没有」这个坑不限于测试文件 ——
截图、配置、文档都可能是「只存在于作者机器上」的东西。
（真事：`06` 引用的 `tests/_spike_test.cs` 被 `.gitignore` 排除 ⇒ CI 上不存在。）

用法：python production/qa/scan-cited-paths.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

LIVING = [
    "production/06_第一阶段验收与测试计划_V1.0.md",
    "production/36_当前实现参数清单_V1.0.md",
    "production/qa/README.md",
    "production/07_技术决策与待确认事项.md",
]

# 反引号里像仓库路径的东西：至少一段目录 + 有扩展名
PATH_RE = re.compile(r"`((?:production|src|tests|config|docs|design|\.claude)/[A-Za-z0-9_./\-]*\.[A-Za-z0-9]+)`")


def git_ignored(rel: str) -> bool:
    p = subprocess.run(["git", "check-ignore", "-q", "--", rel],
                       cwd=ROOT, capture_output=True)
    return p.returncode == 0


def tracked(rel: str) -> bool:
    p = subprocess.run(["git", "ls-files", "--error-unmatch", "--", rel],
                       cwd=ROOT, capture_output=True)
    return p.returncode == 0


def main() -> int:
    missing, ignored, untracked = [], [], []
    checked = 0

    for name in LIVING:
        path = ROOT / name
        if not path.exists():
            print(f"!! 活文档不存在：{name}")
            continue
        text = path.read_text(encoding="utf-8")
        for m in PATH_RE.finditer(text):
            rel = m.group(1)
            if rel in {c[0] for c in missing + ignored + untracked}:
                continue
            checked += 1
            if (ROOT / rel).exists():
                if not tracked(rel):
                    untracked.append((rel, name))
                continue
            if git_ignored(rel):
                ignored.append((rel, name))
            else:
                missing.append((rel, name))

    print(f"扫到 {checked} 条路径引用")
    print(f"\n== 完全不存在（既不在磁盘、也不在版本库、也没被 gitignore）== {len(missing)}")
    for rel, doc in missing:
        print(f"  {rel}   （{doc}）")
    print(f"\n== 在磁盘上但**不在版本库**（且没被 gitignore ⇒ 会随删除消失）== {len(untracked)}")
    for rel, doc in untracked:
        print(f"  {rel}   （{doc}）")
    print(f"\n== 被 .gitignore 排除（⇒ 本机工具，干净检出里没有）== {len(ignored)}")
    for rel, doc in ignored:
        print(f"  {rel}   （{doc}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
