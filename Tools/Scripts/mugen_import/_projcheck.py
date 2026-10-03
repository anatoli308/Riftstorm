"""Zeigt, in welchen Statedefs Projectiles stehen und ob diese exportiert werden."""
from __future__ import annotations

import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, "states.json"), encoding="utf-8") as fh:
    states = json.load(fh)
with open(os.path.join(_HERE, "out", CHAR, f"{CHAR}.json"), encoding="utf-8") as fh:
    flare = json.load(fh)

exported = {s["state"] for s in flare["sequences"]}
anims = set(flare["animations"])

for state in states["states"]:
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        if (params.get("type") or "").lower() != "projectile":
            continue
        number = state.get("number")
        header = state.get("header") or {}
        head_anim = header.get("anim")
        print(f"state {number}: exportiert={number in exported} "
              f"header.anim={head_anim} key_vorhanden={f'action_{head_anim}' in anims} "
              f"triggers={ctrl.get('triggers')}")
