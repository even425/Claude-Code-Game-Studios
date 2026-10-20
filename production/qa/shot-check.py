"""
截一张真图，并检查它**不是一张空图** —— 把呈现断言 MT-01 变成可执行的。

**为什么需要它**：编码标准写着「A parse check is not a run」——
玩家能看到的东西，必须**真跑一遍并看图**。但「看图」这一步一直靠人。
本脚本把其中最机械的一层自动化：

  ① 真跑一遍引擎（带窗口，headless 没有渲染 ⇒ 截出来是空的）并截图；
  ② 纯 Python 解 PNG（不依赖 PIL）；
  ③ 断言画面**不是一整片纯色**、**颜色种类足够多**。

**它能抓到什么**：相机跑偏、节点没挂上、材质没加载、整屏黑屏、UI 全没画出来。
**它抓不到什么**：画面「对不对」（好不好看、位置合不合理）—— 那仍然要人看。
**所以它不是 MT-01 的全部，只是它能自动的那一半**；
人工判读的部分由截图本身作为证据留存。

用法：python production/qa/shot-check.py [--keep <输出路径>]
"""
import argparse
import struct
import subprocess
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT = r"C:\Tools\Godot\4.7.2\godot.exe"
EVIDENCE = ROOT / "production" / "qa" / "evidence"

# 判据（阈值是**看图门槛**，不是设计值）
MIN_DISTINCT_COLORS = 20      # 少于这个数 ⇒ 画面上几乎只有底色
MAX_SINGLE_COLOR_SHARE = 0.90  # 单一颜色占比超过这个数 ⇒ 整屏一色


def decode_png(path: Path):
    """极简 PNG 解码：返回 (width, height, 颜色计数)。只支持 8 位 RGB/RGBA。"""
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("不是 PNG")

    pos = 8
    width = height = 0
    bitdepth = colortype = 0
    idat = bytearray()
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        ctype = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length

        if ctype == b"IHDR":
            width, height, bitdepth, colortype = struct.unpack(">IIBB", body[:10])
        elif ctype == b"IDAT":
            idat += body
        elif ctype == b"IEND":
            break

    if bitdepth != 8 or colortype not in (2, 6):
        raise ValueError(f"只支持 8 位 RGB/RGBA（实际 bitdepth={bitdepth} colortype={colortype}）")

    channels = 3 if colortype == 2 else 4
    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    prev = bytearray(stride)
    colors = {}
    offset = 0

    for _ in range(height):
        f = raw[offset]
        offset += 1
        line = bytearray(raw[offset:offset + stride])
        offset += stride

        # 反过滤（PNG 的 5 种过滤器）
        if f == 1:
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif f == 3:
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif f == 4:
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = prev[i]
                c = prev[i - channels] if i >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 0xFF

        prev = line
        for i in range(0, stride, channels):
            key = bytes(line[i:i + 3])   # 忽略 alpha
            colors[key] = colors.get(key, 0) + 1

    return width, height, colors


def encode_png(width, height, pixels):
    """极简 PNG 编码（只为自检造测试图）：pixels 是逐行的 RGB 字节串。"""
    def chunk(tag, body):
        return (struct.pack(">I", len(body)) + tag + body
                + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF))

    raw = b"".join(b"\x00" + row for row in pixels)
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw))
            + chunk(b"IEND", b""))


