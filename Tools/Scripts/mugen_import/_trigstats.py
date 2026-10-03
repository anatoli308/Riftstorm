"""Zeigt die Roh-Trigger der Explod-Controller (Diagnose der Tick-Aufloesung)."""
from __future__ import annotations

import collections
import json
import os
import re
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_HERE = os.path.dirname(os.path.abspath(__file__))
char = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
path = os.path.join(_HERE, "out", char, "states.json")

with open(path, encoding="utf-8") as fh:
    doc = json.load(fh)

kinds = collections.Counter()
samples: list[str] = []
for state in doc.get("states") or []:
    if not isinstance(state.get("number"), int) or state["number"] < 0:
        continue
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        if (params.get("type") or "").lower() != "explod":
            continue
        trig = ctrl.get("triggers")
        if not isinstance(trig, dict) or not trig:
            kinds["<keine trigger>"] += 1
            continue
        flat = [str(c).strip().lower() for conds in trig.values() for c in (conds or ())]
        if not flat:
            kinds["<leer>"] += 1
            continue
        for c in flat:
            head = re.split(r"[=<>!]", c, 1)[0].strip()
            kinds[head or "?"] += 1
        if len(samples) < 25:
            samples.append(f"state {state['number']:>5}: {json.dumps(trig, ensure_ascii=False)}")

print(f"=== {char}: Trigger-Kopf-Haeufigkeit bei Explod ===")
for k, v in kinds.most_common(25):
    print(f"  {v:5d}  {k}")
print("\n=== Beispiele ===")
for s in samples:
    print(" ", s)
