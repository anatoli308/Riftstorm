"""Sagt zu einem Atlas-Punkt, welches Sprite dort liegt und wer es benutzt.

Uebersetzt Pixelkoordinaten einer Atlas-Seite in den Sprite-Schluessel, listet
die Animationen, die das Sprite zeigen, und ob eine Sequenz diese Animationen
ueberhaupt einplant.

Aufruf: python _atlaswhere.py <Charakter> <x> <y> [<x> <y> ...]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DEPLOY = ROOT / "Assets" / "StreamingAssets" / "Custom_Characters"
OUT = Path(__file__).resolve().parent / "out"


def sequence_anims(flare: dict) -> set[str]:
    keys: set[str] = set()
    for seq in flare.get("sequences", []):
        for entry in seq.get("anims") or []:
            keys.add(entry.get("anim"))
        for fx in seq.get("effects") or []:
            keys.add(fx.get("anim"))
            if fx.get("hitanim"):
                keys.add(fx["hitanim"])
    keys.discard(None)
    return keys


def main() -> None:
    name = sys.argv[1]
    points = [(int(sys.argv[i]), int(sys.argv[i + 1])) for i in range(2, len(sys.argv) - 1, 2)]
    sprites = json.loads((OUT / name / "atlas.sprites.json").read_text(encoding="utf-8"))["sprites"]
    flare = json.loads((DEPLOY / name / f"{name}.json").read_text(encoding="utf-8"))
    used = sequence_anims(flare)

    owners: dict[tuple[int, int, int, int], list[str]] = {}
    for key, anim in flare["animations"].items():
        for direction in anim["frames"]:
            for frame in direction:
                owners.setdefault((frame["x"], frame["y"], frame["w"], frame["h"]), []).append(key)

    for px, py in points:
        hit = [s for s in sprites
               if s["x"] <= px < s["x"] + s["w"] and s["y"] <= py < s["y"] + s["h"]]
        if not hit:
            print(f"({px},{py}) -> keine Zelle")
            continue
        for spr in hit:
            rect = (spr["x"], spr["y"], spr["w"], spr["h"])
            anims = sorted(set(owners.get(rect, [])))
            live = [a for a in anims if a in used]
            state = "IN SEQUENZ: " + ", ".join(live) if live else "NIRGENDS EINGEPLANT"
            print(f"({px},{py}) -> {spr['key']}  rect={rect}")
            print(f"      Animationen: {', '.join(anims) or '-'}")
            print(f"      {state}")


if __name__ == "__main__":
    main()
