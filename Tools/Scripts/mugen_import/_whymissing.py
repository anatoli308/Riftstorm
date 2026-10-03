"""Warum wurde eine AIR-Action nicht exportiert? Prueft die referenzierten Sprites."""
from __future__ import annotations

import collections
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


def main() -> None:
    char = sys.argv[1]
    folder = os.path.join(_DATA, char)

    with open(os.path.join(_OUT, char, f"{char}.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)
    exported = {
        int(name[len("action_"):].split("_")[0])
        for name in manifest.get("animations", {})
        if name.startswith("action_") and name[len("action_"):].split("_")[0].isdigit()
    }

    sprite_path = os.path.join(_OUT, char, "atlas.sprites.json")
    with open(sprite_path, encoding="utf-8") as fh:
        available = {s["key"] for s in json.load(fh)["sprites"]}

    actions = {}
    for path in glob.glob(os.path.join(folder, "**", "*.air"), recursive=True):
        for action in parse_air(path):
            if action.frames:
                actions[action.number] = action

    missing = sorted(set(actions) - exported)
    print(f"{char}: {len(actions)} AIR-Actions mit Frames, {len(exported)} exportiert, "
          f"{len(missing)} fehlen")

    groups: collections.Counter[int] = collections.Counter()
    for number in missing:
        action = actions[number]
        refs = [f"{f.group}_{f.image}" for f in action.frames]
        gone = [r for r in refs if r not in available]
        for ref in gone:
            groups[int(ref.split("_")[0])] += 1
        print(f"  action {number:>8}: {len(action.frames):3d} Frames, "
              f"{len(gone):3d} ohne Sprite  z.B. {sorted(set(gone))[:6]}")
    if groups:
        print(f"\n  fehlende Sprite-Gruppen: {groups.most_common(12)}")


if __name__ == "__main__":
    main()
