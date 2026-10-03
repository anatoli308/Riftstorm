"""Zaehlt Effekt-Positionstypen und Projektil-Aufloesungen je Charakter.

Zeigt, wie viele Effekte an einem anderen Bezugspunkt als dem Charakter selbst
haengen — genau die Faelle, die eine falsche `postype`-Behandlung verschiebt.
"""
from __future__ import annotations

import collections
import glob
import json
import os
import sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")


def _effects(doc: dict) -> list[dict]:
    found: list[dict] = []
    for seq in doc.get("sequences", []):
        found.extend(seq.get("effects") or [])
        for helper in seq.get("helpers") or []:
            found.extend(helper.get("effects") or [])
    return found


def main(names: list[str]) -> None:
    total: collections.Counter[str] = collections.Counter()
    for path in sorted(glob.glob(os.path.join(OUT, "*", "*.json"))):
        char = os.path.basename(os.path.dirname(path))
        if os.path.basename(path) != f"{char}.json":
            continue
        if names and char not in names:
            continue
        with open(path, encoding="utf-8") as fh:
            doc = json.load(fh)
        types: collections.Counter[str] = collections.Counter()
        mirrored = 0
        with_hit = 0
        for fx in _effects(doc):
            types[fx.get("postype") or "p1"] += 1
            if fx.get("facing", 1) < 0:
                mirrored += 1
            if fx.get("hit"):
                with_hit += 1
        total.update(types)
        total["*treffer"] += with_hit
        interesting = {k: v for k, v in types.items() if k not in ("p1",)}
        print(f"{char:16} {sum(types.values()):5} Effekte  "
              f"rueckwaerts={mirrored:3}  einschlag={with_hit:3}  "
              f"{dict(sorted(interesting.items()))}")
    print(f"\nGESAMT {dict(sorted(total.items()))}")


if __name__ == "__main__":
    main(sys.argv[1:])
