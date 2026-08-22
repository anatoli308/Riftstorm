"""MUGEN stage (.def) parser.

A stage definition supplies the numbers the character files deliberately leave
open: where the two fighters stand, where the floor is, and how wide the camera
window is. Without them every screen-anchored ``Explod`` (``postype = back``,
``left``, ``right``, ``front``) and every projectile distance is guesswork.

Relevant sections::

    [Info]        name
    [Camera]      startx/starty, boundleft/boundright, boundhigh/boundlow
    [PlayerInfo]  p1startx/p1starty, p2startx/p2starty, leftbound/rightbound
    [StageInfo]   zoffset, localcoord, xscale/yscale
    [Bound]       screenleft/screenright
    [BGdef]       spr  -> the stage .sff
    [BG <name>]   one background layer
    [Begin Action <n>]  AIR-syntax animations used by ``type = anim`` layers

Coordinates match the character space already used elsewhere: x = 0 is the
centre of the camera window, y counts downward from the top of the screen, so
the floor sits at ``zoffset``.
"""

from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from typing import Optional

from .air import AirAction, parse_air

_SECTION_RE = re.compile(r"^\[(.+?)\]")
_DEFAULT_LOCALCOORD = (320.0, 240.0)


@dataclass
class StageBgLayer:
    """One ``[BG ...]`` block: a sprite or animation pinned to stage space."""

    name: str
    kind: str = "normal"                 # normal | anim | parallax
    sprite: str = ""                     # "group_image" for kind == normal
    action: Optional[int] = None         # action number for kind == anim
    start: tuple[float, float] = (0.0, 0.0)
    delta: tuple[float, float] = (1.0, 1.0)
    scale: tuple[float, float] = (1.0, 1.0)
    tile: tuple[int, int] = (0, 0)       # repeat count; 1 = repeat to fill
    layer: int = 0                       # 1 = in front of the characters
    mask: bool = False
    trans: str = ""


@dataclass
class StageDef:
    """Everything the runtime needs to place fighters and draw the backdrop."""

    name: str = ""
    localcoord: tuple[float, float] = _DEFAULT_LOCALCOORD
    zoffset: float = 0.0
    p1start: tuple[float, float] = (0.0, 0.0)
    p2start: tuple[float, float] = (0.0, 0.0)
    camera_start: tuple[float, float] = (0.0, 0.0)
    camera_bounds: tuple[float, float, float, float] = (0.0, 0.0, 0.0, 0.0)
    screen_bounds: tuple[float, float] = (0.0, 0.0)
    sff: str = ""
    layers: list[StageBgLayer] = field(default_factory=list)
    actions: list[AirAction] = field(default_factory=list)

    @property
    def player_distance(self) -> float:
        """Horizontal gap between the two starting positions, in stage pixels."""
        return abs(self.p2start[0] - self.p1start[0])


def parse_stage_def(path: str) -> StageDef:
    """Read a stage ``.def``. Inline ``[Begin Action]`` blocks are parsed too."""
    sections = _read_sections(path)
    stage = StageDef()

    info = sections.get("info", {})
    stage.name = _unquote(info.get("name", os.path.basename(path)))

    stage_info = sections.get("stageinfo", {})
    stage.localcoord = _pair(stage_info.get("localcoord"), _DEFAULT_LOCALCOORD)
    stage.zoffset = _number(stage_info.get("zoffset"), 0.0)

    players = sections.get("playerinfo", {})
    stage.p1start = (
        _number(players.get("p1startx"), 0.0),
        _number(players.get("p1starty"), 0.0),
    )
    stage.p2start = (
        _number(players.get("p2startx"), 0.0),
        _number(players.get("p2starty"), 0.0),
    )

    camera = sections.get("camera", {})
    stage.camera_start = (
        _number(camera.get("startx"), 0.0),
        _number(camera.get("starty"), 0.0),
    )
    stage.camera_bounds = (
        _number(camera.get("boundleft"), 0.0),
        _number(camera.get("boundright"), 0.0),
        _number(camera.get("boundhigh"), 0.0),
        _number(camera.get("boundlow"), 0.0),
    )

    bound = sections.get("bound", {})
    stage.screen_bounds = (
        _number(bound.get("screenleft"), 0.0),
        _number(bound.get("screenright"), 0.0),
    )

    stage.sff = _resolve_sff(path, sections.get("bgdef", {}).get("spr", ""))
    stage.layers = [
        _bg_layer(name, values)
        for name, values in sections.items()
        if name.startswith("bg ")
    ]
    stage.actions = parse_air(path)
    return stage


