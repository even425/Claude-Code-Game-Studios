# -*- coding: utf-8 -*-
"""D-120：把野兽步态速度登记进参数台账（production/qa/parameters.json）。

只做两件事：
1. parameters 表加 Main.Wildlife.cs :: WildlifeGaitSpeed（野兽步态速度，D-120 调慢）。
2. 写回时保持 JSON 结构与缩进（ensure_ascii=False, indent=2）。
"""
import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _utf8out  # noqa: F401  （Windows 控制台 GBK ⇒ 中文输出要转码）

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PATH = os.path.join(ROOT, "production", "qa", "parameters.json")

with io.open(PATH, "r", encoding="utf-8", newline="") as f:
    doc = json.load(f)

row = {
    "file": "Main.Wildlife.cs",
    "name": "WildlifeGaitSpeed",
    "kind": "参数",
    "status": "【建议值】D-120",
    "note": "野兽步态（走路起伏）的速度：原来调得偏高，看起来像在小跑 ⇒ 开发者反馈「移动得也太快」⇒ 调慢（数值见本行的 value）",
    "value": "0.15",
}

existing = {(r["file"], r["name"]) for r in doc["parameters"]}
key = (row["file"], row["name"])
if key in existing:
    for i, r in enumerate(doc["parameters"]):
        if (r["file"], r["name"]) == key:
            doc["parameters"][i] = row
else:
    doc["parameters"].append(row)

with io.open(PATH, "w", encoding="utf-8", newline="") as f:
    json.dump(doc, f, ensure_ascii=False, indent=2)
    f.write("\n")

print("OK parameters=%d" % len(doc["parameters"]))
