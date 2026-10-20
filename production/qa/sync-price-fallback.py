"""
把 **内置兜底价格表**同步成 `config/prices.json` 的内容。

**为什么需要它**：价格表是**同一份数据的两个副本** ——
`config/prices.json`（开发者改的那份）与 `PriceBook.Default`（配置读不到时的兜底）。
守卫 `PriceBookConfigConsistencyTests` 要求两者**严格相等**。

而**文档承诺的流程是「改一个文件 + 跑一条命令」**：
    改 `config/prices.json` → 跑 `production/qa/recalibrate.py`
可 `recalibrate.py` 的第二步就是 `dotnet test` —— 兜底没同步 ⇒ 守卫红 ⇒
**脚本当场抛错退出，采集根本跑不起来**。
（这不是假设：那条流程正是开发者拿到 T-007 的三个数之后要走的那条。）

所以：**先同步，再构建测试，再采集。** 这个脚本就是「同步」那一步。

用法：
    python production/qa/sync-price-fallback.py            # 同步
    python production/qa/sync-price-fallback.py --check    # 只报差异，不改文件
    python production/qa/sync-price-fallback.py --prices 别的.json

**它只改一个地方**：`src/Domain/Items/PriceBook.cs` 里 `PriceBook.Default` 那一块的
价格行。找不到那一块 / 找到不止一处 ⇒ **报错且不动文件**。
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PRICES = ROOT / "config" / "prices.json"
FALLBACK = ROOT / "src" / "Domain" / "Items" / "PriceBook.cs"

# `PriceBook.Default` 那一块：抓住头部、块体、尾部三段
# ⚠️ 尾部是 `}` `)` `;` —— 整个表达式是 `new PriceBook(new[] { … })`，
# 少写那个 `)` 就永远匹配不上（我第一版就踩了：块尾是 `});`，不是 `};`）。
BLOCK = re.compile(
    r"(public static PriceBook Default \{ get; \} = new PriceBook\(new\[\]\s*\n\s*\{)"
    r"(.*?)"
    r"(\n\s*\}\)\;\s*\n)",
    re.S)

# 块体里的一行价格
LINE = re.compile(
    r"new KeyValuePair<ItemId, long>\(new ItemId\(\"(?P<name>[^\"]+)\"\), (?P<price>-?\d+)L\)")


def read_prices(path: Path) -> dict:
    """读价格表（跳过 <c>_</c> 开头的注释键），并做合法性检查。"""
    raw = json.loads(path.read_text(encoding="utf-8"))
    items = {}
    for k, v in raw.items():
        if k.startswith("_"):
            continue
        if not isinstance(v, int) or isinstance(v, bool):
            raise ValueError(f"{k} = {v!r} 不是整数")
        if v < 0:
            raise ValueError(f"{k} = {v} 是负数（负价会被 PriceBook 拒绝）")
        if any(c in k for c in '"\\\n\r'):
            raise ValueError(f"物品名 {k!r} 含不能进 C# 源码的字符")
        items[k] = v
    if not items:
        raise ValueError("价格表里一个价格都没有")
    return items


def build_body(items: dict, indent: str) -> str:
    """按 JSON 里的**顺序**生成块体（顺序与开发者写下的顺序一致 ⇒ 可预期）。"""
    rows = [f'{indent}new KeyValuePair<ItemId, long>(new ItemId("{name}"), {price}L),'
            for name, price in items.items()]
    return "\n" + "\n".join(rows)


def sync(items: dict) -> str | None:
    """同步；返回「改了哪些」的说明，没改则返回 None。"""
    # ⚠️ newline="" —— 读进来时**不做**换行翻译，写回去时**也不做**：
    # 否则一个 CRLF 的文件会被这个脚本悄悄改成 LF（行尾漂移，最难查的那类改动）。
    text = FALLBACK.read_text(encoding="utf-8", newline="")
    matches = list(BLOCK.finditer(text))
    if len(matches) != 1:
        raise SystemExit(
            f"!! 在 {FALLBACK} 里找到 {len(matches)} 处 `PriceBook.Default` 块"
            "（要求恰好 1 处）—— 不动文件。")

    m = matches[0]
    old_body = m.group(2)
    first = LINE.search(old_body)
    indent = "        "
    if first:
        line_start = old_body.rfind("\n", 0, first.start())
        indent = old_body[line_start + 1:first.start()]

    old_items = {mm.group("name"): int(mm.group("price")) for mm in LINE.finditer(old_body)}
    new_body = build_body(items, indent)
    if old_items == items:
        return None

    new_text = text[:m.start(2)] + new_body + text[m.end(2):]
    FALLBACK.write_text(new_text, encoding="utf-8", newline="")
    return "、".join(
        f"{k}: {old_items.get(k, '(新增)')} → {v}" for k, v in items.items()
        if old_items.get(k) != v)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--prices", default=str(PRICES), help="价格表路径")
    ap.add_argument("--check", action="store_true", help="只报差异，不改文件")
    args = ap.parse_args()

    prices = Path(args.prices)
    try:
        items = read_prices(prices)
    except Exception as exc:
        print(f"!! 读价格表失败：{exc}")
        return 1

    print(f"价格表    : {prices}")
    print(f"要同步的  : " + " ".join(f"{k}={v}" for k, v in items.items()))

    if args.check:
        text = FALLBACK.read_text(encoding="utf-8", newline="")
        m = BLOCK.search(text)
        if not m:
            print("!! 找不到 PriceBook.Default 块")
            return 1
        old = {mm.group("name"): int(mm.group("price"))
               for mm in LINE.finditer(m.group(2))}
        if old == items:
            print("✅ 两份副本已经一致")
            return 0
        print("!! 两份副本不一致：")
        for k, v in items.items():
            if old.get(k) != v:
                print(f"     {k}: 兜底 {old.get(k, '(缺失)')} ≠ 配置 {v}")
        return 1

    try:
        changed = sync(items)
    except SystemExit as exc:
        print(exc)
        return 1

    if changed is None:
        print("✅ 两份副本已经一致（没动文件）")
    else:
        print(f"✅ 已同步内置兜底：{changed}")
        print("   （记得把 src/Domain/Items/PriceBook.cs 一起提交）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