def _bg_layer(section: str, values: dict[str, str]) -> StageBgLayer:
    kind = (values.get("type") or "normal").strip().lower()
    action_no = values.get("actionno")
    return StageBgLayer(
        name=section[3:].strip() or section,
        kind=kind,
        sprite=_sprite_key(values.get("spriteno")),
        action=int(_number(action_no, 0.0)) if action_no is not None else None,
        start=_pair(values.get("start"), (0.0, 0.0)),
        delta=_pair(values.get("delta"), (1.0, 1.0)),
        scale=_pair(values.get("scalestart"), (1.0, 1.0)),
        tile=_int_pair(values.get("tile")),
        layer=int(_number(values.get("layerno"), 0.0)),
        mask=_number(values.get("mask"), 0.0) != 0.0,
        trans=(values.get("trans") or "").strip().lower(),
    )


def _read_sections(path: str) -> dict[str, dict[str, str]]:
    """Section name (lowercased) -> key/value map. Later duplicates win."""
    sections: dict[str, dict[str, str]] = {}
    current: Optional[dict[str, str]] = None
    for raw in _read_text(path).splitlines():
        line = raw.split(";", 1)[0].strip()
        if not line:
            continue
        head = _SECTION_RE.match(line)
        if head:
            name = head.group(1).strip().lower()
            if name.startswith("begin action"):
                current = None
                continue
            # Stages reuse a BG name for several layers ("[BG light]" twice in
            # sewer.def); each one must survive as its own entry.
            if name.startswith("bg ") and name in sections:
                suffix = 2
                while f"{name} #{suffix}" in sections:
                    suffix += 1
                name = f"{name} #{suffix}"
            current = sections.setdefault(name, {})
            continue
        if current is None or "=" not in line:
            continue
        key, value = line.split("=", 1)
        current[key.strip().lower()] = value.strip()
    return sections


def _read_text(path: str) -> str:
    with open(path, "rb") as fh:
        return fh.read().decode("latin-1", errors="ignore")


def _sprite_key(value: Optional[str]) -> str:
    if not value:
        return ""
    parts = [p.strip() for p in value.split(",")]
    if len(parts) < 2:
        return ""
    return f"{int(_number(parts[0], 0.0))}_{int(_number(parts[1], 0.0))}"


def _number(value: Optional[str], fallback: float) -> float:
    if value is None:
        return fallback
    match = re.search(r"-?\d*\.?\d+", str(value))
    return float(match.group(0)) if match else fallback


def _pair(value: Optional[str], fallback: tuple[float, float]) -> tuple[float, float]:
    if not value:
        return fallback
    parts = [p.strip() for p in str(value).split(",")]
    x = _number(parts[0] if parts else None, fallback[0])
    y = _number(parts[1] if len(parts) > 1 else None, fallback[1])
    return (x, y)


def _int_pair(value: Optional[str]) -> tuple[int, int]:
    x, y = _pair(value, (0.0, 0.0))
    return (int(x), int(y))


def _unquote(value: str) -> str:
    return value.strip().strip('"').strip()


def _resolve_sff(def_path: str, declared: str) -> str:
    """The declared path is MUGEN-root relative; our stages ship self-contained."""
    folder = os.path.dirname(def_path)
    candidates = []
    if declared:
        candidates.append(os.path.join(folder, declared))
        candidates.append(os.path.join(folder, os.path.basename(declared)))
    candidates.append(os.path.splitext(def_path)[0] + ".sff")
    for candidate in candidates:
        if os.path.isfile(candidate):
            return candidate
    for entry in sorted(os.listdir(folder)):
        if entry.lower().endswith(".sff"):
            return os.path.join(folder, entry)
    return ""
