"""量一下：每个真机自检**到底需要多少帧**才能把 PASS 标记打出来。

CI 现在用 `--quit-after 8000`（≈ 8000 帧），而自检其实在**第一帧**里就同步跑完了
⇒ 剩下的 7999 帧纯属白烧（每个自检 ≈ 56 秒 × 7 个 ≈ 6.5 分钟；runner 慢一点就是 20 分钟+）。

用法：python production/qa/frame-budget.py [帧数]
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT = r"C:\Tools\Godot\4.7.2\godot.exe"

CHECKS = [
    (["--save"], "SAVETEST 0.6 PASS"),
    (["--save"], "SAVETEST MT-04 PASS"),
    (["--relations"], "RELTEST PASS"),
    (["--market"], "MARKETTEST PASS"),
    (["--shop"], "SHOPTEST PASS"),
    (["--trade"], "TRADETEST PASS"),
    (["--events"], "EVTEST C1"),
    (["--dungeon"], "D-054-01 PASS"),
]


def main():
    frames = int(sys.argv[1]) if len(sys.argv) > 1 else 600
    tmp = Path(tempfile.mkdtemp(prefix="frame-budget-"))
    save = tmp / "selftest-village-save.json"

    ok = True
    for args, marker in CHECKS:
        t0 = time.time()
        proc = subprocess.run(
            [GODOT, "--headless", "--path", str(ROOT), "--quit-after", str(frames),
             "--"] + args + [f"--savepath={save}"],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=300)
        out = proc.stdout + proc.stderr
        hit = marker in out
        ok = ok and hit
        print(f"{'OK ' if hit else 'MISS'}  {' '.join(args):<12} 标记={marker:<22} "
              f"用时={time.time() - t0:5.1f}s")
        if not hit:
            print("      输出尾部：" + out[-400:].replace("\n", " | "))
    print("---")
    print(f"帧数上限 {frames}：{'全部命中' if ok else '有未命中'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
