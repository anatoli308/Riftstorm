"""Exportiert je ein Sprite pro SFF-v2-Kompressionsformat zur Sichtpruefung."""
from __future__ import annotations

import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.sff import read_sff  # noqa: E402

path = sys.argv[1]
out_dir = sys.argv[2]
os.makedirs(out_dir, exist_ok=True)

with open(path, "rb") as fh:
    raw = fh.read()
sprite_ofs, sprite_count = struct.unpack_from("<II", raw, 36)
formats: dict[tuple[int, int], int] = {}
for i in range(sprite_count):
    base = sprite_ofs + i * 28
    group, number = struct.unpack_from("<HH", raw, base)
    formats[(group, number)] = raw[base + 14]

sff = read_sff(path)
written: dict[int, int] = {}
for sprite in sff.sprites:
    fmt = formats.get((sprite.group, sprite.image), -1)
    if written.get(fmt, 0) >= 3 or sprite.width < 20 or sprite.height < 20:
        continue
    sprite.to_pil().save(os.path.join(out_dir, f"fmt{fmt}_{sprite.group}_{sprite.image}.png"))
    written[fmt] = written.get(fmt, 0) + 1
print("geschrieben:", written)
