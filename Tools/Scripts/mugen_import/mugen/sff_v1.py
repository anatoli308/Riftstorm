"""SFF v1 reader.

SFF v1 layout (Elecbyte):

    Header (512 bytes):
        0-11   : "ElecbyteSpr\\0"
        12-15  : version bytes (ver3, ver2, ver1, ver0)
        16-19  : number of groups            (uint32 LE)
        20-23  : number of images            (uint32 LE)
        24-27  : offset of first subfile     (uint32 LE)
        28-31  : subfile header length (32)  (uint32 LE)
        32     : palette type (1 = shared)
        ...padding to 512

    Per subfile (32-byte header + PCX payload):
        0-3    : next subfile offset (0 == last)
        4-7    : payload length in bytes (PCX). 0 means "link to previous":
                 reuse pixels of the subfile at `linked_index`.
        8-9    : x axis (origin)
        10-11  : y axis (origin)
        12-13  : group number
        14-15  : image number
        16-17  : index of previous copy
        18     : use same palette as previous (1 = yes)
        19-31  : comments

PCX (Elecbyte uses 8-bit indexed, version 5):
    128-byte header, RLE-encoded scanlines, optional 769-byte trailer
    (0x0C + 256*3 RGB palette). When `use_same_palette` is set, we reuse
    the previously decoded palette.
"""

from __future__ import annotations

import io
import os
import struct
from dataclasses import dataclass, field
from typing import Optional

from PIL import Image


_SFF_MAGIC = b"ElecbyteSpr\x00"
_HEADER_SIZE = 512
_SUBFILE_HEADER_SIZE = 32


@dataclass
class SffSprite:
    """Single decoded sprite from an SFF v1 file."""

    group: int
    image: int
    x_axis: int
    y_axis: int
    width: int
    height: int
    pixels: bytes              # raw 8-bit indexed pixels (width*height)
    palette: bytes             # 256*3 RGB
    linked_index: int = -1     # -1 means standalone, otherwise index in sprites list
    transparent_index: int = 0 # MUGEN convention: palette index 0 is transparent

    def to_pil(self, transparent_index: Optional[int] = None) -> Image.Image:
        """Convert to a PIL RGBA image with transparent index made fully alpha=0."""
        idx = self.transparent_index if transparent_index is None else transparent_index
        img = Image.frombytes("P", (self.width, self.height), self.pixels)
        img.putpalette(self.palette)
        rgba = img.convert("RGBA")
        if idx is not None and 0 <= idx < 256:
            # Make the chosen palette index fully transparent.
            r, g, b = self.palette[idx * 3], self.palette[idx * 3 + 1], self.palette[idx * 3 + 2]
            data = rgba.load()
            for y in range(self.height):
                for x in range(self.width):
                    pr, pg, pb, _ = data[x, y]
                    if pr == r and pg == g and pb == b:
                        data[x, y] = (0, 0, 0, 0)
        return rgba


@dataclass
class SffFile:
    version: tuple[int, int, int, int]
    num_groups: int
    num_images: int
    palette_type: int
    sprites: list[SffSprite] = field(default_factory=list)

    def find(self, group: int, image: int) -> Optional[SffSprite]:
        for s in self.sprites:
            if s.group == group and s.image == image:
                return s
        return None


