"""Zaehlt AfterImage- und ModifyExplod-Controller ueber mehrere Charaktere."""
from __future__ import annotations

import collections
import json
import os

_HERE = os.path.dirname(os.path.abspath(__file__))
WANTED = ("afterimage", "afterimagetime", "modifyexplod")

counts: collections.Counter[str] = collections.Counter()
samples: list[dict] = []

for char in sorted(os.listdir(os.path.join(_HERE, "out"))):
    path = os.path.join(_HERE, "out", char, "states.json")
    if not os.path.isfile(path):
        continue
    with open(path, encoding="utf-8") as fh:
        doc = json.load(fh)
    for state in doc.get("states") or []:
        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            ctype = (params.get("type") or "").lower()
            if ctype in WANTED:
                counts[f"{char}:{ctype}"] += 1
                if len(samples) < 6:
                    samples.append(params)

print(dict(counts))
print()
print(json.dumps(samples, ensure_ascii=False, indent=1)[:2000])
