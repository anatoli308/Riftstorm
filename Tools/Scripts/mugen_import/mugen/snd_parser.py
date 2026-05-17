"""MUGEN .snd sound file parser.

Binary layout (little-endian):

    Offset  Size  Field
    ------  ----  -----
    0       12    Signature "ElecbyteSnd\\0"
    12       4    Version (verhi/verlo packed)
    16       4    Number of sounds
    20       4    Offset to first subfile
    24       n    Free-text comment / padding up to first subfile

    Each subfile (16-byte header + payload):
        +0    4   Next subfile offset (0 = end of chain)
        +4    4   Payload length in bytes (size of WAV payload)
        +8    4   Group number
        +12   4   Sample number within group
        +16   N   RIFF WAV payload

We extract each WAV to a flat directory keyed by `<group>_<sample>.wav`.
"""

from __future__ import annotations

import os
import struct
from dataclasses import dataclass, field


@dataclass
class SndSubfile:
    group: int
    sample: int
    offset: int             # absolute byte offset of payload start
    length: int             # payload byte length
    data: bytes = b""       # RIFF WAV bytes


@dataclass
class SndFile:
    version: int = 0
    num_sounds: int = 0
    first_offset: int = 0
    comment: str = ""
    subfiles: list[SndSubfile] = field(default_factory=list)


_SIGNATURE = b"ElecbyteSnd\x00"


def read_snd(path: str) -> SndFile:
    """Read a MUGEN .snd file and return all subfiles with payload bytes."""
    with open(path, "rb") as fh:
        raw = fh.read()

    if len(raw) < 24 or not raw.startswith(_SIGNATURE):
        raise ValueError(f"Not a MUGEN SND file: {path}")

    version, num_sounds, first_offset = struct.unpack_from("<III", raw, 12)
    snd = SndFile(
        version=version,
        num_sounds=num_sounds,
        first_offset=first_offset,
    )

    # Free-text region between header end (24) and first subfile.
    if 24 <= first_offset <= len(raw):
        snd.comment = raw[24:first_offset].split(b"\x00", 1)[0].decode(
            "latin-1", errors="ignore"
        ).strip()

    visited: set[int] = set()
    cursor = first_offset
    while 0 < cursor < len(raw) - 16:
        if cursor in visited:
            break       # defensive against cyclic next-pointers
        visited.add(cursor)
        next_off, length, group, sample = struct.unpack_from("<iiii", raw, cursor)
        payload_start = cursor + 16
        payload_end = payload_start + length
        if length < 0 or payload_end > len(raw):
            break
        data = raw[payload_start:payload_end]
        snd.subfiles.append(SndSubfile(
            group=group,
            sample=sample,
            offset=payload_start,
            length=length,
            data=data,
        ))
        if next_off == 0:
            break
        cursor = next_off

    return snd


def export_wavs(snd: SndFile, out_dir: str) -> list[str | None]:
    """Write each RIFF subfile as `<group>_<sample>.wav`.

    Returns a list parallel to `snd.subfiles`: the relative filename for each
    subfile, or `None` if that subfile had no valid RIFF payload.
    """
    os.makedirs(out_dir, exist_ok=True)
    written: list[str | None] = []
    for sf in snd.subfiles:
        if not sf.data.startswith(b"RIFF"):
            written.append(None)
            continue
        filename = f"{sf.group}_{sf.sample}.wav"
        with open(os.path.join(out_dir, filename), "wb") as fh:
            fh.write(sf.data)
        written.append(filename)
    return written


def to_jsonable(snd: SndFile, files: list[str | None]) -> dict:
    return {
        "version": snd.version,
        "num_sounds": snd.num_sounds,
        "comment": snd.comment,
        "sounds": [
            {
                "group": sf.group,
                "sample": sf.sample,
                "length": sf.length,
                "file": (
                    files[i] if i < len(files) and files[i] else None
                ),
            }
            for i, sf in enumerate(snd.subfiles)
        ],
    }