def read_sff_v1(path: str) -> SffFile:
    """Parse an SFF v1 file and return all sprites fully decoded.

    Raises ValueError if the file is not SFF v1.
    """
    with open(path, "rb") as fh:
        data = fh.read()

    if not data.startswith(_SFF_MAGIC):
        raise ValueError(f"Not an SFF file: {path}")

    ver = (data[12], data[13], data[14], data[15])
    # SFF v1 header: ver3=0, ver2=0, ver1=0, ver0=1 (bytes are reversed in spec).
    # SFF v2 has ver0=2. We only support v1 here.
    if data[15] != 1:
        raise ValueError(
            f"Unsupported SFF version {ver} (only v1 is supported). File: {path}"
        )

    num_groups = struct.unpack_from("<I", data, 16)[0]
    num_images = struct.unpack_from("<I", data, 20)[0]
    first_offset = struct.unpack_from("<I", data, 24)[0]
    subheader_len = struct.unpack_from("<I", data, 28)[0]
    palette_type = data[32]

    if subheader_len != _SUBFILE_HEADER_SIZE:
        # Some tools deviate; accept but warn via assertion path.
        subheader_len = _SUBFILE_HEADER_SIZE

    sff = SffFile(
        version=ver,
        num_groups=num_groups,
        num_images=num_images,
        palette_type=palette_type,
    )

    # Decode subfiles in order. We need ordered list because "linked" subfiles
    # reference earlier indices by position.
    raw_entries: list[tuple[int, int, int, int, int, int, int, bytes]] = []
    offset = first_offset
    safety = 0
    while offset != 0 and offset + _SUBFILE_HEADER_SIZE <= len(data):
        next_off = struct.unpack_from("<I", data, offset + 0)[0]
        length = struct.unpack_from("<I", data, offset + 4)[0]
        x_axis = struct.unpack_from("<h", data, offset + 8)[0]
        y_axis = struct.unpack_from("<h", data, offset + 10)[0]
        group = struct.unpack_from("<H", data, offset + 12)[0]
        image = struct.unpack_from("<H", data, offset + 14)[0]
        linked = struct.unpack_from("<H", data, offset + 16)[0]
        same_pal = data[offset + 18]

        payload_start = offset + _SUBFILE_HEADER_SIZE
        payload = data[payload_start:payload_start + length] if length > 0 else b""

        raw_entries.append(
            (group, image, x_axis, y_axis, linked, same_pal, length, payload)
        )

        if next_off == 0 or next_off <= offset:
            break
        offset = next_off
        safety += 1
        if safety > 1_000_000:
            raise ValueError("SFF parse aborted: too many subfiles (corrupt file?)")

    # Decode PCX payloads, resolving linked references and shared palettes.
    last_palette: Optional[bytes] = None
    decoded: list[SffSprite] = []
    for idx, (group, image, ax, ay, linked, same_pal, length, payload) in enumerate(raw_entries):
        if length == 0:
            # Linked sprite: reuse pixels + palette from another index.
            if 0 <= linked < len(decoded):
                src = decoded[linked]
                sprite = SffSprite(
                    group=group, image=image,
                    x_axis=ax, y_axis=ay,
                    width=src.width, height=src.height,
                    pixels=src.pixels, palette=src.palette,
                    linked_index=linked,
                )
            else:
                # Bad link: skip with a 1x1 placeholder so indices stay aligned.
                sprite = SffSprite(
                    group=group, image=image, x_axis=ax, y_axis=ay,
                    width=1, height=1, pixels=b"\x00",
                    palette=b"\x00" * 768, linked_index=linked,
                )
            decoded.append(sprite)
            continue

        width, height, pixels, palette = _decode_pcx(payload)
        if same_pal and last_palette is not None:
            palette = last_palette
        last_palette = palette

        decoded.append(SffSprite(
            group=group, image=image,
            x_axis=ax, y_axis=ay,
            width=width, height=height,
            pixels=pixels, palette=palette,
        ))

    sff.sprites = decoded
    return sff


def _decode_pcx(payload: bytes) -> tuple[int, int, bytes, bytes]:
    """Decode an 8-bit indexed PCX blob.

    Returns (width, height, pixels, palette) where pixels is width*height bytes
    and palette is 256*3 RGB bytes.
    """
    if len(payload) < 128:
        raise ValueError("PCX payload too small")

    manufacturer = payload[0]
    version = payload[1]
    encoding = payload[2]
    bits_per_plane = payload[3]
    xmin = struct.unpack_from("<H", payload, 4)[0]
    ymin = struct.unpack_from("<H", payload, 6)[0]
    xmax = struct.unpack_from("<H", payload, 8)[0]
    ymax = struct.unpack_from("<H", payload, 10)[0]
    planes = payload[65]
    bytes_per_line = struct.unpack_from("<H", payload, 66)[0]

    if manufacturer != 0x0A:
        raise ValueError("Not a PCX file (bad manufacturer byte)")
    if bits_per_plane != 8 or planes != 1:
        raise ValueError(
            f"Unsupported PCX format: bits={bits_per_plane}, planes={planes}"
        )

    width = xmax - xmin + 1
    height = ymax - ymin + 1
    total_bytes = bytes_per_line * height

    # RLE decode.
    out = bytearray(total_bytes)
    src = payload[128:]
    si = 0
    di = 0
    src_len = len(src)
    while di < total_bytes and si < src_len:
        b = src[si]
        si += 1
        if encoding == 1 and (b & 0xC0) == 0xC0:
            run = b & 0x3F
            if si >= src_len:
                break
            val = src[si]
            si += 1
            end = min(di + run, total_bytes)
            for k in range(di, end):
                out[k] = val
            di = end
        else:
            out[di] = b
            di += 1

    # Trim each scanline to actual width.
    if bytes_per_line == width:
        pixels = bytes(out)
    else:
        rows = []
        for y in range(height):
            row_start = y * bytes_per_line
            rows.append(out[row_start:row_start + width])
        pixels = bytes(b"".join(rows))

    # Palette: last 769 bytes if file ends with 0x0C marker.
    palette: bytes
    if len(payload) >= 769 and payload[-769] == 0x0C:
        palette = payload[-768:]
    else:
        # Fallback: 16-color palette in header at offset 16 (rare for MUGEN).
        small = payload[16:64]
        palette = bytes(small + b"\x00" * (768 - len(small)))

    return width, height, pixels, palette
