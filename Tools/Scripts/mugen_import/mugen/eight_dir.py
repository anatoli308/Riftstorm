"""8-direction layout generator.

MUGEN characters are side-view 2D fighters: they only have profile sprites
(facing right by convention). For a topdown / isometric game we fake the
remaining seven directions by mirroring and aliasing:

    direction    visual source           flip
    ---------    ---------------------   ----------
    E            original frame          none
    W            original frame          flipH
    NE           original frame          none   (alias of E)
    SE           original frame          none   (alias of E)
    NW           original frame          flipH  (alias of W)
    SW           original frame          flipH  (alias of W)
    N            original frame          none   (alias of E, fake=True)
    S            original frame          none   (alias of E, fake=True)

The `fake` flag tells Unity that there is no real back/front sprite, so the
runtime can swap in a placeholder, hide the sprite, or draw a shadow only.

The output JSON groups animations per-action with one frame list per
direction. Each frame entry references an atlas sprite by `key`, plus
duration / offset / blend metadata copied from the .air file.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from typing import Iterable

from .air import AirAction, AirFrame


DIRECTIONS_8 = ("E", "NE", "N", "NW", "W", "SW", "S", "SE")
_MUGEN_TICK_SECONDS = 1.0 / 60.0


@dataclass
class DirFrame:
    sprite_key: str
    duration: float        # seconds
    offset_x: int
    offset_y: int
    flip_h: bool
    flip_v: bool
    blend: str
    fake: bool = False     # true when this direction is faked from the side view
    # Per-frame collision boxes from the MUGEN .air file, in pixel coordinates
    # relative to the sprite axis. Clsn1 = attack boxes, Clsn2 = hurt boxes.
    # Each tuple is (x1, y1, x2, y2). For mirrored directions (W/NW/SW) the
    # boxes are already x-flipped so consumers can use them as-is.
    clsn1: list[tuple[int, int, int, int]] = field(default_factory=list)
    clsn2: list[tuple[int, int, int, int]] = field(default_factory=list)


@dataclass
class DirAnimation:
    direction: str
    frames: list[DirFrame] = field(default_factory=list)
    loop_start: int = 0


@dataclass
class ActionExport:
    number: int
    name: str                                      # human label if known, else action_<n>
    directions: dict[str, DirAnimation] = field(default_factory=dict)


# Optional friendly names for well-known MUGEN action numbers. The runtime can
# ignore this and look up by number, but it makes the JSON readable.
_KNOWN_ACTIONS: dict[int, str] = {
    0: "stand",
    5: "stand_turn",
    6: "crouch_turn",
    10: "stand_to_crouch",
    11: "crouch",
    12: "crouch_to_stand",
    20: "walk_forward",
    21: "walk_back",
    40: "jump_start",
    41: "jump_up",
    42: "jump_forward",
    43: "jump_back",
    50: "jump_land",
    100: "run_forward",
    105: "hop_back",
    200: "attack_light",
    210: "attack_medium",
    220: "attack_heavy",
    5000: "hit_light",
    5010: "hit_medium",
    5020: "hit_heavy",
    5050: "fall",
    5100: "lie_down",
    5110: "get_up",
}


def build_8dir(
    actions: Iterable[AirAction],
    atlas_keys: set[str],
) -> list[ActionExport]:
    """Convert AIR actions into 8-direction animation lists.

    Frames whose (group,image) is not present in `atlas_keys` are dropped to
    keep the output safe to consume in Unity.
    """
    exported: list[ActionExport] = []

    for action in actions:
        name = _KNOWN_ACTIONS.get(action.number, f"action_{action.number}")
        export = ActionExport(number=action.number, name=name)

        for direction in DIRECTIONS_8:
            anim = _build_direction(action, direction, atlas_keys)
            export.directions[direction] = anim

        exported.append(export)

    return exported


def _build_direction(
    action: AirAction,
    direction: str,
    atlas_keys: set[str],
) -> DirAnimation:
    # Aliasing strategy: we always start from the side-view frames. East is
    # canonical; West mirrors horizontally; N/NE/NW/S/SE/SW alias to East
    # (with the appropriate flip for the western half) and are flagged fake
    # for N/S where the silhouette would be wrong.
    flip_for_dir = {
        "E": (False, False, False),   # (extra_flip_h, extra_flip_v, fake)
        "NE": (False, False, False),
        "SE": (False, False, False),
        "W": (True, False, False),
        "NW": (True, False, False),
        "SW": (True, False, False),
        "N": (False, False, True),
        "S": (False, False, True),
    }
    extra_flip_h, extra_flip_v, fake = flip_for_dir[direction]

    out_frames: list[DirFrame] = []
    skipped_before_loop = 0

    for idx, f in enumerate(action.frames):
        if f.ticks == 0 or f.ticks < -1:
            # Skip-marker frames in MUGEN; we drop them but keep loop_start aligned.
            if idx < action.loop_start:
                skipped_before_loop += 1
            continue

        key = f"{f.group}_{f.image}"
        if key not in atlas_keys:
            if idx < action.loop_start:
                skipped_before_loop += 1
            continue

        duration = (max(f.ticks, 1) * _MUGEN_TICK_SECONDS) if f.ticks != -1 else -1.0
        # For mirrored directions we also mirror the per-frame x offset so the
        # pivot stays consistent.
        off_x = -f.x_off if extra_flip_h else f.x_off

        # Mirror collision boxes horizontally for W/NW/SW so they line up with
        # the mirrored sprite. (x1,y1,x2,y2) -> (-x2,y1,-x1,y2). Vertical mirror
        # is defensive only; extra_flip_v is currently never true.
        def _mirror(boxes: list[tuple[int, int, int, int]]) -> list[tuple[int, int, int, int]]:
            if not boxes:
                return []
            result: list[tuple[int, int, int, int]] = []
            for x1, y1, x2, y2 in boxes:
                if extra_flip_h:
                    x1, x2 = -x2, -x1
                if extra_flip_v:
                    y1, y2 = -y2, -y1
                result.append((x1, y1, x2, y2))
            return result

        out_frames.append(DirFrame(
            sprite_key=key,
            duration=duration,
            offset_x=off_x,
            offset_y=f.y_off,
            flip_h=f.flip_h ^ extra_flip_h,
            flip_v=f.flip_v ^ extra_flip_v,
            blend=f.blend,
            fake=fake,
            clsn1=_mirror(list(f.clsn1)),
            clsn2=_mirror(list(f.clsn2)),
        ))

    return DirAnimation(
        direction=direction,
        frames=out_frames,
        loop_start=max(action.loop_start - skipped_before_loop, 0),
    )


def save_animations_json(
    actions: list[ActionExport],
    out_dir: str,
    basename: str = "animations",
) -> str:
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"{basename}.json")

    payload = {
        "directions": list(DIRECTIONS_8),
        "tickSeconds": _MUGEN_TICK_SECONDS,
        "actions": [
            {
                "number": a.number,
                "name": a.name,
                "directions": {
                    d: {
                        "loopStart": a.directions[d].loop_start,
                        "frames": [
                            {
                                "sprite": fr.sprite_key,
                                "duration": fr.duration,
                                "offsetX": fr.offset_x,
                                "offsetY": fr.offset_y,
                                "flipH": fr.flip_h,
                                "flipV": fr.flip_v,
                                "blend": fr.blend,
                                "fake": fr.fake,
                                "attackBoxes": [list(b) for b in fr.clsn1],
                                "hurtBoxes": [list(b) for b in fr.clsn2],
                            }
                            for fr in a.directions[d].frames
                        ],
                    }
                    for d in DIRECTIONS_8
                },
            }
            for a in actions
        ],
    }

    with open(path, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)
    return path
