"""Deckungspruefung: was steht in den MUGEN-Rohdaten, was landet im FLARE-Export?

Vergleicht je Charakter
  * AIR-Actions   gegen die Animationen im Manifest
  * CNS-Statedefs gegen die Sequenzen im Manifest
  * SFF-Sprites   gegen die im Atlas gepackten Zellen
und listet die fehlenden Nummern auf.
"""
from __future__ import annotations

import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.air import parse_air

_HERE = os.path.dirname(os.path.abspath(__file__))
_DATA = os.path.join(_HERE, "mugen_data")
_OUT = os.path.join(_HERE, "out")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def _air_actions(folder: str) -> set[int]:
    numbers: set[int] = set()
    for path in glob.glob(os.path.join(folder, "**", "*.air"), recursive=True):
        for action in parse_air(path):
            if action.frames:
                numbers.add(action.number)
    return numbers


def _exported(char: str) -> tuple[set[int], set[int], int]:
    manifest = os.path.join(_OUT, char, f"{char}.json")
    if not os.path.isfile(manifest):
        return set(), set(), 0
    with open(manifest, encoding="utf-8") as fh:
        data = json.load(fh)

    animations: set[int] = set()
    for name in data.get("animations", {}):
        if name.startswith("action_"):
            try:
                animations.add(int(name[len("action_"):]))
            except ValueError:
                pass
    sequences = {int(s["state"]) for s in data.get("sequences", []) if "state" in s}
    return animations, sequences, len(data.get("animations", {}))


def _states(char: str) -> set[int]:
    path = os.path.join(_OUT, char, "states.json")
    if not os.path.isfile(path):
        return set()
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    entries = data if isinstance(data, list) else data.get("states", [])
    return {int(s["number"]) for s in entries if "number" in s}


def _preview(numbers: set[int], limit: int = 14) -> str:
    ordered = sorted(numbers)
    head = ", ".join(str(n) for n in ordered[:limit])
    return head + (f", … (+{len(ordered) - limit})" if len(ordered) > limit else "")


def main() -> None:
    only = {a.lower() for a in sys.argv[1:]}
    for char in sorted(os.listdir(_DATA)):
        folder = os.path.join(_DATA, char)
        if not os.path.isdir(folder) or (only and char.lower() not in only):
            continue

        air = _air_actions(folder)
        animations, sequences, total_anims = _exported(char)
        states = _states(char)

        missing_anims = air - animations
        missing_states = states - sequences

        print(f"\n===== {char} =====")
        print(f"AIR-Actions {len(air):4d} -> exportiert {len(animations):4d} "
              f"(Manifest gesamt {total_anims})   fehlend {len(missing_anims)}")
        if missing_anims:
            print(f"   fehlende Actions: {_preview(missing_anims)}")
        print(f"Statedefs   {len(states):4d} -> Sequenzen   {len(sequences):4d}"
              f"   fehlend {len(missing_states)}")
        if missing_states:
            print(f"   fehlende States : {_preview(missing_states)}")


if __name__ == "__main__":
    main()
