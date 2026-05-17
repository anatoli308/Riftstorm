"""AIR animation file parser.

AIR layout:

    [Begin Action <N>]
    [Clsn1Default: <count>]                ; optional, default attack boxes
    [ Clsn1[i] = x1,y1,x2,y2 ]
    [Clsn2Default: <count>]                ; optional, default hurt boxes
    [ Clsn2[i] = x1,y1,x2,y2 ]
    [Clsn1: <count>]                       ; optional, override for next frame
    [ Clsn1[i] = x1,y1,x2,y2 ]
    [Clsn2: <count>]                       ; optional, override for next frame
    [ Clsn2[i] = x1,y1,x2,y2 ]
    <group>,<image>, <x>,<y>, <ticks>[, <flip>][, <blend>]
    ...
    LoopStart                              ; optional, marks loop return point

Notes:
    * ticks  : -1 means "hold forever"; <= 0 generally means single tick or skip
    * flip   : H, V, HV/VH
    * blend  : A (additive), A1 (alpha), S (subtractive), AS###D### (custom)
    * Clsn1  : attack boxes ("red"). Clsn2: hurt boxes ("blue").
    * `Default` blocks apply to every following frame until replaced.
    * Non-default blocks apply ONLY to the next frame line.
    * Box coordinates are sprite-axis-relative pixels (x1, y1, x2, y2).
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Optional


ClsnBox = tuple[int, int, int, int]


@dataclass
class AirFrame:
    group: int
    image: int
    x_off: int = 0
    y_off: int = 0
    ticks: int = 1          # display duration in MUGEN ticks (1 tick = 1/60 s)
    flip_h: bool = False
    flip_v: bool = False
    blend: str = ""         # "", "A", "A1", "S", "AS<src>D<dst>"
    clsn1: list[ClsnBox] = field(default_factory=list)  # attack boxes
    clsn2: list[ClsnBox] = field(default_factory=list)  # hurt boxes


@dataclass
class AirAction:
    number: int
    frames: list[AirFrame] = field(default_factory=list)
    loop_start: int = 0     # frame index to loop back to


_ACTION_RE = re.compile(r"^\s*\[\s*Begin\s+Action\s+(-?\d+)\s*\]", re.IGNORECASE)
_LOOPSTART_RE = re.compile(r"^\s*LoopStart\s*$", re.IGNORECASE)
_COMMENT_RE = re.compile(r";.*$")
_CLSN_HEADER_RE = re.compile(
    r"^\s*Clsn([12])(Default)?\s*:\s*(\d+)\s*$", re.IGNORECASE
)
_CLSN_ROW_RE = re.compile(
    r"^\s*Clsn([12])\s*\[\s*\d+\s*\]\s*=\s*"
    r"(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*$",
    re.IGNORECASE,
)


def parse_air(path: str) -> list[AirAction]:
    """Parse a .air file. Tolerant of Shift-JIS / mojibake comments."""
    text = _read_text(path)

    actions: list[AirAction] = []
    current: Optional[AirAction] = None

    # Per-action collision state. Reset on every [Begin Action].
    default_clsn1: list[ClsnBox] = []
    default_clsn2: list[ClsnBox] = []
    pending_clsn1: Optional[list[ClsnBox]] = None
    pending_clsn2: Optional[list[ClsnBox]] = None
    collect_kind: Optional[str] = None  # "clsn1_def" | "clsn2_def" | "clsn1" | "clsn2"
    collect_remaining: int = 0
    collect_buffer: list[ClsnBox] = []

    for raw in text.splitlines():
        line = _COMMENT_RE.sub("", raw).strip()
        if not line:
            continue

        m = _ACTION_RE.match(line)
        if m:
            current = AirAction(number=int(m.group(1)))
            actions.append(current)
            default_clsn1 = []
            default_clsn2 = []
            pending_clsn1 = None
            pending_clsn2 = None
            collect_kind = None
            collect_remaining = 0
            collect_buffer = []
            continue

        if current is None:
            continue

        # Collecting Clsn rows after a header.
        if collect_kind is not None:
            row = _CLSN_ROW_RE.match(line)
            if row is not None:
                box: ClsnBox = (
                    int(row.group(2)), int(row.group(3)),
                    int(row.group(4)), int(row.group(5)),
                )
                collect_buffer.append(box)
                collect_remaining -= 1
                if collect_remaining <= 0:
                    if collect_kind == "clsn1_def":
                        default_clsn1 = list(collect_buffer)
                    elif collect_kind == "clsn2_def":
                        default_clsn2 = list(collect_buffer)
                    elif collect_kind == "clsn1":
                        pending_clsn1 = list(collect_buffer)
                    elif collect_kind == "clsn2":
                        pending_clsn2 = list(collect_buffer)
                    collect_kind = None
                    collect_buffer = []
                continue
            # Unexpected line during collection — abort collection and fall through.
            collect_kind = None
            collect_buffer = []

        header = _CLSN_HEADER_RE.match(line)
        if header is not None:
            kind = header.group(1)
            is_default = header.group(2) is not None
            count = int(header.group(3))
            collect_kind = f"clsn{kind}_def" if is_default else f"clsn{kind}"
            collect_remaining = count
            collect_buffer = []
            if count == 0:
                if collect_kind == "clsn1_def":
                    default_clsn1 = []
                elif collect_kind == "clsn2_def":
                    default_clsn2 = []
                elif collect_kind == "clsn1":
                    pending_clsn1 = []
                elif collect_kind == "clsn2":
                    pending_clsn2 = []
                collect_kind = None
            continue

        if _LOOPSTART_RE.match(line):
            current.loop_start = len(current.frames)
            continue

        frame = _parse_frame_line(line)
        if frame is None:
            continue

        frame.clsn1 = (
            list(pending_clsn1) if pending_clsn1 is not None else list(default_clsn1)
        )
        frame.clsn2 = (
            list(pending_clsn2) if pending_clsn2 is not None else list(default_clsn2)
        )
        pending_clsn1 = None
        pending_clsn2 = None

        current.frames.append(frame)

    return actions


def _parse_frame_line(line: str) -> Optional[AirFrame]:
    """Parse one AIR frame row.

    Format (commas, with optional flip + blend fields):
        group, image, x, y, ticks [, flip] [, blend]
    """
    parts = [p.strip() for p in line.split(",")]
    if len(parts) < 5:
        return None
    try:
        group = int(parts[0])
        image = int(parts[1])
        x_off = int(parts[2])
        y_off = int(parts[3])
        ticks = int(parts[4])
    except ValueError:
        return None

    flip_h = False
    flip_v = False
    blend = ""

    if len(parts) >= 6 and parts[5]:
        flip_token = parts[5].upper()
        flip_h = "H" in flip_token
        flip_v = "V" in flip_token

    if len(parts) >= 7 and parts[6]:
        blend = parts[6].upper()

    return AirFrame(
        group=group, image=image,
        x_off=x_off, y_off=y_off,
        ticks=ticks,
        flip_h=flip_h, flip_v=flip_v,
        blend=blend,
    )


def _read_text(path: str) -> str:
    """Read text robustly. MUGEN char files are typically Shift-JIS or CP1252;
    we only need the structural ASCII parts, so fall back to latin-1."""
    with open(path, "rb") as fh:
        raw = fh.read()
    for enc in ("utf-8-sig", "shift_jis", "cp1252", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1", errors="ignore")
