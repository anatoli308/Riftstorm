"""Prueft den SFF-v2-Reader an einer Datei und schreibt Probesprites als PNG."""
from __future__ import annotations

import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.sff import read_sff, sff_version  # noqa: E402

path = sys.argv[1]
out_dir = sys.argv[2] if len(sys.argv) > 2 else None

print("version:", sff_version(path))
sff = read_sff(path)
print("sprites:", len(sff.sprites))

sizes = collections.Counter()
empty = 0
for sprite in sff.sprites:
    sizes[(sprite.width, sprite.height)] += 1
    if sprite.rgba is None and not any(sprite.pixels):
        empty += 1
print("leere (nur Index 0):", empty)
print("haeufigste Groessen:", sizes.most_common(5))
print("truecolor:", sum(1 for s in sff.sprites if s.rgba is not None))

groups = collections.Counter(s.group for s in sff.sprites)
print("groesste Gruppen:", groups.most_common(8))

if out_dir:
    os.makedirs(out_dir, exist_ok=True)
    # Gameplay-Gruppen zuerst: Stand, Lauf, Angriff sind am aussagekraeftigsten.
    preferred = [s for s in sff.sprites if s.group in (0, 20, 200, 210, 5000)]
    written = 0
    for sprite in preferred + sff.sprites:
        if sprite.width <= 4 or sprite.height <= 4 or written >= 12:
            continue
        sprite.to_pil().save(os.path.join(out_dir, f"{sprite.group}_{sprite.image}.png"))
        written += 1
    print("PNGs geschrieben:", written)
