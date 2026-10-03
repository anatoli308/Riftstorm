"""Findet Atlas-Zellen, die KEINE AIR-Action verwendet.

Solche Sprites sind in der Showcase nirgends erreichbar: weder ueber eine
Sequenz noch ueber "Nur Atlas", weil beide nur Animationen auflisten.
"""
from __future__ import annotations

import json
import sys
from collections import defaultdict
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"


def main() -> None:
    names = sys.argv[1:] or sorted(p.name for p in OUT.iterdir() if p.is_dir())
    for name in names:
        atlas_path = OUT / name / "atlas.sprites.json"
        anim_path = OUT / name / "animations.json"
        if not atlas_path.exists() or not anim_path.exists():
            continue
        atlas = json.loads(atlas_path.read_text(encoding="utf-8"))
        anims = json.loads(anim_path.read_text(encoding="utf-8"))

        cells = {s["key"] for s in atlas["sprites"]}
        used: set[str] = set()
        for action in anims["actions"]:
            for direction in action["directions"].values():
                for frame in direction["frames"]:
                    used.add(frame["sprite"])

        orphans = cells - used
        by_group: dict[int, int] = defaultdict(int)
        for key in orphans:
            by_group[int(key.split("_")[0])] += 1
        print(f"{name}: {len(cells)} Zellen, {len(used)} in Actions, {len(orphans)} verwaist")
        if orphans:
            listing = ", ".join(f"{g}({n})" for g, n in sorted(by_group.items()))
            print(f"   Gruppen: {listing}")


if __name__ == "__main__":
    main()
