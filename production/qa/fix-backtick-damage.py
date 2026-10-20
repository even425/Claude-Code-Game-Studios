"""
修复 07 文档里被 PowerShell 反引号转义污染的行。

**事故**：用 PowerShell 的双引号 here-string（@\"...\"@）写入 Markdown 时，
`` `0 `` 被当成**八进制转义**（`0` = NUL、`7` = BEL）⇒ 原文的 `` `07` `` 变成
「NUL + 7」，整行被污染。

**修法**：损坏模式高度一致 —— 「NUL + 数字 + 空格」本应是「反引号 + 0 + 数字 + 反引号 + 空格」。
所以规则是确定的：`` \x00([0-7]) `` ⇒ `` `0\1` ``。

跑完必须**零控制字符**（除 \\r \\n \\t）。
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOC = ROOT / "production" / "07_技术决策与待确认事项.md"

# NUL + 数字 + 空格  →  `0 + 数字 + ` + 空格
PATTERN = re.compile("\x00([0-7]) ")


def main():
    text = DOC.read_text(encoding="utf-8")
    before = sum(1 for c in text if ord(c) < 32 and c not in "\r\n\t")
    if before == 0:
        print("无需修复（无控制字符）")
        return 0

    fixed, n = PATTERN.subn(r"`0\1` ", text)
    left = [i + 1 for i, l in enumerate(fixed.split("\n"))
            if any(ord(c) < 32 and c not in "\t" for c in l)]

    if left:
        print(f"!! 仍有 {len(left)} 行含控制字符：{left[:10]}")
        for i in left[:5]:
            l = fixed.split("\n")[i - 1]
            print("   ", i, repr(l[:160]))
        return 1

    DOC.write_text(fixed, encoding="utf-8", newline="")
    print(f"修复 {n} 处（原有 {before} 个控制字符）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
