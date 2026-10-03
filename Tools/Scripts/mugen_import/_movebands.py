"""Zeigt, welche Statedefs mit Sequenz in Standard-Ranges fallen und welche custom sind."""
from __future__ import annotations

import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, f"{CHAR}.json"), encoding="utf-8") as fh:
    flare = json.load(fh)
with open(os.path.join(_HERE, "out", CHAR, "states.json"), encoding="utf-8") as fh:
    raw = json.load(fh)

labels = {int(s["number"]): (s.get("label") or "")
          for s in raw.get("states") or [] if str(s.get("number", "")).lstrip("-").isdigit()}

# MUGEN-Konvention: <200 System/Basis, 200-299 Normals, 300-399 Sonder,
# 400-799 Throws/Specials, 1000-2999 Specials, 3000-3999 Hypers, 5000+ Get-Hit.
BANDS = [
    (0, 199, "System"),
    (200, 299, "Normal"),
    (300, 399, "Normal"),
    (400, 799, "Throw"),
    (800, 999, "Throw"),
    (1000, 2999, "Special"),
    (3000, 3999, "Hyper"),
    (5000, 5999, "GetHit"),
]


def band(num: int) -> str:
    for lo, hi, name in BANDS:
        if lo <= num <= hi:
            return name
    return "CUSTOM"


groups: dict[str, list[tuple[int, str]]] = {}
for seq in flare.get("sequences") or []:
    num = int(seq.get("state"))
    groups.setdefault(band(num), []).append((num, labels.get(num, "") or seq.get("label", "")))

for name in sorted(groups):
    items = sorted(groups[name])
    print(f"--- {name} ({len(items)}) ---")
    for num, label in items:
        print(f"  {num:<7} {label}")
    print()
