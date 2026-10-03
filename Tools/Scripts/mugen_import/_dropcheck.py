"""Prueft, welche Statedefs beim Export wegfallen und welche Animationen stehen."""
from __future__ import annotations

import collections
import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))


def load(name: str):
    with open(os.path.join(_HERE, "out", CHAR, name), encoding="utf-8") as fh:
        return json.load(fh)


states = load("states.json")
flare = load(f"{CHAR}.json")
exported = {s["state"] for s in flare["sequences"]}
anims = flare["animations"]

# --- weggefallene Statedefs ---------------------------------------------
dropped_with_anim = []
for state in states["states"]:
    number = state.get("number")
    if not isinstance(number, int) or number < 0 or number in exported:
        continue
    head = (state.get("header") or {}).get("anim")
    key = f"action_{head}" if head is not None else None
    if key in anims:
        dropped_with_anim.append((number, key, state.get("label") or ""))

print(f"Statedefs gesamt: {len(states['states'])}, exportiert: {len(exported)}")
print(f"Weggefallen OBWOHL Animation vorhanden: {len(dropped_with_anim)}")
for number, key, label in dropped_with_anim[:20]:
    print(f"   {number:<8} {key:<16} {label}")

# --- Standbilder ---------------------------------------------------------
frames = collections.Counter(v["frames_count"] for v in anims.values())
single = [k for k, v in anims.items() if v["frames_count"] <= 1]
print(f"\nFrame-Anzahlen: {dict(sorted(frames.items())[:8])}")
print(f"Animationen mit 1 Frame: {len(single)} von {len(anims)}")

for name in ("action_0", "action_11", "action_20", "action_5000"):
    a = anims.get(name)
    if a:
        print(f"  {name}: frames={a['frames_count']} type={a['type']} "
              f"dauer={a['duration_ms']}ms loop_start={a.get('loop_start')} "
              f"erste_dauern={a['frame_durations_ms'][:6]}")

types = collections.Counter(v["type"] for v in anims.values())
print("Typen:", dict(types))
