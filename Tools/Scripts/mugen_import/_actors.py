"""Zeigt einen Move als Akteure: Root plus Helfer mit eigener Animationskette."""
from __future__ import annotations

import json
import os
import sys

CHAR = sys.argv[1]
WANTED = int(sys.argv[2])
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, f"{CHAR}.json"), encoding="utf-8") as fh:
    doc = json.load(fh)

seq = next((s for s in doc["sequences"] if s["state"] == WANTED), None)
if seq is None:
    print(f"State {WANTED} nicht exportiert")
    raise SystemExit(0)

print(f"=== state {seq['state']}  {seq.get('label')}  [{seq.get('category')}] ===")
print("ROOT")
for a in seq["anims"]:
    print(f"   t={a['time']:<5} ANIM {a['anim']}")
for e in seq["effects"]:
    print(f"   t={e['time']:<5} FX   {e['anim']} pos={e['pos']} prio={e['sprpriority']}")
for h in seq.get("hits") or []:
    print(f"   t={h['time']:<5} HIT  spark={h['spark']} pos={h['sparkpos']}")

for i, actor in enumerate(seq.get("helpers") or []):
    print(f"\nHELPER {i}  spawn t={actor['time']} pos={actor['pos']}")
    for a in actor["anims"]:
        print(f"   t={a['time']:<5} ANIM {a['anim']}")
    for e in actor["effects"]:
        print(f"   t={e['time']:<5} FX   {e['anim']} pos={e['pos']} prio={e['sprpriority']}")
    for s in actor["sounds"]:
        print(f"   t={s['time']:<5} SND  {s['group']},{s['index']}")
    for h in actor["hits"]:
        print(f"   t={h['time']:<5} HIT  spark={h['spark']} pos={h['sparkpos']}")
