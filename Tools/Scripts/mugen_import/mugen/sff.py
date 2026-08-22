"""Waehlt anhand des Versionsbytes den passenden SFF-Reader."""

from __future__ import annotations

from .sff_v1 import SffFile, SffSprite, read_sff_v1
from .sff_v2 import read_sff_v2

__all__ = ["SffFile", "SffSprite", "read_sff", "sff_version"]


def sff_version(path: str) -> int:
    """Liest das Haupt-Versionsbyte des SFF-Kopfes (1 oder 2)."""
    with open(path, "rb") as fh:
        header = fh.read(16)
    if len(header) < 16 or not header.startswith(b"ElecbyteSpr\x00"):
        raise ValueError(f"Not an SFF file: {path}")
    return header[15]


def read_sff(path: str) -> SffFile:
    """Laedt ein SFF beliebiger Version und liefert die dekodierten Sprites."""
    version = sff_version(path)
    if version == 1:
        return read_sff_v1(path)
    if version == 2:
        return read_sff_v2(path)
    raise ValueError(f"Unsupported SFF version {version}: {path}")
