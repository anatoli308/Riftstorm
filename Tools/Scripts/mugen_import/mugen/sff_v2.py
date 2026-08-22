"""SFF v2 reader (MUGEN 1.0/1.1, Ikemen-GO).

Anders als v1 (eine PCX-Datei pro Sprite) trennt v2 Sprite-Koepfe, Paletten und
Pixeldaten in eigene Bloecke:

    Header (nach der 12-Byte-Signatur "ElecbyteSpr\\0"):
        12  4  Versionsbytes (verlo3, verlo2, verlo1, verhi) -- verhi == 2
        16  4  reserviert
        20 16  reserviert
        36  4  Offset des ersten Sprite-Kopfes
        40  4  Anzahl Sprites
        44  4  Offset des ersten Paletten-Kopfes
        48  4  Anzahl Paletten
        52  4  ldata-Offset (unkomprimierter Datenblock)
        56  4  ldata-Laenge
        60  4  tdata-Offset (komprimierter Datenblock)

    Sprite-Kopf (28 Byte):
        0  2  Gruppe            14  1  Format
        2  2  Nummer            15  1  Farbtiefe
        4  4  Breite, Hoehe     16  4  Daten-Offset (relativ zu ldata/tdata)
        8  4  Achse x, y        20  4  Datenlaenge
       12  2  verlinkter Index  24  2  Palettenindex
                                26  2  Flags (Bit 0: 0 = ldata, 1 = tdata)

    Paletten-Kopf (16 Byte):
        0  2  Gruppe   4  2  Farbanzahl   8  4  Daten-Offset (relativ zu ldata)
        2  2  Nummer   6  2  verlinkt    12  4  Datenlaenge

Formate: 0 = roh, 1 = verlinkt, 2 = RLE8, 3 = RLE5, 4 = LZ5,
10 = PNG8, 11 = PNG24, 12 = PNG32. Die Kompressionsformate 2-4 stellen ihrem
Strom eine 4-Byte-Laenge voran; PNG-Sprites ebenso.

Die Decoder sind 1:1 aus Ikemen-GOs ``src/image.go`` portiert
(``Rle8Decode``, ``Rle5Decode``, ``Lz5Decode``).
"""

from __future__ import annotations

import io
import struct

from PIL import Image

from .sff_v1 import SffFile, SffSprite

_SFF_MAGIC = b"ElecbyteSpr\x00"
_SPRITE_NODE_SIZE = 28
_PALETTE_NODE_SIZE = 16

FMT_RAW = 0
FMT_LINKED = 1
FMT_RLE8 = 2
FMT_RLE5 = 3
FMT_LZ5 = 4
FMT_PNG8 = 10
FMT_PNG24 = 11
FMT_PNG32 = 12


def read_sff_v2(path: str) -> SffFile:
    """Parse an SFF v2 file and return all sprites fully decoded.

    Raises ValueError if the file is not SFF v2.
    """
    with open(path, "rb") as fh:
        data = fh.read()

    if not data.startswith(_SFF_MAGIC):
        raise ValueError(f"Not an SFF file: {path}")
    version = (data[15], data[14], data[13], data[12])
    if data[15] != 2:
        raise ValueError(f"Not an SFF v2 file (version {version}): {path}")

    sprite_ofs, sprite_count = struct.unpack_from("<II", data, 36)
    palette_ofs, palette_count = struct.unpack_from("<II", data, 44)
    ldata_ofs = struct.unpack_from("<I", data, 52)[0]
    tdata_ofs = struct.unpack_from("<I", data, 60)[0]

    palettes = _read_palettes(data, palette_ofs, palette_count, ldata_ofs)
    sprites = _read_sprites(
        data, sprite_ofs, sprite_count, ldata_ofs, tdata_ofs, palettes)

    return SffFile(
        version=version,
        num_groups=len({s.group for s in sprites}),
        num_images=len(sprites),
        palette_type=0,
        sprites=sprites,
    )


def _read_palettes(
    data: bytes,
    node_ofs: int,
    count: int,
    ldata_ofs: int,
) -> list[bytes]:
    """Liest die Palettenbank; jeder Eintrag wird auf 256*3 RGB normalisiert."""
    palettes: list[bytes] = []
    for i in range(count):
        base = node_ofs + i * _PALETTE_NODE_SIZE
        if base + _PALETTE_NODE_SIZE > len(data):
            break
        link = struct.unpack_from("<H", data, base + 6)[0]
        offset, size = struct.unpack_from("<II", data, base + 8)
        if size == 0:
            # Verlinkte Palette: teilt sich die Farben mit einem frueheren Eintrag.
            palettes.append(palettes[link] if link < len(palettes) else b"\x00" * 768)
            continue
        palettes.append(_rgba_block_to_rgb(data, ldata_ofs + offset, size))
    return palettes


