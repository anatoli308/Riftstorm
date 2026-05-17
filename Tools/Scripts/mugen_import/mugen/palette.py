"""MUGEN palette (.act) loader.

An .act file is the Adobe Color Table format: exactly 768 bytes = 256 RGB triplets.
MUGEN treats palette index 0 as transparent.
"""

from __future__ import annotations

import os
from dataclasses import dataclass


RGB = tuple[int, int, int]


@dataclass
class Palette:
    """A 256-color palette. Index 0 is the transparent color in MUGEN."""

    name: str
    colors: list[RGB]   # length 256
    source: str         # absolute path to source file

    def as_flat_rgb(self) -> bytes:
        out = bytearray(256 * 3)
        for i, (r, g, b) in enumerate(self.colors):
            out[i * 3] = r
            out[i * 3 + 1] = g
            out[i * 3 + 2] = b
        return bytes(out)


def load_act(path: str, name: str = "") -> Palette:
    """Load a single .act palette file. Must be exactly 768 bytes."""
    with open(path, "rb") as fh:
        raw = fh.read()
    if len(raw) < 768:
        raise ValueError(f"ACT file too small ({len(raw)} bytes): {path}")
    colors: list[RGB] = []
    for i in range(256):
        r = raw[i * 3]
        g = raw[i * 3 + 1]
        b = raw[i * 3 + 2]
        colors.append((r, g, b))
    return Palette(
        name=name or os.path.splitext(os.path.basename(path))[0],
        colors=colors,
        source=os.path.abspath(path),
    )


def discover_palettes(def_dir: str, pal_refs: dict[int, str]) -> list[Palette]:
    """Resolve palette file references from a parsed .def.

    pal_refs is the {slot: relative_path} map from CharDef.palettes.
    Files are searched relative to def_dir, including its `palette/` subfolder.
    Missing files are silently skipped (MUGEN allows sparse palette slots).
    """
    palettes: list[Palette] = []
    for slot, rel in sorted(pal_refs.items()):
        resolved = _resolve(def_dir, rel)
        if resolved is None:
            continue
        pal = load_act(resolved, name=f"pal{slot}")
        palettes.append(pal)
    return palettes


def _resolve(def_dir: str, rel: str) -> str | None:
    """Try common MUGEN palette path conventions."""
    candidates = [
        os.path.join(def_dir, rel),
        os.path.join(def_dir, "palette", rel),
        os.path.join(def_dir, "palettes", rel),
    ]
    for c in candidates:
        if os.path.isfile(c):
            return c
    return None
