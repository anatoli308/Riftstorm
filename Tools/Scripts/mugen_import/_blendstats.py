"""Zeigt, welche Actions eines Chars laut AIR additiv/subtraktiv gezeichnet werden."""
from __future__ import annotations

import collections
import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, "animations.json"), encoding="utf-8") as fh:
    doc = json.load(fh)

blends: collections.Counter[str] = collections.Counter()
per_action: dict[int, set[str]] = {}
for action in doc.get("actions") or []:
    number = action.get("number")
    found: set[str] = set()
    for direction in (action.get("directions") or {}).values():
        for frame in direction.get("frames") or []:
            value = (frame.get("blend") or "").strip()
            blends[value or "(keins)"] += 1
            if value:
                found.add(value)
    if found:
        per_action[number] = found

print("Frame-Blends:", dict(blends.most_common(10)))
print("Actions mit Blend:", len(per_action), "von", len(doc.get("actions") or []))
for number, values in list(per_action.items())[:15]:
    print(f"  action_{number}: {sorted(values)}")
