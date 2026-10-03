"""Wie viele Sequenzen tragen jetzt eine sprpriority-Spur?"""
from __future__ import annotations

import json
import os
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

total = 0
withprio = 0
values: set[int] = set()
for char in sorted(os.listdir(_OUT)):
    path = os.path.join(_OUT, char, f"{char}.json")
    if not os.path.isfile(path):
        continue
    with open(path, encoding="utf-8") as fh:
        sequences = json.load(fh)["sequences"]
    hits = [s for s in sequences if s.get("sprpriority")]
    total += len(sequences)
    withprio += len(hits)
    for seq in hits:
        for entry in seq["sprpriority"]:
            values.add(entry["value"])
    print(f"{char}: {len(hits)} von {len(sequences)} mit sprpriority-Spur")

print(f"\ngesamt {withprio} von {total}")
print("Werte:", sorted(values))
