"""Vergleicht dekodierte Sprites mit dem, was ein sauberer Decode liefern muss."""
from __future__ import annotations

import collections
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.sff import read_sff  # noqa: E402

path = sys.argv[1]
with open(path, "rb") as fh:
    raw = fh.read()

sprite_ofs, sprite_count = struct.unpack_from("<II", raw, 36)
formats: dict[tuple[int, int], int] = {}
for i in range(sprite_count):
    base = sprite_ofs + i * 28
    group, number = struct.unpack_from("<HH", raw, base)
    formats[(group, number)] = raw[base + 14]

sff = read_sff(path)
by_format: dict[int, list[float]] = collections.defaultdict(list)
for sprite in sff.sprites:
    if sprite.width <= 0 or sprite.height <= 0 or sprite.rgba is not None:
        continue
    zero = sprite.pixels.count(0) / len(sprite.pixels)
    by_format[formats.get((sprite.group, sprite.image), -1)].append(zero)

# Ein sauber dekodiertes MUGEN-Sprite besteht groesstenteils aus Index 0
# (Hintergrund). Liegt der Anteil bei einem Format deutlich niedriger,
# stimmt dessen Decoder nicht.
for fmt, values in sorted(by_format.items()):
    avg = sum(values) / len(values)
    print(f"format {fmt}: {len(values)} Sprites, Index-0-Anteil im Mittel {avg:.3f}")

for sprite in sff.sprites:
    if sprite.group == 0 and sprite.image == 0:
        print("sprite 0,0:", sprite.width, "x", sprite.height,
              "fmt", formats.get((0, 0)),
              "index0", sprite.pixels.count(0) / max(1, len(sprite.pixels)))
        print("erste 40 Pixel:", list(sprite.pixels[:40]))
        break
