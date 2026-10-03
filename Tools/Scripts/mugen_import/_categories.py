"""Kategorie-Abdeckung: faellt eine Animation oder Sequenz durchs Raster der Showcase?"""
from __future__ import annotations

import collections
import json
import os
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
# Muss zu m_Categories in MugenAnimationShowcase.cs passen.
_SHOWN = {"Normal", "Special", "Hyper", "Command", "Custom", "System", "Helper", "Effect"}

categories: collections.Counter[str] = collections.Counter()
gaps = 0
for char in sorted(os.listdir(_OUT)):
    path = os.path.join(_OUT, char, f"{char}.json")
    if not os.path.isfile(path):
        continue
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    sequences = data["sequences"]
    animations = set(data["animations"])

    referenced: set[str] = set()
    for seq in sequences:
        for entry in seq.get("anims") or []:
            referenced.add(entry["anim"])
        for entry in seq.get("victim") or []:
            referenced.add(entry["anim"])
        for entry in seq.get("effects") or []:
            referenced.add(entry["anim"])
        for entry in seq.get("hits") or []:
            if entry.get("spark"):
                referenced.add(entry["spark"])
        for helper in seq.get("helpers") or []:
            for entry in helper.get("anims") or []:
                referenced.add(entry["anim"])
            for entry in helper.get("effects") or []:
                referenced.add(entry["anim"])
        categories[seq.get("category") or "<leer>"] += 1

    ghosts = referenced - animations
    if ghosts:
        gaps += 1
        print(f"{char}: {len(ghosts)} Verweise auf nicht vorhandene Animationen")

print("Kategorien im Datenbestand:")
for name, count in categories.most_common():
    mark = "" if name in _SHOWN else "   <-- NICHT in m_Categories"
    print(f"  {name:<12} {count:>5}{mark}")
print(f"\nCharaktere mit Geister-Verweisen: {gaps}")