def self_test():
    """**防空转自检**：证明判据真能抓出「整屏一色」的空图。

    没有这一步，这个脚本可能一直「通过」——因为它读的是**真截图**，
    而真截图**几乎不会**变成空图。那样这条检查就是个摆设。
    """
    import tempfile

    problems = []

    # ① 整屏一色 ⇒ 必须被判为「不通过」
    solid = encode_png(64, 64, [bytes([32, 46, 74]) * 64 for _ in range(64)])
    with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as f:
        f.write(solid)
        solid_path = Path(f.name)
    try:
        _, _, colors = decode_png(solid_path)
        total = 64 * 64
        distinct = len(colors)
        share = max(colors.values()) / total
        if distinct >= MIN_DISTINCT_COLORS or share <= MAX_SINGLE_COLOR_SHARE:
            problems.append(
                f"整屏一色没有被判为不通过（distinct={distinct}, share={share:.1%}）"
                " ⇒ 判据是空转的")
    finally:
        solid_path.unlink(missing_ok=True)

    # ② 只有两种颜色 ⇒ 也必须被判为「不通过」
    # （注意：夹具本身得真的只有两种颜色 —— 上一版用 `x*4 % 256` 造了 64 种，
    #   于是自检自己先失败了。这正是自检存在的意义：夹具错了它会喊。）
    stripe = (bytes([10, 10, 10]) * 32 + bytes([240, 240, 240]) * 32)
    few = encode_png(64, 64, [stripe] * 64)
    with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as f:
        f.write(few)
        few_path = Path(f.name)
    try:
        _, _, colors = decode_png(few_path)
        if len(colors) >= MIN_DISTINCT_COLORS:
            problems.append("少颜色图没有被判为不通过 ⇒ 判据是空转的")
    finally:
        few_path.unlink(missing_ok=True)

    # ③ 真正的画面（多色）⇒ 必须被判为「通过」
    rich_rows = []
    for y in range(64):
        row = bytearray()
        for x in range(64):
            row += bytes([(x * 4) % 256, (y * 4) % 256, (x * y) % 256])
        rich_rows.append(bytes(row))
    rich = encode_png(64, 64, rich_rows)
    with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as f:
        f.write(rich)
        rich_path = Path(f.name)
    try:
        _, _, colors = decode_png(rich_path)
        if len(colors) < MIN_DISTINCT_COLORS:
            problems.append("多色图没有被判为通过 ⇒ 判据过严")
    finally:
        rich_path.unlink(missing_ok=True)

    if problems:
        print("!! 自检不通过：")
        for p in problems:
            print("   ", p)
        return 1
    print("✅ 自检通过：整屏一色 ⇒ 判不通过；多色 ⇒ 判通过（判据不是空转的）")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep", default=str(EVIDENCE / "shot-check-latest.png"),
                    help="截图保存路径")
    ap.add_argument("--min-colors", type=int, default=MIN_DISTINCT_COLORS)
    ap.add_argument("--max-share", type=float, default=MAX_SINGLE_COLOR_SHARE)
    ap.add_argument("--self-test", action="store_true",
                    help="只跑判据自检（不截��）")
    args = ap.parse_args()

    if args.self_test:
        return self_test()

    out = Path(args.keep)
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()

    # ⚠️ 必须带窗口：headless 没有渲染，截出来是空图。
    # ⚠️ 引擎参数与用户参数之间必须有 `--`（否则 `--shot=` 会被引擎静默忽略）。
    res_path = "res://" + out.relative_to(ROOT).as_posix()
    proc = subprocess.run(
        [GODOT, "--path", str(ROOT), "--", f"--shot={res_path}"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
        timeout=180)

    if not out.exists():
        print("!! 没截到图。引擎输出：")
        print(proc.stdout[-2000:])
        print(proc.stderr[-2000:])
        return 1

    width, height, colors = decode_png(out)
    total = width * height
    distinct = len(colors)
    top_share = max(colors.values()) / total if total else 1.0

    print(f"截图        : {out.relative_to(ROOT)}（{width}x{height}）")
    print(f"颜色种类    : {distinct}（下限 {args.min_colors}）")
    print(f"单一色占比  : {top_share:.1%}（上限 {args.max_share:.0%}）")
    print(f"最常见颜色  : #{max(colors, key=colors.get).hex()}")

    problems = []
    if distinct < args.min_colors:
        problems.append(f"颜色种类只有 {distinct} —— 画面上几乎只有底色（黑屏 / 什么都没画）")
    if top_share > args.max_share:
        problems.append(f"单一颜色占 {top_share:.1%} —— 整屏一色")

    if problems:
        print("!! 不通过：")
        for p in problems:
            print("   ", p)
        return 1

    print("✅ 通过：画面不是空图（**这不等于画面正确** —— 好不好看仍要人看）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
