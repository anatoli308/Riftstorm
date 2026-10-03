"""Schneidet einen Streifen der Atlas-Seite aus und legt ein Koordinatenraster darueber.

Damit lassen sich Stellen, die im Screenshot markiert wurden, auf exakte
Atlas-Pixel und damit auf Sprite-Eintraege zurueckfuehren.

Aufruf: python _atlascrop.py <Charakter> <y0> <y1> [spalten]
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / "Assets" / "Art" / "Custom_Characters"
GRID = 256
TARGET_WIDTH = 1400


def main() -> None:
    name = sys.argv[1]
    y0, y1 = int(sys.argv[2]), int(sys.argv[3])
    columns = int(sys.argv[4]) if len(sys.argv) > 4 else 4

    Image.MAX_IMAGE_PIXELS = None
    page = Image.open(ART / name / "atlas.png").convert("RGBA")
    step = page.width // columns
    for index in range(columns):
        x0 = index * step
        tile = page.crop((x0, y0, x0 + step, y1))
        scale = TARGET_WIDTH / tile.width
        tile = tile.resize((TARGET_WIDTH, max(1, int(tile.height * scale))), Image.LANCZOS)

        flat = Image.new("RGBA", tile.size, (255, 255, 255, 255))
        flat.alpha_composite(tile)
        draw = ImageDraw.Draw(flat)
        for gx in range(x0 - x0 % GRID + GRID, x0 + step, GRID):
            px = int((gx - x0) * scale)
            draw.line([(px, 0), (px, flat.height)], fill=(255, 0, 0, 90))
            draw.text((px + 2, 2), str(gx), fill=(200, 0, 0, 255))
        for gy in range(y0 - y0 % GRID + GRID, y1, GRID):
            py = int((gy - y0) * scale)
            draw.line([(0, py), (flat.width, py)], fill=(0, 0, 255, 90))
            draw.text((2, py + 2), str(gy), fill=(0, 0, 200, 255))

        out = Path(__file__).parent / f"_crop_{name}_{index}.png"
        flat.convert("RGB").save(out)
        print(out)


if __name__ == "__main__":
    main()
