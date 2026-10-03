"""Ermittelt die tatsaechlich belegte Hoehe jeder Atlas-Seite.

Ein Screenshot zeigt immer die ganze Seite, aber nur der obere Teil ist
befuellt. Ueber diesen Fuellstand laesst sich ein Screenshot eindeutig einem
Charakter zuordnen, wenn mehrere Atlanten dieselbe Groesse haben.
"""
from __future__ import annotations

import json
import os

OUT = "out"


def main() -> None:
    for name in sorted(os.listdir(OUT)):
        path = os.path.join(OUT, name, "atlas.sprites.json")
        if not os.path.isfile(path):
            continue
        data = json.load(open(path, encoding="utf-8"))
        sprites = data["sprites"] if isinstance(data, dict) else data
        bottom = 0
        for spr in sprites:
            bottom = max(bottom, int(spr["y"]) + int(spr["h"]))
        print(f"{name:<16} sprites={len(sprites):<6} belegt_bis_y={bottom}")


if __name__ == "__main__":
    main()
