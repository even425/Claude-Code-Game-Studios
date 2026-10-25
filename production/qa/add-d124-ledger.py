# -*- coding: utf-8 -*-
"""D-124：把「副本专用难度表」登记进参数台账。"""
import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _utf8out  # noqa: F401

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PATH = os.path.join(ROOT, "production", "qa", "parameters.json")

with io.open(PATH, "r", encoding="utf-8", newline="") as f:
    doc = json.load(f)

row = {
    "id": "dungeon-archetypes",
    "file": "src/Domain/Combat/CombatParameters.cs",
    "status": "【建议值】D-124",
    "note": ("**副本专用**的三档怪物（普通 60 / 精英 210 / 首领 180）—— "
             "与通用表 MonsterTiers（60 / 150 / 120）**分开**：副本、试炼塔、狩猎、兽潮原本共用一张表，"
             "一起抬高会把试炼塔守卫 / 狼 / 兽潮一起弄坏。D-124 拍板「装备 = 通关许可证」"
             "⇒ 副本难度抬高：裸装 43.6% / +2 攻 66.7% / +4 攻 87.2%"),
    "extractor": "dungeon-archetypes",
    "expected": [
        "普通=6,2,60",
        "精英=7,3,210",
        "首领=8,4,180",
    ],
}

for i, t in enumerate(doc["tables"]):
    if t.get("id") == "dungeon-archetypes":
        doc["tables"][i] = row
        break
else:
    doc["tables"].append(row)

with io.open(PATH, "w", encoding="utf-8", newline="") as f:
    json.dump(doc, f, ensure_ascii=False, indent=2)
    f.write("\n")

print("OK tables=%d" % len(doc["tables"]))
