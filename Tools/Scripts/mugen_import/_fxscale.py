"""Zeigt die exportierten Explod-Skalierungen je Charakter."""
from __future__ import annotations

import collections
import glob
import json
import os
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    for folder in sorted(os.listdir(root)):
        manifest = os.path.join(root, folder, f"{folder}.json")
        if not os.path.isfile(manifest):
            continue
        with open(manifest, encoding="utf-8") as fh:
            data = json.load(fh)
        counter: collections.Counter[tuple[float, float]] = collections.Counter()
        for sequence in data.get("sequences", []):
            for effect in sequence.get("effects", []):
                scale = effect.get("scale") or [1.0, 1.0]
                counter[(scale[0], scale[1])] += 1
        print(f"{folder:14s} {sum(counter.values()):5d} Effekte  {counter.most_common(5)}")


if __name__ == "__main__":
    main()
