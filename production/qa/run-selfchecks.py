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
                    "SAVETEST MT-04 PASS",
                    # D-104：试炼塔的**永久**生命加成必须真的读回来。
                    "SAVETEST 0.10 PASS"],
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
    {
        # D-088：商队成交在**真实游戏**里第一次跑一笔
        # （标定里商队成交一直是 0 笔 ⇒ 这条规则只被单测验过）。
        "name": "caravan",
        "args": ["--caravan"],
        "require": ["CARAVANTEST 钱对=True 货对=True ⇒ PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        # D-096：盗匪来袭（第三个大事件实例）在**真实游戏**里跑一次
        # （村库材料被抢 / 金币不动 / 幂等 —— 单测验不到接线层）。
        "name": "bandit",
        "args": ["--bandit"],
        "require": ["BANDITTEST 必败=True 材料对=True 金币对=True 扣减不超量=True 幂等=True ⇒ PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        # D-099：层级附带建筑（井）在**真实游戏**里读档一次
        # （老档补记 / 入档 / 不重复 —— 接线层，单测验不到）。
        "name": "tier",
        "args": ["--tier"],
        "require": ["TIERTEST 层级附带建筑（井 + 铁匠铺）⇒ PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        # D-101：大事件**真的走到结算**了吗（走真实日循环路径 ——
        # 单测与旧自检都是手工推进 ⇒ 绕开了那道闸门 ⇒ 真实游戏里一直是死的）。
        "name": "event",
        "args": ["--event"],
        "require": ["EVENTTEST 大事件真的会结算 ⇒ PASS"],
        "forbid": ["FAIL"],
        "zeros": [],
    },
    {
        # D-093：玩家自己选落点（接线层，单测验不到）。
        "name": "build",
        "args": ["--build"],
        "require": ["BUILDTEST 落点由玩家定 ⇒ PASS", "BUILDTEST 建筑长得不一样 ⇒ PASS"],
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

# 自检默认用**固定种子** ⇒ 每次运行的结果**可复现** ⇒ CI 不会偶发红。
#
# ⚠️ **为什么需要**：自检的随机源来自**英雄生成**（`HeroFactory.Seed`），
# 每次运行的英雄构成（名字 / 世界 / 性格 / 属性）都不同
# ⇒ 某些构成会让某个自检失败。
# 真事（2026-10-21）：CI 的 run #20 绿、**#21 红**、#22 绿，而 #21 那个提交
# **只改了一处 README** ⇒ 与代码无关 ⇒ **偶发**。
# ⇒ 固定种子把「偶发」变成「确定」：同一个种子，7 个自检的判定**逐条一致**（实测）。
#
# ⚠️ **取舍**：固定种子意味着真机自检只覆盖**一组**英雄构成。
# **组合覆盖由单元测试负责** —— 它自己造数据、不需要真机、且能一次覆盖很多组。
# 想跑随机构成用 `--seed=random`。
DEFAULT_SEED = "20261021"


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


# ── GitHub Actions 的机器可读输出 ──────────────────────────────────────
#
# **为什么需要它**：CI 的 job 日志**要登录才能看**（`/logs` 端点还要求 admin）
# ⇒ 对自动化环境来说，「CI 红了」等于「只知道红了，**不知道哪一条、为什么**」。
# 实测：run #21 的 Godot 作业红了，而我能读到的 annotation 只有一句
# 「Process completed with exit code 1.」—— **零信息量**。
#
# 而 workflow command（`::error::`）会被 runner 转成 **check-run annotation**，
# 公开仓库用**无认证的 API** 就能读到 ⇒ 人一眼看到原因，机器也能直接取到。
#
# ⚠️ message 必须**单行**：换行会被截断，甚至可能注入新的 workflow command。
def _gh_command(kind: str, title: str, message: str) -> None:
    """在 GitHub Actions 上输出一条机器可读的 annotation（不在 Actions 里就是空操作）。"""
    if not os.environ.get("GITHUB_ACTIONS"):
        return
    flat = " ".join(str(message).split())
    # workflow command 的转义规则：% → %25，\r → %0D，\n → %0A
    flat = flat.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
    title = " ".join(str(title).split()).replace("%", "%25")
    print(f"::{kind} title={title}::{flat}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="",
                    help="只跑这几个（逗号分隔的名字）")
    ap.add_argument("--timeout", type=int, default=300, help="每个自检的超时（秒）")
    ap.add_argument("--summary", default="",
                    help="把结果写成 Markdown 摘要（CI 传 $GITHUB_STEP_SUMMARY）")
    ap.add_argument("--seed", default="",
                    help=f"给每个自检传 --seed=N（默认用固定种子 {DEFAULT_SEED} ⇒ 结果可复现；"
                         "传 random 则每次运行都不同 —— 那正是 CI 偶发红灯的来源）")
    ap.add_argument("--self-test-annotation", action="store_true",
                    help="自测两条通道：annotation 与 summary（走与真实运行**同一条**代码路径），不跑自检")
    ap.add_argument("--prices", default="",
                    help="给每个自检传 --prices=<路径>（用**另一张价格表**跑自检）—— "
                         "用途：证明「改价格 ⇒ 自检仍然全绿」。⚠️ 该文件是**外部配置**，"
                         "游戏会优先用它（`Main.LoadPriceBook` 的查找顺序 ①）")
    args = ap.parse_args()

    # 自测开关：走**同一条** _gh_command / _write_summary 代码路径 ⇒ 它们坏了这里就会坏
    # （不是「另写一份演示代码」那种自欺）
    if args.self_test_annotation:
        # 故意让消息**带换行**：workflow command 是单行的，
        # 换行必须被**折叠成空格**（否则 GitHub 会截断，甚至可能注入新的命令）
        _gh_command("error", "自检通道自测", "第一行\n第二行")
        _gh_command("notice", "自检通道自测", "这是一条用于验证 annotation 通道的样例通知")
        if args.summary:
            _write_summary(Path(args.summary),
                           [("sample", False, "自测样例（故意失败）")],
                           ("<未跑>", "<未跑>"), ("<未跑>", "<未跑>"),
                           ["sample: 自测样例（故意失败）"])
        return 0

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
    results = []
    # 默认固定种子 ⇒ 确定性；--seed=random 才是随机构成（见 DEFAULT_SEED 的说明）
    if args.seed.lower() == "random":
        seed_args = []
    else:
        seed_args = [f"--seed={args.seed or DEFAULT_SEED}"]
    for c in checks:
        argv = [godot, "--headless", "--path", str(ROOT), "--quit-after", FRAMES, "--"] \
               + c["args"] + [f"--savepath={tmp_save}"] + seed_args
        if args.prices:
            # ⚠️ **放在用户参数里**（`--` 之后）—— 游戏是从 `OS.GetCmdlineUserArgs()` 读它的。
            argv.append(f"--prices={args.prices}")
        try:
            proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                                  encoding="utf-8", errors="replace", timeout=args.timeout)
        except subprocess.TimeoutExpired:
            failures.append(f"{c['name']}: 超时（{args.timeout}s）")
            results.append((c["name"], False, f"超时（{args.timeout}s）"))
            print(f"  ✗ {c['name']:<10} 超时")
            _gh_command("error", f"真机自检失败：{c['name']}", f"超时（{args.timeout}s）")
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
            detail = "；".join(problems)
            failures.append(f"{c['name']}: " + detail)
            results.append((c["name"], False, detail))
            print(f"  ✗ {c['name']:<10} " + detail)
            # 让失败原因变成**公开可读**的 annotation（日志要登录才看得到）
            _gh_command("error", f"真机自检失败：{c['name']}", detail)
        else:
            results.append((c["name"], True, ""))
            print(f"  ✓ {c['name']:<10} PASS")

    after = sha_and_mtime(PLAYER_SAVE)
    print()
    print(f"玩家存档跑之后: {after[0][:16]}… mtime={after[1]}")
    save_untouched = before == after
    if not save_untouched:
        failures.append("玩家存档被自检改动了 —— 自检必须用自己的端口")
        print("  ✗ 玩家存档被改动！")
        _gh_command("error", "真机自检失败：玩家存档",
                    "玩家存档被自检改动了 —— 自检必须用自己的端口")
    else:
        print("  ✓ 玩家存档未被改动")

    # 成功也给一条：这样「CI 上的自检跑到哪一步了」**不用登录日志**也能知道
    if not failures:
        _gh_command("notice", "真机自检",
                    f"全部 {len(checks)} 个自检通过，且未碰玩家存档")

    if args.summary:
        _write_summary(Path(args.summary), results, before, after, failures)

    print()
    if failures:
        print(f"!! 自检失败 {len(failures)} 项：")
        for f in failures:
            print("   ", f)
        _gh_command("error", "真机自检失败汇总", "；".join(failures))
        return 1
    print(f"✅ 全部 {len(checks)} 个自检通过，且未碰玩家存档")
    return 0


def _write_summary(path: Path, results, before, after, failures) -> None:
    """把结果写成 Markdown 摘要（CI 传 $GITHUB_STEP_SUMMARY ⇒ 网页上直接看得到）。"""
    lines = ["### 真机自检（Godot headless）", ""]
    lines.append("| 自检 | 结果 |")
    lines.append("|---|---|")
    for name, ok, detail in results:
        lines.append(f"| `{name}` | {'✅ PASS' if ok else '❌ FAIL — ' + detail} |")
    lines.append("")
    lines.append(f"玩家存档：{'未被改动' if before == after else '**被改动了**'}"
                 f"（SHA-256 `{after[0][:16]}…`）")
    if failures:
        lines.append("")
        lines.append(f"**失败 {len(failures)} 项**：")
        for f in failures:
            lines.append(f"- `{f}`")
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    except OSError as exc:
        print(f"!! 写摘要失败（不影响自检判定）：{exc}")


if __name__ == "__main__":
    sys.exit(main())
