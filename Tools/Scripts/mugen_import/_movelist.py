"""Listet die aus State -1 ableitbaren Moves eines Chars (Command -> StateNo)."""
from __future__ import annotations

import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(_HERE, "out", CHAR, "commands.json"), encoding="utf-8") as fh:
    cmds = json.load(fh)
with open(os.path.join(_HERE, "out", CHAR, f"{CHAR}.json"), encoding="utf-8") as fh:
    flare = json.load(fh)

seq_states = {s.get("state") for s in flare.get("sequences") or []}
entries = cmds.get("state_minus1") or []
print(f"state_minus1 Eintraege: {len(entries)}")
print(json.dumps(entries[:3], ensure_ascii=False, indent=2)[:1200])

hit = 0
for ctrl in entries:
    params = ctrl.get("params") or {}
    value = params.get("value")
    if value is None:
        continue
    try:
        num = int(str(value).strip())
    except ValueError:
        continue
    if num in seq_states:
        hit += 1
print(f"\ndavon mit exportierter Sequenz: {hit}")
