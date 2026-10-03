"""Zeigt die Format-/Farbtiefenverteilung der Sprite-Koepfe eines SFF v2."""
from __future__ import annotations

import collections
import struct
import sys

path = sys.argv[1]
with open(path, "rb") as fh:
    data = fh.read()

sprite_ofs, sprite_count = struct.unpack_from("<II", data, 36)
palette_ofs, palette_count = struct.unpack_from("<II", data, 44)
ldata_ofs, ldata_len = struct.unpack_from("<II", data, 52)
tdata_ofs = struct.unpack_from("<I", data, 60)[0]

print("verbytes:", data[12], data[13], data[14], data[15])
print("sprites:", sprite_count, "at", sprite_ofs)
print("palettes:", palette_count, "at", palette_ofs)
print("ldata:", ldata_ofs, ldata_len, "tdata:", tdata_ofs, "filesize:", len(data))

fmts = collections.Counter()
depths = collections.Counter()
flags_c = collections.Counter()
for i in range(sprite_count):
    base = sprite_ofs + i * 28
    group, number, w, h = struct.unpack_from("<HHHH", data, base)
    fmt = data[base + 14]
    depth = data[base + 15]
    ofs, size = struct.unpack_from("<II", data, base + 16)
    pal, flags = struct.unpack_from("<HH", data, base + 24)
    fmts[fmt] += 1
    depths[depth] += 1
    flags_c[flags] += 1
    if i < 6:
        print(f"  [{i}] {group},{number} {w}x{h} fmt={fmt} depth={depth} "
              f"ofs={ofs} size={size} pal={pal} flags={flags}")

print("formate:", dict(fmts))
print("farbtiefen:", dict(depths))
print("flags:", dict(flags_c))

for i in range(min(3, palette_count)):
    base = palette_ofs + i * 16
    g, n, cols, link = struct.unpack_from("<HHHH", data, base)
    ofs, size = struct.unpack_from("<II", data, base + 8)
    print(f"  pal[{i}] {g},{n} cols={cols} link={link} ofs={ofs} size={size}")
