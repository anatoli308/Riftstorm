"""Sequenzen ohne Body-Animation - die verwirft der Showcase-Katalog derzeit."""
from __future__ import annotations

import json
import os
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

total = 0
bodyless = 0
for char in sorted(os.listdir(_OUT)):
    path = os.path.join(_OUT, char, f"{char}.json")
    if not os.path.isfile(path):
        continue
    with open(path, encoding="utf-8") as fh:
        sequences = json.load(fh)["sequences"]
    empty = [s for s in sequences if not s.get("anims")]
    total += len(sequences)
    bodyless += len(empty)
    if empty:
        print(f"{char}: {len(empty)} von {len(sequences)} ohne Body")
        for seq in empty[:8]:
            tracks = [k for k in ("victim", "effects", "helpers", "hits", "sounds") if seq.get(k)]
            print(f"   {seq['state']:<7} {(seq.get('label') or ''):<34} {tracks}")

print(f"\ngesamt {total} Sequenzen, {bodyless} ohne Body-Animation")
