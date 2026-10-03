"""Findet Atlas-Zellen, die in der ausgelieferten Flare-JSON nirgends vorkommen.

Anders als ``_cellcheck.py`` prueft dieses Skript das Endergebnis: Es loest die
Frames ueber ihre Pixelkoordinaten auf und meldet jede Zelle, die in keiner
einzigen Animation auftaucht. Zusaetzlich wird ausgewiesen, ueber welchen Weg
eine Zelle erreichbar ist (Action, Waisen-Bucket oder gar nicht).
"""
from __future__ import annotations

import json
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DEPLOY = ROOT / "Assets" / "StreamingAssets" / "Custom_Characters"
OUT = Path(__file__).resolve().parent / "out"


def main() -> None:
    names = sys.argv[1:] or sorted(p.name for p in OUT.iterdir() if p.is_dir())
    for name in names:
        atlas_path = OUT / name / "atlas.sprites.json"
        flare_path = DEPLOY / name / f"{name}.json"
        if not atlas_path.exists() or not flare_path.exists():
            continue
        sprites = json.loads(atlas_path.read_text(encoding="utf-8"))["sprites"]
        flare = json.loads(flare_path.read_text(encoding="utf-8"))

        by_rect: dict[tuple[int, int, int, int], str] = {}
        for spr in sprites:
            by_rect[(spr["x"], spr["y"], spr["w"], spr["h"])] = spr["key"]

        reached: dict[str, set[str]] = defaultdict(set)
        for anim_key, anim in flare["animations"].items():
            for direction in anim["frames"]:
                for frame in direction:
                    rect = (frame["x"], frame["y"], frame["w"], frame["h"])
                    cell = by_rect.get(rect)
                    if cell is not None:
                        reached[cell].add(anim_key)

        missing = [s["key"] for s in sprites if s["key"] not in reached]
        by_group: dict[int, int] = defaultdict(int)
        for key in missing:
            by_group[int(key.split("_")[0])] += 1
        print(f"{name}: {len(sprites)} Zellen, {len(reached)} sichtbar, {len(missing)} unerreichbar")
        if by_group:
            listing = ", ".join(f"{g}({n})" for g, n in sorted(by_group.items()))
            print(f"   Gruppen: {listing}")


if __name__ == "__main__":
    main()
