"""Listet die charaktereigenen Statedefs mit Nummer, Label und Inhalt."""
from __future__ import annotations

import collections
import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, f"{CHAR}.json"), encoding="utf-8") as fh:
    flare = json.load(fh)
with open(os.path.join(_HERE, "out", CHAR, "states.json"), encoding="utf-8") as fh:
    states = json.load(fh)

labels = {s.get("number"): (s.get("label") or "").strip() for s in states["states"]}

print("Kategorien:", dict(collections.Counter(s["category"] for s in flare["sequences"])))

# Statedef-Nummern in Bloecken: MUGEN-Autoren gruppieren eigene Zustaende
# typischerweise in zusammenhaengenden Nummernbereichen.
blocks: collections.Counter[int] = collections.Counter()
for seq in flare["sequences"]:
    blocks[seq["state"] // 1000] += 1
print("Nummernbloecke (Tausender -> Anzahl):", dict(sorted(blocks.items())))

print("\nCustom-States mit Label:")
shown = 0
for seq in sorted(flare["sequences"], key=lambda s: s["state"]):
    if seq["category"] not in ("Custom", "Helper"):
        continue
    label = labels.get(seq["state"]) or seq.get("label") or ""
    if not label:
        continue
    print(f"   {seq['state']:<8} [{seq['category']:<7}] {label}")
    shown += 1
    if shown >= 30:
        break

named = sum(1 for s in flare["sequences"] if (labels.get(s["state"]) or s.get("label")))
print(f"\nSequenzen mit Label: {named} von {len(flare['sequences'])}")