def _rgba_block_to_rgb(data: bytes, offset: int, size: int) -> bytes:
    """Wandelt einen RGBA-Palettenblock in 256*3 RGB; fehlende Farben sind schwarz."""
    out = bytearray(768)
    colors = min(256, size // 4)
    for i in range(colors):
        src = offset + i * 4
        if src + 3 > len(data):
            break
        out[i * 3:i * 3 + 3] = data[src:src + 3]
    return bytes(out)


def _read_sprites(
    data: bytes,
    node_ofs: int,
    count: int,
    ldata_ofs: int,
    tdata_ofs: int,
    palettes: list[bytes],
) -> list[SffSprite]:
    """Liest und dekodiert alle Sprites in Dateireihenfolge."""
    sprites: list[SffSprite] = []
    for i in range(count):
        base = node_ofs + i * _SPRITE_NODE_SIZE
        if base + _SPRITE_NODE_SIZE > len(data):
            break
        group, number, width, height = struct.unpack_from("<HHHH", data, base)
        axis_x, axis_y = struct.unpack_from("<hh", data, base + 8)
        link = struct.unpack_from("<H", data, base + 12)[0]
        fmt = data[base + 14]
        coldepth = data[base + 15]
        offset, size = struct.unpack_from("<II", data, base + 16)
        pal_index, flags = struct.unpack_from("<HH", data, base + 24)
        offset += tdata_ofs if flags & 1 else ldata_ofs

        palette = palettes[pal_index] if pal_index < len(palettes) else b"\x00" * 768

        if size == 0 or fmt == FMT_LINKED:
            sprites.append(_linked_sprite(sprites, link, group, number, axis_x, axis_y))
            continue

        try:
            pixels, rgba = _decode(data, offset, size, fmt, coldepth, width, height)
        except (ValueError, OSError, struct.error) as ex:
            print(f"[sff-v2] WARNING: sprite {group},{number} (fmt {fmt}) nicht lesbar: {ex}")
            pixels, rgba = b"\x00" * max(1, width * height), None

        sprites.append(SffSprite(
            group=group, image=number,
            x_axis=axis_x, y_axis=axis_y,
            width=width, height=height,
            pixels=pixels, palette=palette,
            rgba=rgba,
        ))
    return sprites


def _linked_sprite(
    sprites: list[SffSprite],
    link: int,
    group: int,
    number: int,
    axis_x: int,
    axis_y: int,
) -> SffSprite:
    """Ein Sprite ohne eigene Daten teilt sich die Pixel eines frueheren Eintrags."""
    if 0 <= link < len(sprites):
        src = sprites[link]
        return SffSprite(
            group=group, image=number, x_axis=axis_x, y_axis=axis_y,
            width=src.width, height=src.height,
            pixels=src.pixels, palette=src.palette,
            linked_index=link, rgba=src.rgba,
        )
    return SffSprite(
        group=group, image=number, x_axis=axis_x, y_axis=axis_y,
        width=1, height=1, pixels=b"\x00", palette=b"\x00" * 768,
        linked_index=link,
    )


def _decode(
    data: bytes,
    offset: int,
    size: int,
    fmt: int,
    coldepth: int,
    width: int,
    height: int,
) -> tuple[bytes, bytes | None]:
    """Dekodiert einen Sprite-Datenblock zu (indizierte Pixel, RGBA oder None)."""
    expected = width * height

    if fmt == FMT_RAW:
        raw = data[offset:offset + size]
        if coldepth in (24, 32):
            return b"\x00" * expected, _raw_truecolor_to_rgba(raw, coldepth, expected)
        return _pad(raw, expected), None

    # Alle uebrigen Formate stellen ihrem Nutzdatenstrom eine 4-Byte-Laenge voran.
    payload = data[offset + 4:offset + size]

    if fmt == FMT_RLE8:
        return _pad(_rle8_decode(payload, expected), expected), None
    if fmt == FMT_RLE5:
        return _pad(_rle5_decode(payload, expected), expected), None
    if fmt == FMT_LZ5:
        return _pad(_lz5_decode(payload, expected), expected), None
    if fmt in (FMT_PNG8, FMT_PNG24, FMT_PNG32):
        return _decode_png(payload, fmt, expected)
    raise ValueError(f"unbekanntes SFF-v2-Format {fmt}")


def _decode_png(payload: bytes, fmt: int, expected: int) -> tuple[bytes, bytes | None]:
    """PNG8 liefert Palettenindizes, PNG24/32 fertige RGBA-Pixel."""
    with Image.open(io.BytesIO(payload)) as img:
        if fmt == FMT_PNG8:
            indexed = img if img.mode == "P" else img.convert("P")
            return _pad(indexed.tobytes(), expected), None
        return b"\x00" * expected, img.convert("RGBA").tobytes()


def _raw_truecolor_to_rgba(raw: bytes, coldepth: int, expected: int) -> bytes:
    """Baut aus rohen 24/32-Bit-Pixeln einen RGBA-Puffer."""
    if coldepth == 32:
        return _pad(raw, expected * 4)
    out = bytearray(expected * 4)
    for i in range(min(expected, len(raw) // 3)):
        out[i * 4:i * 4 + 3] = raw[i * 3:i * 3 + 3]
        out[i * 4 + 3] = 255
    return bytes(out)


def _pad(payload: bytes, size: int) -> bytes:
    """Kuerzt oder fuellt einen Puffer auf die erwartete Groesse."""
    if len(payload) == size:
        return payload
    if len(payload) > size:
        return payload[:size]
    return payload + b"\x00" * (size - len(payload))


def _rle8_decode(rle: bytes, expected: int) -> bytes:
    """RLE8: ``0b01xxxxxx`` startet einen Lauf, sonst steht das Byte fuer sich."""
    out = bytearray(expected)
    i = 0
    j = 0
    last = len(rle) - 1
    while j < expected and i <= last:
        n = 1
        d = rle[i]
        if i < last:
            i += 1
        if d & 0xC0 == 0x40:
            n = d & 0x3F
            d = rle[i]
            if i < last:
                i += 1
        while n > 0 and j < expected:
            out[j] = d
            j += 1
            n -= 1
    return bytes(out)


def _rle5_decode(rle: bytes, expected: int) -> bytes:
    """RLE5: Pakete aus Lauflaenge, Farbanzahl und 5-Bit-Farben mit 3-Bit-Laufteil."""
    out = bytearray(expected)
    i = 0
    j = 0
    last = len(rle) - 1
    while j < expected and i <= last:
        run = rle[i]
        if i < last:
            i += 1
        color_count = rle[i] & 0x7F
        color = 0
        if rle[i] >> 7 != 0:
            if i < last:
                i += 1
            color = rle[i]
        if i < last:
            i += 1
        while True:
            if j < expected:
                out[j] = color
                j += 1
            run -= 1
            if run < 0:
                color_count -= 1
                if color_count < 0:
                    break
                color = rle[i] & 0x1F
                run = rle[i] >> 5
                if i < last:
                    i += 1
    return bytes(out)


def _lz5_decode(rle: bytes, expected: int) -> bytes:
    """LZ5: Kontrollbyte je acht Pakete, danach Rueckwaerts-Kopien oder Farblaeufe."""
    out = bytearray(expected)
    if not rle:
        return bytes(out)
    i = 0
    j = 0
    last = len(rle) - 1
    control = rle[i]
    control_bit = 0
    recycle = 0
    recycle_bits = 0
    if i < last:
        i += 1

    while j < expected and i <= last:
        d = rle[i]
        if i < last:
            i += 1
        if control & (1 << control_bit):
            # Rueckwaerts-Kopie aus dem bereits geschriebenen Puffer.
            if d & 0x3F == 0:
                d = (d << 2 | rle[i]) + 1
                if i < last:
                    i += 1
                n = rle[i] + 2
                if i < last:
                    i += 1
            else:
                recycle |= (d & 0xC0) >> recycle_bits
                recycle_bits += 2
                n = d & 0x3F
                if recycle_bits < 8:
                    d = rle[i] + 1
                    if i < last:
                        i += 1
                else:
                    d = recycle + 1
                    recycle = 0
                    recycle_bits = 0
            while True:
                if j < expected and j - d >= 0:
                    out[j] = out[j - d]
                    j += 1
                elif j < expected:
                    j += 1
                n -= 1
                if n < 0:
                    break
        else:
            if d & 0xE0 == 0:
                n = rle[i] + 8
                if i < last:
                    i += 1
            else:
                n = d >> 5
                d &= 0x1F
            while n > 0 and j < expected:
                out[j] = d
                j += 1
                n -= 1
        control_bit += 1
        if control_bit >= 8:
            control = rle[i]
            control_bit = 0
            if i < last:
                i += 1
    return bytes(out)
