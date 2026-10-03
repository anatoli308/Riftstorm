"""Zaehlt die Zeit-Trigger-Formen ueber alle Chars (Diagnose der Tick-Aufloesung)."""
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
out_root = os.path.join(_HERE, "out")

PATTERNS = [
    ("time = N", re.compile(r"^time\s*=\s*-?\d+$")),
    ("time >= N", re.compile(r"^time\s*>=\s*-?\d+$")),
    ("time % N = K", re.compile(r"^time\s*%\s*\d+\s*=\s*\d+$")),
    ("animelem = N", re.compile(r"^animelem\s*=\s*\d+")),
    ("animelemno(0) = N", re.compile(r"^animelemno\s*\(\s*0\s*\)\s*=\s*\d+")),
    ("animelemtime", re.compile(r"^animelemtime")),
]

kinds = collections.Counter()
ctrl_total = 0
ctrl_with_time = 0

for char in sorted(os.listdir(out_root)):
    path = os.path.join(out_root, char, "states.json")
    if not os.path.isfile(path):
        continue
    with open(path, encoding="utf-8") as fh:
        doc = json.load(fh)
    for state in doc.get("states") or []:
        if not isinstance(state.get("number"), int) or state["number"] < 0:
            continue
        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            if (params.get("type") or "").lower() not in ("explod", "projectile", "helper"):
                continue
            ctrl_total += 1
            trig = ctrl.get("triggers")
            flat = []
            if isinstance(trig, dict):
                flat = [str(c).strip().lower() for conds in trig.values() for c in (conds or ())]
            matched = False
            for cond in flat:
                for label, rx in PATTERNS:
                    if rx.match(cond):
                        kinds[label] += 1
                        matched = True
                        break
            if matched:
                ctrl_with_time += 1

print(f"Effekt-Controller gesamt: {ctrl_total}")
print(f"davon mit Zeit-Trigger  : {ctrl_with_time}")
for label, _ in PATTERNS:
    print(f"  {kinds[label]:5d}  {label}")
