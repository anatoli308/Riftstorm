"""Gesamtbericht: was steckt nach dem Import wirklich in jedem Charakter?

Zeigt je Charakter die Anzahl Animationen und Sequenzen, die Verteilung auf
Kategorien, wie viele Sequenzen einen Namen tragen und eine Kostprobe der
charaktereigenen Moves.
"""
from __future__ import annotations

import collections
import json
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_OUT = os.path.join(_HERE, "out")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def _load(char: str, name: str) -> dict | None:
    path = os.path.join(_OUT, char, name)
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def main() -> None:
    samples = int(sys.argv[1]) if len(sys.argv) > 1 else 6
    total_sequences = 0

    for char in sorted(os.listdir(_OUT)):
        flare = _load(char, f"{char}.json")
        if flare is None:
            continue
        states = _load(char, "states.json") or {"states": []}
        labels = {s.get("number"): (s.get("label") or "").strip() for s in states["states"]}

        sequences = flare.get("sequences", [])
        total_sequences += len(sequences)
        categories = collections.Counter(s["category"] for s in sequences)
        named = sum(1 for s in sequences if labels.get(s["state"]) or s.get("label"))

        print(f"\n===== {char} =====")
        print(f"  {len(flare.get('animations', {})):4d} Animationen   "
              f"{len(sequences):4d} Moves   {named:4d} benannt")
        print(f"  {dict(sorted(categories.items()))}")

        custom = [
            s for s in sorted(sequences, key=lambda s: s["state"])
            if s["category"] in ("Custom", "Helper")
            and (labels.get(s["state"]) or s.get("label"))
        ]
        for seq in custom[:samples]:
            label = labels.get(seq["state"]) or seq.get("label")
            print(f"     {seq['state']:<8} [{seq['category']:<6}] {label}")
        if len(custom) > samples:
            print(f"     … und {len(custom) - samples} weitere eigene Moves")

    print(f"\nGesamt: {total_sequences} Moves")


if __name__ == "__main__":
    main()
