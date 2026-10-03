"""Prueft Body-Ketten auf mehrfach belegte Ticks und die LoopStart-Uebernahme."""
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


anims = load("animations.json")
flare = load(f"{CHAR}.json")

# Welche Schluessel liefert der AIR-Parser ueberhaupt?
first = (anims.get("actions") or [{}])[0]
print("Action-Schluessel:", sorted(first.keys()))
# loopStart liegt pro Richtung, nicht auf Action-Ebene.
loops = []
for action in anims.get("actions") or []:
    east = (action.get("directions") or {}).get("E") or {}
    if east.get("loopStart"):
        loops.append((action.get("number"), east["loopStart"], len(east.get("frames") or [])))
print("Actions mit LoopStart > 0:", len(loops), "von", len(anims.get("actions") or []))
print("  Beispiele (action, loopStart, frames):", loops[:10])

# Body-Ketten, in denen mehrere Animationen auf demselben Tick liegen: das sind
# sich ausschliessende ChangeAnim-Zweige, keine Abfolge.
clashes = []
for seq in flare["sequences"]:
    ticks = collections.Counter(a["time"] for a in seq["anims"])
    doubled = [t for t, n in ticks.items() if n > 1]
    if doubled:
        clashes.append((seq["state"], [(a["anim"], a["time"]) for a in seq["anims"]]))
print(f"\nMoves mit mehrfach belegtem Tick: {len(clashes)} von {len(flare['sequences'])}")
for state, chain in clashes[:8]:
    print(f"  state {state}: {chain}")
