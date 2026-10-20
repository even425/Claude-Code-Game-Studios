"""
真机自检跑批 —— 把 7 个自检开关一次跑完，并**证明它们没碰玩家的存档**。

**为什么需要它**：自检开关（`--save` / `--relations` / …）此前只存在于**对话里**
（我的临时脚本住在 `%TEMP%`），仓库里没有 ⇒ 别人跑不了、也没人监督它们。

**两件它做而人做不好的事**：
1. **玩家存档不许被碰**：跑之前记录玩家存档的 SHA-256 与时间戳，跑完再比 ——
   **不一样就是失败**。自检必须用**自己的端口**（`selftest-village-save.json`），
   而这条断言把「它确实没碰」从「我记得它没碰」变成了机器事实。
2. **自检不许静默通过**：每个自检都有**必需的 PASS 标记**与**禁止出现的 FAIL 标记**；
   少标记、多 FAIL、进程非零退出，都算失败。

用法：
  python production/qa/run-selfchecks.py            # 跑全部
  python production/qa/run-selfchecks.py --only save,trade
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import argparse
import hashlib
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT_CANDIDATES = [
    os.environ.get("GODOT"),
    r"C:\Tools\Godot\4.7.2\godot.exe",
]
PLAYER_SAVE = Path(os.environ.get("APPDATA", "")) / "Godot" / "app_userdata" / \
    "村长与冒险者" / "village-save.json"

# 每个自检：必需标记 / 禁止标记 / 必须出现的「计数为 0」片段
CHECKS = [
    {
        "name": "save",
        "args": ["--save"],
        "require": ["SAVETEST 0.6 PASS", "SAVETEST R-03 PASS", "SAVETEST M4 PASS",
                    "SAVETEST MT-04 PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        "name": "relations",
        "args": ["--relations"],
        "require": ["RELTEST PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        "name": "market",
        "args": ["--market"],
        "require": ["MARKETTEST PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        "name": "shop",
        "args": ["--shop"],
        "require": ["SHOPTEST PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        "name": "trade",
        "args": ["--trade"],
        "require": ["TRADETEST PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        "name": "events",
        "args": ["--events"],
        # 事件自检打的是 C1–C4 的**计数**，不是单一 PASS ⇒ 逐条断言计数为 0
        "require": ["EVTEST C1", "EVTEST C2", "EVTEST C3", "EVTEST C4"],
        "forbid": ["FAIL", "触发失败"],
        "zeros": ["无法匹配 0 条", "超过 1 条的事件 0 个", "空条目 0 条"],
    },
    {
        "name": "dungeon",
        "args": ["--dungeon"],
        "require": ["D-054-01 PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
]


# 每个自检跑多少帧。
#
# ⚠️ 这里曾经是 8000 —— 那是**纯浪费**：自检在**第一帧**里就同步跑完了，
# 剩下的 7999 帧什么也不做，只为了让进程自己退出。
# 于是每个自检 ≈ 56 秒、7 个 ≈ 6.5 分钟；runner 慢一点就是 20~35 分钟
# （CI 上实测到过 17 分钟还没跑完）。
#
# 实测（`production/qa/frame-budget.py`）：**600 帧全部命中**，每个 ≈ 4.6 秒；
# 120 帧也够（≈ 1.3 秒）。取 600 —— 留足余量，runner 慢也不怕，但已经快了 12 倍。
# ⚠️ 改这个数之前先跑 `frame-budget.py <帧数>` 量一遍，别凭感觉。
FRAMES = "600"


def find_godot():
    for c in GODOT_CANDIDATES:
        if c and Path(c).exists():
            return c
    found = shutil_which("godot")
    if found:
        return found
    raise SystemExit("!! 找不到 Godot 可执行文件（设 GODOT 环境变量或装到 C:\\Tools\\Godot\\4.7.2）")


def shutil_which(name):
    from shutil import which
    return which(name)


def sha_and_mtime(path):
    if not path.exists():
        return ("<不存在>", "<不存在>")
    h = hashlib.sha256(path.read_bytes()).hexdigest()
    return (h, path.stat().st_mtime)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="",
                    help="只跑这几个（逗号分隔的名字）")
    ap.add_argument("--timeout", type=int, default=300, help="每个自检的超时（秒）")
    args = ap.parse_args()

    godot = find_godot()
    checks = CHECKS
    if args.only:
        wanted = {s.strip() for s in args.only.split(",") if s.strip()}
        checks = [c for c in CHECKS if c["name"] in wanted]
        if not checks:
            print(f"!! 没有匹配的自检：{args.only}")
            return 2

    before = sha_and_mtime(PLAYER_SAVE)
    print(f"Godot       : {godot}")
    print(f"玩家存档    : {PLAYER_SAVE}")
    print(f"  跑之前    : {before[0][:16]}… mtime={before[1]}")
    print()

    # 自检一律用**自己的存档端口**，绝不写玩家存档
    tmp_dir = Path(tempfile.mkdtemp(prefix="selftest-"))
    tmp_save = tmp_dir / "selftest-village-save.json"

    failures = []
    for c in checks:
        argv = [godot, "--headless", "--path", str(ROOT), "--quit-after", FRAMES, "--"] \
               + c["args"] + [f"--savepath={tmp_save}"]
        try:
            proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                                  encoding="utf-8", errors="replace", timeout=args.timeout)
        except subprocess.TimeoutExpired:
            failures.append(f"{c['name']}: 超时（{args.timeout}s）")
            print(f"  ✗ {c['name']:<10} 超时")
            continue

        out = proc.stdout + proc.stderr
        problems = []
        if proc.returncode != 0:
            problems.append(f"退出码 {proc.returncode}")
        for marker in c["require"]:
            if marker not in out:
                problems.append(f"缺标记「{marker}」")
        for marker in c["forbid"]:
            if marker in out:
                problems.append(f"出现禁止标记「{marker}」")
        for marker in c["zeros"]:
            if marker not in out:
                problems.append(f"计数不对（缺「{marker}」）")

        if problems:
            failures.append(f"{c['name']}: " + "；".join(problems))
            print(f"  ✗ {c['name']:<10} " + "；".join(problems))
        else:
            print(f"  ✓ {c['name']:<10} PASS")

    after = sha_and_mtime(PLAYER_SAVE)
    print()
    print(f"玩家存档跑之后: {after[0][:16]}… mtime={after[1]}")
    if before != after:
        failures.append("玩家存档被自检改动了 —— 自检必须用自己的端口")
        print("  ✗ 玩家存档被改动！")
    else:
        print("  ✓ 玩家存档未被改动")

    print()
    if failures:
        print(f"!! 自检失败 {len(failures)} 项：")
        for f in failures:
            print("   ", f)
        return 1
    print(f"✅ 全部 {len(checks)} 个自检通过，且未碰玩家存档")
    return 0


if __name__ == "__main__":
    sys.exit(main())
