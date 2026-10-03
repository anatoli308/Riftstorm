"""Welche AssertSpecial-Flags kommen vor? Nur wenige sind ueberhaupt sichtbar."""
from __future__ import annotations

import json
from collections import Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"

flags: Counter[str] = Counter()
for states_file in sorted(OUT.glob("*/states.json")):
    doc = json.loads(states_file.read_text("utf-8"))
    for state in doc.get("states") or []:
        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            if (params.get("type") or "").strip().lower() != "assertspecial":
                continue
            for key in ("flag", "flag2", "flag3"):
                value = str(params.get(key) or "").strip().lower()
                if value:
                    flags[value] += 1

for flag, count in flags.most_common(20):
    print(f"  {count:5d}  {flag}")
