"""Zeigt Header und Zustandsuebergaenge einzelner Statedefs eines Charakters."""
from __future__ import annotations

import json
import os
import sys

CHAR = sys.argv[1]
WANTED = {int(x) for x in sys.argv[2:]}
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, "states.json"), encoding="utf-8") as fh:
    states = json.load(fh)

by_number = {s.get("number"): s for s in states["states"]}

for number in sorted(WANTED):
    state = by_number.get(number)
    if state is None:
        print(f"=== {number}: nicht vorhanden ===\n")
        continue
    header = state.get("header") or {}
    print(f"=== Statedef {number}  {state.get('label') or ''} ===")
    print(f"  header: anim={header.get('anim')} type={header.get('type')} "
          f"movetype={header.get('movetype')} physics={header.get('physics')} "
          f"ctrl={header.get('ctrl')}")
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        ctype = (params.get("type") or "").lower()
        if ctype not in ("changestate", "selfstate", "changeanim", "explod",
                         "helper", "projectile", "hitdef", "destroyself"):
            continue
        shown = {k: v for k, v in params.items()
                 if k in ("value", "anim", "stateno", "projanim", "pos", "postype", "id", "sparkno")}
        print(f"  {ctype:12} {shown}")
        print(f"               triggers={ctrl.get('triggers')}")
    print()
