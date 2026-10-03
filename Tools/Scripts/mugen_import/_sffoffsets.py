"""Prueft, ob Sprite- und Palettenoffsets eines SFF v2 richtig aufgeloest werden."""
from __future__ import annotations

import struct
import sys

path = sys.argv[1]
with open(path, "rb") as fh:
    data = fh.read()

sprite_ofs, sprite_count = struct.unpack_from("<II", data, 36)
palette_ofs, palette_count = struct.unpack_from("<II", data, 44)
ldata_ofs = struct.unpack_from("<I", data, 52)[0]

# Bei den Kompressionsformaten stehen die ersten vier Bytes des Datenblocks
# fuer die Groesse der dekomprimierten Pixel. Stimmt sie mit Breite*Hoehe
# ueberein, zeigt der Offset auf die richtige Stelle.
matches = 0
checked = 0
for i in range(min(40, sprite_count)):
    base = sprite_ofs + i * 28
    group, number, w, h = struct.unpack_from("<HHHH", data, base)
    fmt = data[base + 14]
    ofs, size = struct.unpack_from("<II", data, base + 16)
    if fmt < 2 or size < 4:
        continue
    absolute = ldata_ofs + ofs
    declared = struct.unpack_from("<I", data, absolute)[0]
    checked += 1
    ok = declared == w * h
    matches += ok
    if i < 6:
        print(f"[{i}] {group},{number} {w}x{h}={w * h} fmt={fmt} "
              f"abs={absolute} deklariert={declared} {'OK' if ok else 'FALSCH'}")
print(f"Offsets plausibel: {matches}/{checked}")

print("\nPalettenblock bei ldata+0:")
print(" ", list(data[ldata_ofs:ldata_ofs + 16]))
first_pal_ofs, first_pal_size = struct.unpack_from("<II", data, palette_ofs + 8)
print(f"  erster Paletteneintrag ofs={first_pal_ofs} size={first_pal_size}")
print(f"  Palettenbereich braucht {palette_count * 1024} Bytes ab ldata")
