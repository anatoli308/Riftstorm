"""Listet Groesse und Seitenverhaeltnis aller Atlas-Seiten.

Dient dazu, einen vom Nutzer gezeigten Screenshot dem richtigen Charakter
zuzuordnen, bevor Pixelkoordinaten in Sprite-Eintraege uebersetzt werden.
"""
from __future__ import annotations

import os
import struct

ART = os.path.join("..", "..", "..", "Assets", "Art", "Custom_Characters")


def png_size(path: str) -> tuple[int, int]:
    with open(path, "rb") as handle:
        head = handle.read(24)
    return struct.unpack(">II", head[16:24])


def main() -> None:
    for name in sorted(os.listdir(ART)):
        png = os.path.join(ART, name, "atlas.png")
        if not os.path.isfile(png):
            continue
        w, h = png_size(png)
        print(f"{name:<16} {w:>5} x {h:<5} ratio={w / h:.3f}")


if __name__ == "__main__":
    main()
