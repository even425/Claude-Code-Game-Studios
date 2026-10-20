"""
让本目录的脚本在**任何**控制台编码下都能把话说出来。

**为什么需要它**：本机 Windows 控制台默认是 **GBK（cp936）**，
而脚本里满是「✓ / ✗ / ⇒ / ✅ / ▸」这类符号 —— **GBK 里没有它们**
⇒ `print` 直接抛 `UnicodeEncodeError` ⇒ **脚本在开发者自己的机器上跑不起来**。

这不是「显示不好看」的问题，是**守卫根本没跑**：
`run-selfchecks.py` 跑到**第一个通过项**就炸 ⇒ 后面 6 个自检一个没跑
⇒ 而它自己最后那句「全部 7 个自检通过」压根没机会打印出来。

**教训**：工具自己没被跑过，就等于没有工具。
所以每个脚本开头都 `import _utf8out`（本目录在 `sys.path[0]`，import 得到）。

用法：在脚本的 import 区加三行

    try:
        import _utf8out  # noqa: F401
    except ImportError:
        pass

`errors="replace"` 兜底：万一还有编不出来的字符，宁可印成「?」也别整段崩掉。
"""
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        # 老 Python / 已被替换成别的流 —— 那就退回默认编码，至少不会因此崩掉。
        pass
