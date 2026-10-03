"""Wie gross waere ein Laufzeit-Trigger-Interpreter?

Zaehlt die distinkten Trigger-Funktionen in allen Charakter-CNS. Jede davon
muesste zur Laufzeit auswertbar sein, damit die State-Machine echt laeuft.
"""
from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"

# Namen am Anfang eines Terms; Operatoren und Zahlen fallen raus.
TOKEN_RE = re.compile(r"\b([a-z][a-z0-9_]*)\b", re.IGNORECASE)
NOISE = {"and", "or", "not", "xor", "true", "false", "s", "c", "a", "l", "n",
         "u", "h", "m", "sca", "sc", "sa", "ca"}

names: Counter[str] = Counter()
lines = 0
for states_file in sorted(OUT.glob("*/states.json")):
    doc = json.loads(states_file.read_text("utf-8"))
    for state in doc.get("states") or []:
        for ctrl in state.get("controllers") or []:
            text = json.dumps(ctrl.get("triggers") or {})
            lines += text.count('"')
            for token in TOKEN_RE.findall(text):
                low = token.lower()
                if low not in NOISE and not low.startswith("trigger"):
                    names[low] += 1

print(f"distinkte Trigger-Bezeichner: {len(names)}")
print(f"Trigger-Ausdruecke gesamt: ~{lines // 2}")
print("\nTop 25:")
for name, count in names.most_common(25):
    print(f"  {count:6d}  {name}")
