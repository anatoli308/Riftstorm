"""Convert MUGEN importer output to Riftstorm FLARE NPC JSON.

Reads:
  <char_dir>/animations.json
  <char_dir>/atlas.sprites.json

Writes:
  <char_dir>/<atlas_name>.json   (FLARE schema, image="atlas.png")

The FLARE schema (see FlareAtlasData.cs) is:
  {
    "image": "atlas.png",
    "animations": {
      "<name>": {
        "frames_count": N,
        "duration_ms": M,
        "type": "looped" | "play_once" | "back_forth",
        "frames": [ [ {x,y,w,h,ox,oy} * 8 dirs ], ... per frame ]
      }
    }
  }

FLARE direction order: 0=W 1=SW 2=S 3=SE 4=E 5=NE 6=N 7=NW.

Every MUGEN action is exported twice:
  * under the generic key "action_<number>"  (lossless, full coverage)
  * additionally under a canonical FLARE alias (e.g. "stance", "run", "swing",
    "hit", "die") if the action matches the well-known CNS convention.

A MUGEN frame with a non-positive duration (CNS uses -1 to mean "hold")
flips the animation type to "play_once"; otherwise it is "looped".

Direction modes (--directions, default 2):
  * 2 -> reine 2D-Seitenansicht. Es existiert nur die E-Variante; alle
    8 FLARE-Slots werden aus E synthetisiert. Linksseitige Slots (W, SW, NW)
    werden horizontal gespiegelt, alle anderen (E, NE, SE, N, S) unflipped
    übernommen. Konsistent, kein Rotations-Artefakt. Standard für MUGEN.
  * 8 -> klassisches Verhalten: per-direction MUGEN-Frames werden gelesen,
    fehlende Richtungen fallen über die Mugen-Kette auf E zurück. Nur
    sinnvoll bei tatsächlich 8-dir-animierten Quellen (sehr selten).

Character Reserved Action Numbers

M.U.G.E.N's common state engine (data/common1.cns) requires several animation actions to be present in your character (a warning will be logged if not all required animations are present). There are also some actions that are optional, which will be made use of if present in your character.

If you are unsure of how any of these actions should look, take a look at chars/kfm/kfm.air.

All action numbers in the 5000-5999 range not listed below are reserved for possible future use, so avoid using those numbers for custom actions.

An "opt" besides the number means the animation is optional.
Number 	Description 	Comments
0 	Standing 	 
5 	Stand turning 	Must have finite looptime
6 	Crouch turning 	Must have finite looptime
10 	Stand to crouch 	Finite looptime
11 	Crouching 	 
12 	Crouch to stand 	Finite looptime
20 	Walking forwards 	 
21 	Walking backwards 	 
40 	Jump start (on ground) 	Shown just before player leaves ground
41 	Jump neutral (upwards) 	Shown when player is going up
42 	Jump forwards (upwards) 	Shown when player is going up-towards
43 	Jump back (upwards) 	Shown when player is going up-back
44 opt 	Jump neutral (downwards) 	Activated when Y-velocity > -2
45 opt 	Jump fwd (downwards) 	Same as above
46 opt 	Jump back (downwards) 	Same as above
47 	Jump landing 	Shown as player lands on ground
100 	Run fwd/hop forward 	 
105 	Hop back 	 
120 	Start guarding (stand) 	Finite looptime
121 	Start guarding (crouch) 	Finite looptime
122 	Start guarding (air) 	Finite looptime
130 	Guard (stand) 	 
131 	Guard (crouch) 	 
132 	Guard (air) 	 
140 	Stop guarding (stand) 	Finite looptime
141 	Stop guarding (crouch) 	Finite looptime
142 	Stop guarding (air) 	Finite looptime
150 	Guarding a hit (stand) 	Finite looptime
151 	Guarding a hit (crouch) 	Finite looptime
152 	Guarding a hit (air) 	No loop
170 opt 	Lose 	(See Note 1)
175 opt 	Time Over drawgame 	(See Note 1)
180 opt 	Win 	No loop (181-189 for multiple) (See Note 1)
190 opt 	Intro 	No loop (See Note 1)
195 opt 	Taunt 	Finite looptime (See Note 1)
5000 	Stand/Air Hit high (light) 	Looptime around 10-20
5001 	Stand/Air Hit high (medium) 	" (See Note 2)
5002 	Stand/Air Hit high (hard) 	"
5005 	Stand Recover high (light) 	No loop (See Note 3)
5006 	Stand Recover high (medium) 	"
5007 	Stand Recover high (hard) 	"
5010 	Stand/Air Hit low (light) 	Looptime around 10-20
5011 	Stand/Air Hit low (medium) 	"
5012 	Stand/Air Hit low (hard) 	"
5015 	Stand Recover low (light) 	No loop
5016 	Stand Recover low (medium) 	"
5017 	Stand Recover low (hard) 	"
5020 	Crouch Hit (light) 	Looptime around 10-20
5021 	Crouch Hit (medium) 	"
5022 	Crouch Hit (hard) 	"
5025 	Crouch Recover (light) 	No loop
5026 	Crouch Recover (medium) 	"
5027 	Crouch Recover (hard) 	"
5030 	Stand/Air Hit back 	Looptime around 10-20
5035 opt 	Stand/Air Hit transition 	Looptime around 5-15 (See Note 3)
5040 	Air Recover 	No loop
5050 	Air Fall 	No loop
5060 opt 	Air Fall (coming down) 	No loop
5070 	Tripped 	 
5080 	LieDown Hit (stay down) 	(See Note 4)
5090 	LieDown Hit (hit up into air) 	 
5100 	Hitting ground from fall 	Looptime around 3
5160 	Bounce into air 	 
5170 	Hit ground from bounce 	Looptime around 3 or 4
5110 	LieDown 	 
5120 	Get up from LieDown 	 
5140 opt 	LieDead (first rounds) 	 
5150 opt 	LieDead (final round) 	 
5200 	Fall-recovery near ground 	 
5210 	Fall-recovery in mid-air 	 
5300 	Dizzy 	 
5500 opt 	"Continue?" screen 	If omitted, plays dizzy anim
5510 opt 	"Yes" to "Continue" 	If omitted, plays first win anim (not yet implemented)
5520 opt 	"No" to "Continue" 	(not yet implemented)

Optional Hit Up animations (see Note 5):
Number 	Description 	Comments
5051 opt 	Air fall -- hit up 	 
5061 opt 	Coming down from hit up 	 
5081 opt 	LieDown Hit (stay down) 	 
5101 opt 	Bounce from ground into air 	Looptime around 3
5161 opt 	Bounce into air 	 
5171 opt 	Hit ground from bounce 	Looptime around 3 or 4
5111 opt 	LieDown 	 
5121 opt 	Get up from LieDown 	 
5151 opt 	LieDead (first rounds) 	 
5156 opt 	LieDead (final round) 	 

Optional Hit Up-Diagonal animations (see Note 6):
Number 	Description 	Comments
5052 opt 	Air fall -- hit up 	 
5062 opt 	Coming down from hit up 	 
5082 opt 	LieDown Hit (stay down) 	 
5102 opt 	Bounce from ground into air 	Looptime around 3
5162 opt 	Bounce into air 	 
5172 opt 	Hit ground from bounce 	Looptime around 3 or 4
5112 opt 	LieDown 	 
5122 opt 	Get up from LieDown 	 
5152 opt 	LieDead (first rounds) 	 
5157 opt 	LieDead (final round) 	 

Note 1
    These do not actually have to use only the stated numbers. If is
    more of a recommended number than a M.U.G.E.N one. If necessary, feel free to use any other action numbers.

Note 2: If medium and heavy hits are omitted, they default to the light hits

Note 3
    "No loop" means last frame has a time of -1.
    For recover animation, the first frame of each recovery should be the last frame of the corresponding hit. Eg. If action 5000 has frame 5000,0 and 5000,1, then action 5005 should start with frame 5000,1. This is because the animation will be locked in the first frame of the recovery after the hit animation is over, but before the player has recovered from the hit. If you have a Stand/Air Hit transition animation, then the first frame of Air Recover and Air Fall should be the last frame of the transition animation.
Note 4
    The Stand/Air Hit transition animation is played back after each
    hit animation in (or into) the air, but before the Air Recover and Air Fall animations.
Note 5
    You can loop the LieDown Hit if you want the player to look like he
    is "twitching" while being hit
Note 6
    This set of animations is optional. It is an alternate set of
    falling animations, which is used if hit by a HitDef with "Up" as the animtype.
Note 7
    This set of animations is optional. It is an alternate set of
    falling animations, which is used if hit by a HitDef with "DiagUp" as the animtype.
    
    Recommended Action Numbers

You do not have to follow this exactly, but it should serve as a guideline.
In general, the states in the CNS should have the same numbers as the animation they use, to reduce confusion.

You might want space out your animation and state numbers. 
This gives room for you to add in more actions as necessary
(some attacks can use multiple states and animations).
For instance, Standing Light Kick and Standing 
Strong Kick could have action numbers 200 and 210 respectively.
Number 	Description
0-199 	reserved
200-299 	Standing attacks
300-399 	More standing attacks, running attacks
400-499 	Crouch attacks
500-599 	More crouch attacks
600-699 	Air attacks
700-799 	More air attacks
800-999 	Unused - use if you need more states
1000-2999 	All special attacks
3000-4999 	All hyper attacks
5000-5999 	reserved
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

# Upper bound for a single frame's on-screen duration. MUGEN's real "hold"
# frame is -1 (handled separately); any positive value above this comes from a
# malformed AIR row (mis-aligned columns) and would otherwise overflow Int32 in
# the emitted duration_ms. 10s is far longer than any legitimate MUGEN frame.
_MAX_FRAME_MS = 10_000

# Map FLARE direction index -> MUGEN direction key, with a fallback chain.
# All chains end in "E" because action 0 always has E populated.
_FLARE_DIR_TO_MUGEN: list[tuple[str, ...]] = [
    ("W",  "E"),          # 0 W
    ("SW", "W",  "E"),    # 1 SW
    ("S",  "E"),          # 2 S
    ("SE", "E"),          # 3 SE
    ("E",),               # 4 E
    ("NE", "E"),          # 5 NE
    ("N",  "E"),          # 6 N
    ("NW", "W",  "E"),    # 7 NW
]

# MUGEN action numbers -> canonical Riftstorm FLARE animation aliases.
# Numbers come from the standard CNS convention used by virtually every
# MUGEN character. Anything not listed here is still emitted under
# "action_<number>" so no data is lost.
_ACTION_ALIASES: dict[int, str] = {
    # Idle / movement
    0:    "stance",
    #20:   "walk",
    #21:   "walk_back",
    #100:  "run",
    #105:  "run_back",
    # Crouch
    #10:   "crouch_down",
    #11:   "crouch",
    #12:   "crouch_up",
    # Jump
    #40:   "jump_start",
    #41:   "jump",
    #50:   "jump_land",
    # Guard / block
    #120:  "guard_start",
    #130:  "guard",
    #131:  "crouch_guard",
    #132:  "air_guard",
    #140:  "guard_end",
    #150:  "guard_hit_stand",
    #151:  "guard_hit_crouch",
    #152:  "guard_hit_air",
    # Stand attacks (light/medium/heavy punches + kicks).
    # "swing" = primary stand light attack so it is plug-compatible with the
    # PlayerCombatVisuals m_AnimSwing default.
    #200:  "swing",
    #210:  "swing_medium",
    #220:  "swing_heavy",
    #230:  "crouch_swing",
    #240:  "crouch_swing_medium",
    #250:  "crouch_swing_heavy",
    #400:  "kick",
    #410:  "kick_medium",
    #420:  "kick_heavy",
    #430:  "crouch_kick",
    #440:  "crouch_kick_medium",
    #450:  "crouch_kick_heavy",
    # Air attacks
    #600:  "air_swing",
    #610:  "air_swing_medium",
    #620:  "air_swing_heavy",
    #630:  "air_kick",
    #640:  "air_kick_medium",
    #650:  "air_kick_heavy",
    # Get-hit / fall / die (5xxx range)
    #5000: "hit",
    #5001: "hit",
    #5010: "hit_mid",
    #5011: "hit_mid",
    #5020: "hit_high",
    #5021: "hit_high",
    #5030: "hit_low",
    #5031: "hit_low",
    #5040: "hit_crouch",
    #5050: "fall",
    #5060: "fall",
    #5070: "trip",
    #5080: "fall",
    #5090: "fall",
    #5100: "land",
    #5110: "liedown",
    #5120: "getup",
    #5150: "die",
    #5160: "die",
    #5170: "die",
    #5200: "stand_recover",
    #5210: "crouch_recover",
    #5300: "getup",
    # Throws / hold (3xxx)
    #800:  "throw",
    #810:  "throw",
    #820:  "thrown",
    # KO / victory / continue
    #180:  "victory",
    #181:  "victory",
    #5900: "continue",
}

# MUGEN reserved action numbers -> human-readable move names (common1.cns
# convention). Only the numbers MUGEN actually reserves are named here; custom
# attack numbers get a category name via _action_label() instead.
_ACTION_NAMES: dict[int, str] = {
    0: "Standing",
    5: "Stand turning",
    6: "Crouch turning",
    10: "Stand to crouch",
    11: "Crouching",
    12: "Crouch to stand",
    20: "Walk forwards",
    21: "Walk backwards",
    40: "Jump start",
    41: "Jump up (neutral)",
    42: "Jump up (forward)",
    43: "Jump up (back)",
    44: "Jump down (neutral)",
    45: "Jump down (forward)",
    46: "Jump down (back)",
    47: "Jump landing",
    100: "Run forward",
    105: "Hop back",
    120: "Start guard (stand)",
    121: "Start guard (crouch)",
    122: "Start guard (air)",
    130: "Guard (stand)",
    131: "Guard (crouch)",
    132: "Guard (air)",
    140: "Stop guard (stand)",
    141: "Stop guard (crouch)",
    142: "Stop guard (air)",
    150: "Guard hit (stand)",
    151: "Guard hit (crouch)",
    152: "Guard hit (air)",
    170: "Lose",
    175: "Time over drawgame",
    180: "Win",
    190: "Intro",
    195: "Taunt",
    5000: "Hit high (light)",
    5001: "Hit high (medium)",
    5002: "Hit high (hard)",
    5005: "Recover high (light)",
    5006: "Recover high (medium)",
    5007: "Recover high (hard)",
    5010: "Hit low (light)",
    5011: "Hit low (medium)",
    5012: "Hit low (hard)",
    5015: "Recover low (light)",
    5016: "Recover low (medium)",
    5017: "Recover low (hard)",
    5020: "Crouch hit (light)",
    5021: "Crouch hit (medium)",
    5022: "Crouch hit (hard)",
    5025: "Crouch recover (light)",
    5026: "Crouch recover (medium)",
    5027: "Crouch recover (hard)",
    5030: "Hit back",
    5035: "Hit transition",
    5040: "Air recover",
    5050: "Air fall",
    5060: "Air fall (coming down)",
    5070: "Tripped",
    5080: "Liedown hit (stay down)",
    5090: "Liedown hit (into air)",
    5100: "Hit ground from fall",
    5110: "Liedown",
    5120: "Get up",
    5140: "Lie dead",
    5150: "Lie dead (final)",
    5160: "Bounce into air",
    5170: "Hit ground from bounce",
    5200: "Fall recovery (near ground)",
    5210: "Fall recovery (mid-air)",
    5300: "Dizzy",
    5500: "Continue screen",
    5510: "Continue: Yes",
    5520: "Continue: No",
}


def _action_label(number: int) -> str:
    """Human-readable move name for a MUGEN action number.

    Named reserved numbers win; otherwise the recommended attack ranges
    (200-599 ground, 600-799 air, 1000-2999 special, 3000-4999 hyper) yield a
    category name. Custom numbers outside any convention return "".
    """
    name = _ACTION_NAMES.get(number)
    if name:
        return name
    if 200 <= number <= 399:
        return "Standing attack"
    if 400 <= number <= 599:
        return "Crouch attack"
    if 600 <= number <= 799:
        return "Air attack"
    if 1000 <= number <= 2999:
        return "Special attack"
    if 3000 <= number <= 4999:
        return "Hyper attack"
    return ""


def _load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def _build_sprite_index(atlas_sprites: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """Index atlas sprites by their 'key' field ("<group>_<image>")."""
    sprites = atlas_sprites.get("sprites") or []
    index: dict[str, dict[str, Any]] = {}
    for entry in sprites:
        key = entry.get("key")
        if key:
            index[key] = entry
    return index


def _find_action(animations: dict[str, Any], action_number: int) -> dict[str, Any] | None:
    for action in animations.get("actions", []):
        if action.get("number") == action_number:
            return action
    return None


def _pick_direction_frames(action: dict[str, Any], flare_dir_idx: int) -> list[dict[str, Any]] | None:
    """Return the MUGEN frame list for the FLARE direction, falling back as needed."""
    directions = action.get("directions") or {}
    for mugen_key in _FLARE_DIR_TO_MUGEN[flare_dir_idx]:
        dir_data = directions.get(mugen_key)
        if dir_data and dir_data.get("frames"):
            return dir_data["frames"]
    return None


def _cell_from_frame(
    frame: dict[str, Any],
    sprite_index: dict[str, dict[str, Any]],
) -> dict[str, Any] | None:
    sprite_key = frame.get("sprite")
    if not sprite_key:
        return None
    sprite = sprite_index.get(sprite_key)
    if sprite is None:
        return None

    # FLARE expects top-left x/y plus an anchor (ox,oy) relative to the top-left.
    # MUGEN pivot in atlas.sprites.json is already the offset from top-left of
    # the sprite to the MUGEN axis (character foot/anchor). The per-frame
    # offsetX/offsetY shifts the displayed sprite relative to that axis.
    # FLARE's pivot is the anchor that snaps to the world position, so we move
    # the anchor in the OPPOSITE direction of offsetX/offsetY.
    base_ox = int(sprite.get("pivotX") or 0)
    base_oy = int(sprite.get("pivotY") or 0)
    off_x = int(frame.get("offsetX") or 0)
    off_y = int(frame.get("offsetY") or 0)

    cell: dict[str, Any] = {
        "x": int(sprite["x"]),
        "y": int(sprite["y"]),
        "w": int(sprite["w"]),
        "h": int(sprite["h"]),
        "ox": base_ox - off_x,
        "oy": base_oy - off_y,
    }

    # Atlas page this sprite lives on (0 = atlas.png). Only emit when non-zero
    # to keep single-page FLARE JSON identical to the pre-multi-page output.
    page = int(sprite.get("page") or 0)
    if page:
        cell["atlasPage"] = page

    # Per-frame collision boxes (already direction-mirrored upstream in
    # eight_dir.py). Only emit when non-empty to keep FLARE JSON lean —
    # animations without Clsn data fall back to the radius-based hit check.
    attack_boxes = frame.get("attackBoxes")
    if isinstance(attack_boxes, list) and attack_boxes:
        cell["attackBoxes"] = [[int(v) for v in box] for box in attack_boxes if isinstance(box, list) and len(box) == 4]
    hurt_boxes = frame.get("hurtBoxes")
    if isinstance(hurt_boxes, list) and hurt_boxes:
        cell["hurtBoxes"] = [[int(v) for v in box] for box in hurt_boxes if isinstance(box, list) and len(box) == 4]

    # Per-frame sprite flip (eight_dir.py mirrors W/NW/SW to flipH=True so the
    # side-view fighter faces the right way). Only emit when true to keep JSON lean.
    if frame.get("flipH"):
        cell["flipH"] = True
    if frame.get("flipV"):
        cell["flipV"] = True

    return cell


def _orphan_sprite_animations(
    animations: dict[str, Any],
    sprite_index: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    """Baut je ungenutzter Sprite-Gruppe eine Blaetter-Animation.

    Viele SFF enthalten Gruppen, die keine einzige AIR-Action anspricht
    (verworfene Posen, Ersatzeffekte, Portraits). Ohne diese Ersatz-Actions
    waeren die Sprites nirgends sichtbar — weder ueber eine Sequenz noch ueber
    die Kategorie "Nur Atlas", denn beide listen nur Animationen auf.
    """
    used: set[str] = set()
    for action in animations.get("actions", []):
        for direction in (action.get("directions") or {}).values():
            for frame in direction.get("frames") or []:
                key = frame.get("sprite")
                if key:
                    used.add(key)

    by_group: dict[int, list[tuple[int, dict[str, Any]]]] = {}
    for key, sprite in sprite_index.items():
        if key in used:
            continue
        by_group.setdefault(int(sprite.get("group") or 0), []).append(
            (int(sprite.get("image") or 0), sprite))

    frame_ms = int(round(1000.0 / 60.0)) * 8
    result: dict[str, Any] = {}
    for group, entries in sorted(by_group.items()):
        entries.sort(key=lambda e: e[0])
        frames: list[list[dict[str, Any]]] = []
        for _, sprite in entries:
            cell: dict[str, Any] = {
                "x": int(sprite["x"]),
                "y": int(sprite["y"]),
                "w": int(sprite["w"]),
                "h": int(sprite["h"]),
                "ox": int(sprite.get("pivotX") or 0),
                "oy": int(sprite.get("pivotY") or 0),
            }
            page = int(sprite.get("page") or 0)
            if page:
                cell["atlasPage"] = page
            frames.append([dict(cell) for _ in range(8)])
        result[f"sprites_{group}"] = {
            "frames_count": len(frames),
            "duration_ms": frame_ms * len(frames),
            "frame_durations_ms": [frame_ms] * len(frames),
            "frame_blends": [[0, 255, 0]] * len(frames),
            "loop_start": 0,
            "type": "looped",
            "label": f"Sprite-Gruppe {group}",
            "frames": frames,
        }
    return result


def _resolve_animation_type(reference_frames: list[dict[str, Any]]) -> str:
    """Decide FLARE animation type from MUGEN frame timings.

    MUGEN uses ``duration = -1`` on a final frame to mean "hold forever",
    which is exactly the FLARE ``play_once`` semantic. Otherwise we treat
    the animation as ``looped`` — MUGEN has no native back-and-forth flag.
    """
    if not reference_frames:
        return "looped"
    last = reference_frames[-1]
    last_dur = last.get("duration")
    if isinstance(last_dur, (int, float)) and last_dur <= 0:
        return "play_once"
    return "looped"


# Linksseitige FLARE-Slots, die im 2-dir-Modus horizontal gespiegelt werden.
# Restliche Slots (E, NE, SE, N, S) erhalten die E-Quelle unverändert.
_FLARE_LEFT_SLOTS: frozenset[int] = frozenset({0, 1, 7})  # W, SW, NW


def _build_animation(
    action: dict[str, Any],
    sprite_index: dict[str, dict[str, Any]],
    directions_mode: int = 2,
) -> dict[str, Any] | None:
    """Build one FLARE animation dict from one MUGEN action.

    Returns ``None`` if the action has no resolvable sprites at all.

    ``directions_mode`` steuert wie die 8 FLARE-Slots befüllt werden:
      * 2 -> nur E-Frames; linke Slots (W/SW/NW) werden via ``flipH`` gespiegelt.
        Kein Rotations-Artefakt, korrektes Side-View-Verhalten.
      * 8 -> per-direction MUGEN-Daten werden eingelesen (Legacy-Modus).
    """
    reference_frames = (
        (action.get("directions") or {}).get("E", {}).get("frames")
        or _pick_direction_frames(action, 4)
    )
    # Defensive: leere oder rein None-haltige Frame-Listen verwerfen.
    if not reference_frames or not any(reference_frames):
        return None

    frame_count = len(reference_frames)
    # Per-frame Dauer in Millisekunden. MUGEN -1 ("hold") oder 0 = ein Tick (~16ms),
    # damit der Animator nicht durch 0 teilt und der Frame trotzdem sichtbar wird.
    one_tick_ms = int(round(1000.0 / 60.0))
    frame_durations_ms: list[int] = []
    clamped_ticks = False
    for fr in reference_frames:
        dur = fr.get("duration")
        if isinstance(dur, (int, float)) and dur > 0:
            ms = int(round(float(dur) * 1000.0))
            if ms > _MAX_FRAME_MS:
                ms = _MAX_FRAME_MS
                clamped_ticks = True
            frame_durations_ms.append(max(1, ms))
        else:
            frame_durations_ms.append(one_tick_ms)
    if clamped_ticks:
        print(
            f"[warn] action {action.get('number')}: absurd frame duration in AIR "
            f"clamped to {_MAX_FRAME_MS} ms (malformed tick value).",
            file=sys.stderr,
        )
    total_ms = sum(frame_durations_ms)
    if total_ms <= 0:
        total_ms = max(one_tick_ms, frame_count * one_tick_ms)
    total_seconds = total_ms / 1000.0
    frame_blends = [_frame_blend(fr) for fr in reference_frames]
    # MUGENs LoopStart: eine geloopte Animation springt ans Ende der Einleitung
    # zurueck, nicht auf Frame 0.
    loop_start = int(
        ((action.get("directions") or {}).get("E") or {}).get("loopStart") or 0)
    loop_start = max(0, min(loop_start, frame_count - 1))

    frames: list[list[dict[str, Any]]] = []

    if directions_mode == 2:
        # 2D-Side-View: ausschließlich E-Quelle, linke Slots via flipH spiegeln.
        # Damit verschwindet das frühere Rotations-Artefakt, das beim Mischen
        # von 8-dir-Fallbacks aus MUGEN-Daten entstand.
        for f_idx in range(frame_count):
            src = reference_frames[f_idx]
            base_cell = _cell_from_frame(src, sprite_index)
            if base_cell is None:
                # Skip this animation entirely if even E is unresolvable.
                return None
            base_flip = bool(base_cell.get("flipH", False))
            row: list[dict[str, Any]] = []
            for d in range(8):
                # Shallow-copy reicht: attackBoxes/hurtBoxes-Listen werden nie
                # zur Laufzeit mutiert, FLARE liest sie nur. Spart Speicher
                # gegenüber einer Tiefenkopie pro Slot.
                cell = dict(base_cell)
                mirrored = base_flip if d not in _FLARE_LEFT_SLOTS else not base_flip
                if mirrored:
                    cell["flipH"] = True
                else:
                    cell.pop("flipH", None)
                row.append(cell)
            frames.append(row)
        return {
            "frames_count": frame_count,
            "duration_ms": int(round(total_seconds * 1000.0)),
            "frame_durations_ms": frame_durations_ms,
            "frame_blends": frame_blends,
            "loop_start": loop_start,
            "type": _resolve_animation_type(reference_frames),
            "frames": frames,
        }

    # Legacy 8-dir mode: per-direction MUGEN data with E fallback chain.
    per_direction: list[list[dict[str, Any]]] = []
    for d in range(8):
        df = _pick_direction_frames(action, d) or reference_frames
        per_direction.append(df)

    for f_idx in range(frame_count):
        row = []
        for d in range(8):
            dir_frames = per_direction[d]
            # If a fallback direction has a different length, clamp.
            src = dir_frames[f_idx] if f_idx < len(dir_frames) else dir_frames[-1]
            cell = _cell_from_frame(src, sprite_index)
            if cell is None:
                # Last-ditch fallback: use the E frame.
                cell = _cell_from_frame(
                    reference_frames[min(f_idx, len(reference_frames) - 1)],
                    sprite_index,
                )
            if cell is None:
                # Skip this animation entirely if even E is unresolvable.
                return None
            row.append(cell)
        frames.append(row)

    return {
        "frames_count": frame_count,
        "duration_ms": int(round(total_seconds * 1000.0)),
        "frame_durations_ms": frame_durations_ms,
        "frame_blends": frame_blends,
        "loop_start": loop_start,
        "type": _resolve_animation_type(reference_frames),
        "frames": frames,
    }


_AS_BLEND_RE = re.compile(r"^AS(\d+)D(\d+)$", re.IGNORECASE)


def _frame_blend(frame: dict[str, Any]) -> list[int]:
    """Uebersetzt das AIR-Blend-Flag eines Frames in ``[op, src, dst]``.

    ``op``: 0 = normal, 1 = additiv, 2 = subtraktiv. ``src``/``dst`` sind die
    MUGEN-Alphas 0..255. Ohne Flag bleibt es bei normalem Alpha-Blending.
    Ohne diese Angabe wuerden additive Effekte als schwarze Flaechen erscheinen,
    denn MUGEN legt Schwarz auf Deckung nur ueber die Blendfunktion frei.
    """
    flag = str(frame.get("blend") or "").strip().upper()
    if not flag:
        return [0, 255, 0]
    if flag == "A":
        return [1, 255, 255]
    if flag == "A1":
        return [1, 255, 128]
    if flag == "S":
        return [2, 255, 255]
    match = _AS_BLEND_RE.match(flag)
    if match:
        return [1, min(255, int(match.group(1))), min(255, int(match.group(2)))]
    return [0, 255, 0]


# --------------------------------------------------------------------------- #
# Skill / HitDef extraction
# --------------------------------------------------------------------------- #

# Pattern for trigger expressions like "AnimElem = 3", "animelem= 5", "anim Elem=2"
_ANIMELEM_RE = re.compile(r"animelem\s*=\s*(\d+)", re.IGNORECASE)


def _parse_int_tuple(value: Any, count: int = 2) -> list[int]:
    """Parse a MUGEN comma list ("17, 0") into ``count`` ints (zero-padded)."""
    if value is None:
        return [0] * count
    parts = [p.strip() for p in str(value).split(",")]
    out: list[int] = []
    for i in range(count):
        if i < len(parts) and parts[i]:
            try:
                out.append(int(float(parts[i])))
            except ValueError:
                out.append(0)
        else:
            out.append(0)
    return out


def _parse_float_tuple(value: Any, count: int = 2) -> list[float]:
    """Parse a MUGEN comma list ("-1.4, -3") into ``count`` floats (zero-padded)."""
    if value is None:
        return [0.0] * count
    parts = [p.strip() for p in str(value).split(",")]
    out: list[float] = []
    for i in range(count):
        if i < len(parts) and parts[i]:
            try:
                out.append(float(parts[i]))
            except ValueError:
                out.append(0.0)
        else:
            out.append(0.0)
    return out


def _resolve_hit_on_frame(controller: dict[str, Any]) -> int:
    """Best-effort: extract the MUGEN AnimElem index (1-based) from any trigger.

    Returns 0 if no AnimElem trigger is found — the runtime can then treat the
    HitDef as armed for the whole animation.
    """
    triggers = controller.get("triggers") or {}
    for _, expressions in triggers.items():
        if not isinstance(expressions, list):
            continue
        for expr in expressions:
            if not isinstance(expr, str):
                continue
            m = _ANIMELEM_RE.search(expr)
            if m:
                try:
                    return int(m.group(1))
                except ValueError:
                    continue
    return 0


def _build_skills(
    states: list[dict[str, Any]],
    action_aliases: dict[int, str],
) -> list[dict[str, Any]]:
    """Distill every state-bound HitDef into a Riftstorm skill entry.

    One entry per ``HitDef`` controller, even if a state contains several
    (some MUGEN moves chain multiple hits on different AnimElem frames). The
    entry is intentionally close to the source so the Unity side can decide
    later how to map it onto its own ability system.
    """
    skills: list[dict[str, Any]] = []
    for state in states or []:
        state_no = state.get("number")
        if not isinstance(state_no, int):
            continue
        header = state.get("header") or {}
        anim_raw = header.get("anim")
        anim_action_id = None
        if isinstance(anim_raw, int):
            anim_action_id = anim_raw
        elif isinstance(anim_raw, str) and anim_raw.strip().lstrip("-").isdigit():
            anim_action_id = int(anim_raw)
        anim_alias = action_aliases.get(anim_action_id) if anim_action_id is not None else None

        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            if (params.get("type") or "").strip().lower() != "hitdef":
                continue
            damage = _parse_int_tuple(params.get("damage"), 2)
            pause = _parse_int_tuple(params.get("pausetime"), 2)
            ground_vel = _parse_float_tuple(params.get("ground.velocity"), 2)
            air_vel = _parse_float_tuple(params.get("air.velocity"), 2)
            airguard_vel = _parse_float_tuple(params.get("airguard.velocity"), 2)
            skills.append({
                "stateNo": state_no,
                "animActionId": anim_action_id if anim_action_id is not None else -1,
                "animAlias": anim_alias or "",
                "hitOnFrame": _resolve_hit_on_frame(ctrl),
                "damage": damage[0],
                "guardDamage": damage[1],
                "attr": str(params.get("attr") or ""),
                "hitFlag": str(params.get("hitflag") or ""),
                "guardFlag": str(params.get("guardflag") or ""),
                "animType": str(params.get("animtype") or ""),
                "priority": str(params.get("priority") or ""),
                "pauseTimeHit": pause[0],
                "pauseTimeGuard": pause[1],
                "groundType": str(params.get("ground.type") or ""),
                "airType": str(params.get("air.type") or ""),
                "groundSlideTime": _seq_int(params.get("ground.slidetime"), 0),
                "groundHitTime": _seq_int(params.get("ground.hittime"), 0),
                "airHitTime": _seq_int(params.get("air.hittime"), 0),
                "groundVelocityX": ground_vel[0],
                "groundVelocityY": ground_vel[1],
                "airVelocityX": air_vel[0],
                "airVelocityY": air_vel[1],
                "airGuardVelocityX": airguard_vel[0],
                "airGuardVelocityY": airguard_vel[1],
                "sparkNo": str(params.get("sparkno") or ""),
                "hitSound": str(params.get("hitsound") or ""),
                "guardSound": str(params.get("guardsound") or ""),
                "p2StateNo": int(params.get("p2stateno") or 0) if str(params.get("p2stateno") or "").lstrip("-").isdigit() else 0,
                "fall": str(params.get("fall") or "").strip().lower() in ("1", "true"),
            })
    return skills


def _build_stats(
    constants: dict[str, Any],
    display_name: str = "",
    pixels_per_meter: float = 100.0,
) -> dict[str, Any]:
    """Distill MUGEN ``constants.json`` into a Riftstorm-friendly stats DTO.

    Pixel-space values are converted into meters using ``pixels_per_meter``
    (default 100). Per-tick velocities are converted to m/s assuming MUGEN's
    standard 60-tick simulation rate. Sprite scale shrinks/grows the body and
    its hit radius together; world velocities are not rescaled because MUGEN
    treats them as world-space.

    ``display_name`` is the human-readable label sourced from the MUGEN ``.def``
    (mirrored in ``char.json``). It is embedded in the sidecar so the Unity
    loader needs only a single file for runtime overrides.
    """
    data = constants.get("data") or {}
    size = constants.get("size") or {}
    velocity = constants.get("velocity") or {}

    def to_m(px: Any) -> float:
        return float(px or 0) / pixels_per_meter

    def to_mps(per_tick: Any) -> float:
        return float(per_tick or 0) * 60.0 / pixels_per_meter

    xscale = float(size.get("xscale", 1.0) or 1.0)
    yscale = float(size.get("yscale", 1.0) or 1.0)
    body_scale = max(xscale, yscale)

    ground_front = float(size.get("ground_front", 0) or 0)
    ground_back = float(size.get("ground_back", 0) or 0)
    hit_radius_m = to_m(max(ground_front, ground_back) * body_scale)

    run_fwd = velocity.get("run_fwd")
    run_fwd_x = run_fwd[0] if isinstance(run_fwd, list) and run_fwd else 0.0

    return {
        # Identity -> UnitStats.DisplayName (used for nametags, debug labels).
        "displayName": display_name or "",
        # Combat core -> UnitStats
        "maxHp":     int(data.get("life", 100)),
        "maxMana":   int(data.get("power", 0)),
        "strength":  int(data.get("attack", 0)),
        "armor":     int(data.get("defence", 0)),
        # Body / visual
        "hitRadius":   round(hit_radius_m, 3),
        "visualScale": {"x": round(xscale, 3), "y": round(yscale, 3)},
        "height":      round(to_m(float(size.get("height", 0) or 0) * yscale), 3),
        # Movement (m/s)
        "walkSpeed": round(to_mps(velocity.get("walk_fwd", 0)), 3),
        "runSpeed":  round(to_mps(run_fwd_x), 3),
        # NPC-intrinsic attack ranges (m). The Riftstorm weapon system can
        # still override these per-ability; they are sensible defaults derived
        # from the source character.
        "attackRange":     round(to_m(float(size.get("attack_dist", 0) or 0) * xscale), 3),
        "projectileRange": round(to_m(float(size.get("proj_attack_dist", 0) or 0) * xscale), 3),
        # Provenance, useful for debugging at runtime.
        "pixelsPerMeter": pixels_per_meter,
    }


def _seq_anim_number(raw: Any) -> int | None:
    """First integer token of a MUGEN param value (e.g. "200,1" -> 200)."""
    if raw is None:
        return None
    token = str(raw).split(",")[0].strip()
    return int(token) if token.lstrip("-").isdigit() else None


def _split_args(inner: str) -> list[str]:
    """Split a MUGEN call's arguments on top-level commas (respects nesting)."""
    args: list[str] = []
    depth = 0
    current: list[str] = []
    for ch in inner:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            args.append("".join(current).strip())
            current = []
            continue
        current.append(ch)
    args.append("".join(current).strip())
    return args


# Bedingungen, die fuer den ruhenden Showcase-Charakter entscheidbar sind:
# er steht still, also ist "vel x = 0" wahr und jeder Vergleich mit != 0 falsch.
_REST_TRUE_RE = re.compile(r"^\(?\s*vel\s+[xy]\s*\)?\s*(=|<=|>=)\s*0\s*$", re.IGNORECASE)
_REST_FALSE_RE = re.compile(r"^\(?\s*vel\s+[xy]\s*\)?\s*(<|>|!=)\s*-?\d", re.IGNORECASE)


def _rest_condition(cond: str) -> bool | None:
    """Wahrheitswert einer Bedingung im Ruhezustand (None = unentscheidbar)."""
    text = cond.strip()
    if _REST_TRUE_RE.match(text):
        return True
    if _REST_FALSE_RE.match(text):
        return False
    return None


def _seq_resolve_anim(raw: Any) -> int | None:
    """Anim number from a param, resolving common MUGEN expressions.

    ``IfElse(cond, a, b)`` picks mirrored effect sets per team, e.g.
    ``IfElse(Root,TeamSide = 2, 7840, 7830)``. The showcase always renders P1
    (TeamSide 1), so the else-branch is the correct static answer — ausser die
    Bedingung haengt an der Geschwindigkeit: Statedef 50 waehlt so zwischen
    neutralem, vorwaertsem und rueckwaertsem Sprung.
    ``8000 + (Random % 4)`` picks a random variant; the base number is used.
    """
    direct = _seq_anim_number(raw)
    if direct is not None:
        return direct
    if raw is None:
        return None
    text = str(raw).strip()
    m = re.search(r"ifelse\s*\((.*)\)\s*$", text, re.IGNORECASE | re.DOTALL)
    if m is not None:
        args = _split_args(m.group(1))
        if len(args) >= 3:
            decided = _rest_condition(args[0])
            if decided is not None:
                return _seq_resolve_anim(args[1] if decided else args[2])
        if len(args) >= 2:
            for arg in (args[-1], args[-2]):
                num = _seq_resolve_anim(arg)
                if num is not None:
                    return num
        return None
    # "8000 + (Random % 4)" -> base variant 8000.
    m = re.match(r"(-?\d+)\s*[+\-]", text)
    return int(m.group(1)) if m else None


def _seq_pos(raw: Any) -> list[int]:
    """MUGEN "x, y" position -> [x, y] ints (non-numeric/expressions -> 0)."""
    x = 0
    y = 0
    if raw is not None:
        parts = str(raw).split(",")
        if len(parts) >= 1:
            m = re.match(r"\s*(-?\d+)", parts[0])
            if m:
                x = int(m.group(1))
        if len(parts) >= 2:
            m = re.match(r"\s*(-?\d+)", parts[1])
            if m:
                y = int(m.group(1))
    return [x, y]


_MUGEN_TICKS_PER_SECOND = 60.0

# Die Trigger-Formen, mit denen MUGEN Effekte im Ablauf eines Moves platziert.
# Ohne sie landet jeder Effekt auf Tick 0 und der ganze Move feuert auf einmal.
_TIME_TRIGGER_RE = re.compile(r"^time\s*(?:=|>=)\s*(-?\d+)$")
_TIME_MOD_RE = re.compile(r"^time\s*%\s*(\d+)\s*=\s*(\d+)$")
_ANIMELEM_RE = re.compile(r"^animelem\s*(?:=|>=)\s*(\d+)\s*(?:,\s*(?:=|>=)\s*(-?\d+))?$")
_ANIMELEMNO_RE = re.compile(r"^animelemno\s*\(\s*0\s*\)\s*(?:=|>=)\s*(\d+)$")
_ANIMELEMTIME_RE = re.compile(r"^animelemtime\s*\(\s*(\d+)\s*\)\s*(?:=|>=)\s*(-?\d+)$")


def _anim_elem_ticks(animations: dict[str, Any]) -> dict[int, list[int]]:
    """Pro Action die Tick-Dauer jedes AIR-Elements (fuer `animelem`-Trigger)."""
    out: dict[int, list[int]] = {}
    for action in animations.get("actions") or ():
        num = action.get("number")
        if not isinstance(num, int) or num in out:
            continue
        frames = (action.get("directions") or {}).get("E", {}).get("frames")
        if not frames:
            continue
        ticks: list[int] = []
        for fr in frames:
            dur = fr.get("duration")
            if isinstance(dur, (int, float)) and dur > 0:
                ticks.append(max(1, int(round(float(dur) * _MUGEN_TICKS_PER_SECOND))))
            else:
                ticks.append(1)
        out[num] = ticks
    return out


def _elem_start_tick(
    elem_ticks: dict[int, list[int]] | None, anim_no: int | None, elem: int
) -> int | None:
    """Tick, an dem AIR-Element `elem` (1-basiert) von `anim_no` beginnt."""
    ticks = (elem_ticks or {}).get(anim_no) if anim_no is not None else None
    if not ticks or elem < 1 or elem > len(ticks):
        return None
    return sum(ticks[: elem - 1])


def _cond_timing(
    text: str, elem_ticks: dict[int, list[int]] | None, anim_no: int | None
) -> tuple[int | None, int]:
    """(Start-Tick, Periode) einer einzelnen Trigger-Bedingung.

    Periode > 0 nur bei `Time % N = K` — so baut MUGEN Dauerstrahlen und
    Partikelstroeme, indem derselbe Explod alle N Ticks neu gespawnt wird.
    """
    m = _TIME_MOD_RE.match(text)
    if m:
        return int(m.group(2)), int(m.group(1))
    m = _TIME_TRIGGER_RE.match(text)
    if m:
        return max(0, int(m.group(1))), 0
    m = _ANIMELEM_RE.match(text)
    if m:
        start = _elem_start_tick(elem_ticks, anim_no, int(m.group(1)))
        return (None, 0) if start is None else (start + max(0, int(m.group(2) or 0)), 0)
    m = _ANIMELEMNO_RE.match(text)
    if m:
        return _elem_start_tick(elem_ticks, anim_no, int(m.group(1))), 0
    m = _ANIMELEMTIME_RE.match(text)
    if m:
        start = _elem_start_tick(elem_ticks, anim_no, int(m.group(1)))
        return (None, 0) if start is None else (start + max(0, int(m.group(2))), 0)
    return None, 0


def _seq_trigger_timing(
    triggers: Any,
    elem_ticks: dict[int, list[int]] | None = None,
    anim_no: int | None = None,
) -> tuple[int, int]:
    """(Start-Tick, Periode) eines Controllers aus seinen Triggern.

    Innerhalb einer Trigger-Gruppe gilt UND (also der spaeteste Tick der Gruppe),
    zwischen den Gruppen ODER (also die frueheste Gruppe feuert).
    """
    if not isinstance(triggers, dict):
        return 0, 0
    best: tuple[int, int] | None = None
    for conds in triggers.values():
        tick: int | None = None
        period = 0
        for cond in conds or ():
            value, cond_period = _cond_timing(
                str(cond).strip().lower(), elem_ticks, anim_no)
            if value is None:
                continue
            tick = value if tick is None else max(tick, value)
            period = max(period, cond_period)
        if tick is not None and (best is None or tick < best[0]):
            best = (tick, period)
    return best or (0, 0)


def _seq_trigger_time(
    triggers: Any,
    elem_ticks: dict[int, list[int]] | None = None,
    anim_no: int | None = None,
) -> int:
    """Nur der Start-Tick — fuer die Body-Kette, die keine Periode kennt."""
    return _seq_trigger_timing(triggers, elem_ticks, anim_no)[0]


def _seq_scale(raw: Any) -> list[float]:
    """MUGEN "sx, sy" scale -> [sx, sy] floats (expressions/blank -> 1.0).

    ``const(size.xscale)`` kommt in Explod-Skalierungen haeufig vor und wird auf
    den Wert aus ``[Size]`` aufgeloest — fuer den Charakter ist das eine Konstante.
    """
    values = [1.0, 1.0]
    if raw is None:
        return values
    parts = str(raw).split(",")
    for i in range(min(2, len(parts))):
        token = parts[i].strip()
        low = token.lower()
        if "size.xscale" in low:
            values[i] = _CHAR_SIZE_SCALE[0]
            continue
        if "size.yscale" in low:
            values[i] = _CHAR_SIZE_SCALE[1]
            continue
        m = re.match(r"\s*(-?\d+(?:\.\d+)?)", token)
        if m:
            values[i] = float(m.group(1))
    return values


def _seq_int(raw: Any, default: int) -> int:
    """First int token of a value, else the default (for facing/removetime/...)."""
    n = _seq_anim_number(raw)
    return n if n is not None else default


def _seq_float(raw: Any, default: float) -> float:
    """Erste Fliesskommazahl eines Werts, sonst der Default."""
    if raw is None:
        return default
    match = re.match(r"\s*(-?\d+(?:\.\d+)?)", str(raw))
    return float(match.group(1)) if match else default


# MUGEN trans type -> default (source, destination) alpha, mirroring Ikemen-GO's
# compiler (src/compiler.go). The destination alpha is what actually decides how
# additive a sprite looks: 0 = plain alpha blend, 255 = fully additive.
_TRANS_DEFAULT_ALPHA = {
    "none": (255, 0),
    "default": (255, 0),
    "add": (255, 255),
    "add1": (255, 128),
    "addalpha": (255, 0),
    "sub": (255, 255),
    "subadd": (255, 255),
}


def _seq_alpha(trans: str, raw: Any) -> list[int]:
    """Resolve a MUGEN ``alpha = S, D`` pair against its trans type's defaults.

    ``addalpha`` normally reads its blend from this parameter. Characters often
    drive it from a variable (``256 - Var(0), Var(0)``, a glow fade) which cannot
    be evaluated statically — those fall back to fully additive, because the
    literal default (dst 0) would render the effect as if it had no blending.
    """
    src, dst = _TRANS_DEFAULT_ALPHA.get(trans, (255, 0))
    parts = str(raw).split(",") if raw is not None else []
    parsed = [_seq_anim_number(p) for p in parts[:2]]
    if len(parsed) == 2 and all(v is not None for v in parsed):
        src, dst = parsed[0], parsed[1]
    elif trans == "addalpha":
        src, dst = 255, 255
    return [max(0, min(255, src)), max(0, min(255, dst))]


def _seq_layer(params: dict[str, Any]) -> int:
    """Resolve the MUGEN ``ontop`` / ``under`` explod flags into one layer hint.

    Both override ``sprpriority`` completely: ``ontop`` draws the effect above
    everything (super flashes, screen tints), ``under`` below everything (ground
    shadows, floor decals). ``0`` means "sort normally by sprpriority".
    """
    if _seq_int(params.get("ontop"), 0) != 0:
        return 1
    if _seq_int(params.get("under"), 0) != 0:
        return -1
    return 0


def _seq_bank_number(raw: Any) -> int | None:
    """Erste Zahl eines Werts, auch hinter dem Bank-Praefix ``S``/``F``.

    MUGEN adressiert die geteilte fightfx-Bank mit ``S8000``/``F1``. Die
    Autoren dieser Charaktere mergen den Bank-Inhalt in ihre eigene SFF/SND,
    also wird das Praefix abgestreift statt den Wert zu verwerfen.
    """
    if raw is None:
        return None
    text = str(raw).strip()
    if text[:1].upper() in ("S", "F"):
        text = text[1:]
    # `sparkno = S8000 + (Random % 4)` waehlt zufaellig aus 8000..8003 — die
    # Basiszahl ist ein gueltiger Vertreter, der Rest ist statisch nicht loesbar.
    match = re.match(r"\s*(-?\d+)", text)
    return int(match.group(1)) if match else None


def _seq_sound(raw: Any) -> tuple[int, int] | None:
    """Resolve a MUGEN ``value = Group, Index`` sound reference."""
    if raw is None:
        return None
    parts = [p.strip() for p in str(raw).split(",")]
    if len(parts) < 2:
        return None
    group = _seq_bank_number(parts[0])
    index = _seq_bank_number(parts[1])
    if group is None or index is None or group < 0 or index < 0:
        return None
    return group, index


def _seq_hitdef(params: dict[str, Any], time: int, key_for: Any) -> dict[str, Any] | None:
    """Uebersetzt einen ``HitDef`` in einen Treffer-Eintrag.

    Neben Funke und Trefferton traegt der Eintrag, was das Opfer tut: Ikemen
    waehlt dessen Zustand nach dessen StateType (char.go ~12470, stehend -> 5000)
    und common1.cns waehlt darin die Animation aus ``animtype``/``groundtype``.
    Mit ``ground.velocity`` fliegt die Puppe dann tatsaechlich zurueck.
    """
    spark = _seq_bank_number(params.get("sparkno"))
    sound = _seq_sound(params.get("hitsound"))
    spark_key = key_for(spark) if spark is not None and spark >= 0 else None
    if spark_key is None and sound is None:
        return None
    pos = _seq_pos(params.get("sparkxy"))
    ground_vel = _parse_float_tuple(params.get("ground.velocity"), 2)
    air_vel = _parse_float_tuple(params.get("air.velocity"), 2)
    return {
        "time": time,
        "spark": spark_key,
        "sparkpos": pos,
        "sound": {"group": sound[0], "index": sound[1]} if sound else None,
        # `pausetime = self, target` friert beide Seiten beim Treffer kurz ein.
        "pausetime": _seq_int(params.get("pausetime"), 0),
        "animtype": _HIT_ANIMTYPES.get(
            str(params.get("animtype") or "").strip().lower(), 0),
        "groundtype": _HIT_GROUNDTYPES.get(
            str(params.get("ground.type") or "").strip().lower(), 1),
        "groundvel": list(ground_vel),
        "airvel": list(air_vel),
        "fall": 1 if _seq_int(params.get("fall"), 0) else 0,
        "hittime": _seq_int(params.get("ground.hittime"), 0),
    }


# `animtype`/`ground.type` steuern in common1.cns die Trefferanimation des Opfers.
_HIT_ANIMTYPES = {"light": 0, "medium": 1, "hard": 2, "heavy": 2,
                  "back": 3, "up": 4, "diagup": 5}
_HIT_GROUNDTYPES = {"high": 1, "low": 2, "trip": 3, "none": 1}


# Helper spawnen Helper; tiefer als drei Ebenen wird es reine Upkeep-Logik.


def _seq_volume(params: dict[str, Any]) -> float:
    """MUGEN-Lautstaerke als linearer Faktor 0..1.

    ``volumescale`` ist die neue Skala (100 = normal), ``volume`` die alte
    (0 = normal, -255 = stumm).
    """
    scale = params.get("volumescale")
    if scale is not None:
        value = _seq_anim_number(scale)
        return 1.0 if value is None else max(0.0, min(1.0, value / 100.0))
    raw = _seq_int(params.get("volume"), 0)
    return 1.0 if raw == 0 else max(0.0, min(1.0, (raw + 255) / 255.0))


_STATE_BANDS: tuple[tuple[int, int, str], ...] = (
    (0, 199, "System"),
    (200, 399, "Normal"),
    (400, 999, "Command"),
    (1000, 2999, "Special"),
    (3000, 3999, "Hyper"),
    (5000, 5999, "Hit"),
)


def _state_category(
    state_no: int,
    body_keys: list[dict[str, Any]],
    helper_states: set[int],
) -> str:
    """Grobe Einordnung eines Statedefs fuer die Showcase-Gliederung.

    MUGEN-Autoren halten sich fuer Standard-States an feste Nummernbaender.
    Alles ausserhalb ist charakter-eigen und wird als ``Custom`` behalten statt
    verworfen — genau dort leben die selbstgebauten Moves. Ein Statedef ganz
    ohne Body-Animation ist kein Move, sondern eine reine Effekt-Buehne;
    ``Helper`` sind die Fragmente, die nur als Teil eines Moves laufen.
    Negative Statedefs laufen in jedem Frame und tragen die Dauereffekte.
    """
    if state_no < 0:
        return "Global"
    if state_no in helper_states:
        return "Helper"
    if not body_keys:
        return "Effect"
    for low, high, name in _STATE_BANDS:
        if low <= state_no <= high:
            return name
    return "Custom"


def _seq_int_list(raw: Any, count: int, default: int | list[int]) -> list[int]:
    """Liest ``a, b, c``-Ganzzahlen; Komponenten mit Ausdruecken behalten den Default.

    MUGEN-Autoren rechnen in PalFX-Parametern haeufig mit Variablen
    (``256 + Var(24)``). Statisch ist das nicht aufloesbar, also bleibt die
    Komponente neutral, statt einen erfundenen Wert einzusetzen. ``default``
    darf eine Liste sein, wenn die Komponenten unterschiedliche Ruhewerte haben.
    """
    values = list(default) if isinstance(default, list) else [default] * count
    if raw is None:
        return values
    parts = str(raw).split(",")
    for i in range(min(count, len(parts))):
        match = re.fullmatch(r"\s*(-?\d+)\s*", parts[i])
        if match:
            values[i] = int(match.group(1))
    return values


def _seq_palfx(params: dict[str, Any], time: int, scope: str) -> dict[str, Any] | None:
    """Uebersetzt einen ``PalFX``/``AllPalFX``-Controller in einen Farbfilter.

    Skala wie in Ikemens ``PalFXDef``: ``add`` in 0..255, ``mul`` mit 256 = 1.0,
    ``color`` mit 256 = volle Saettigung. Ein Filter ohne jede Wirkung wird
    verworfen, damit die Laufzeit nicht sinnlos Material-Bloecke setzt.
    """
    add = _seq_int_list(params.get("add"), 3, 0)
    mul = _seq_int_list(params.get("mul"), 3, 256)
    sinadd = _seq_int_list(params.get("sinadd"), 4, 0)
    color = _seq_int_list(params.get("color"), 1, 256)[0]
    invert = 1 if _seq_int(params.get("invertall"), 0) else 0
    if add == [0, 0, 0] and mul == [256, 256, 256] and sinadd[:3] == [0, 0, 0] \
            and color == 256 and invert == 0:
        return None
    return {
        "time": time,
        # MUGEN `time`: Dauer in Ticks, -1 = dauerhaft.
        "duration": _seq_int(params.get("time"), 1),
        "scope": scope,
        "add": add,
        "mul": mul,
        "color": color,
        "invertall": invert,
        "sinadd": sinadd[:3],
        "sinperiod": sinadd[3],
    }


def _char_movement(char_dir: Path) -> dict[str, float]:
    """Physik-Konstanten aus ``[Movement]`` (Ikemen-Defaults als Rueckfall).

    Ikemen bremst einen Charakter je nach ``physics`` des Statedefs jeden Tick:
    ``vel.x *= stand.friction`` am Boden, ``crouch.friction`` in der Hocke
    (char.go, posUpdate). Ohne diese Werte laeuft eine ``VelSet``-Bewegung ewig.
    """
    defaults = {"yaccel": 0.5, "stand_friction": 0.85, "crouch_friction": 0.82}
    path = char_dir / "constants.json"
    if not path.is_file():
        return defaults
    try:
        movement = (_load_json(path) or {}).get("movement") or {}
    except Exception:  # noqa: BLE001
        return defaults
    return {key: _seq_float(movement.get(key), fallback)
            for key, fallback in defaults.items()}


# ``const(size.xscale)`` in Explod-Ausdruecken. Pro Charakter einmal gesetzt,
# weil MUGEN es dort ebenfalls als Konstante behandelt.
_CHAR_SIZE_SCALE: tuple[float, float] = (1.0, 1.0)


def _set_char_size_scale(char_dir: Path) -> None:
    """Merkt sich ``[Size] xscale/yscale`` des gerade konvertierten Charakters."""
    global _CHAR_SIZE_SCALE
    path = char_dir / "constants.json"
    size: dict[str, Any] = {}
    if path.is_file():
        try:
            size = (_load_json(path) or {}).get("size") or {}
        except Exception:  # noqa: BLE001
            size = {}
    _CHAR_SIZE_SCALE = (
        _seq_float(size.get("xscale"), 1.0),
        _seq_float(size.get("yscale"), 1.0),
    )


# Controller, die den Charakter selbst bewegen. MUGEN rechnet in Pixeln pro
# Tick, x ist facing-relativ, y zaehlt nach unten (0 = Boden).
_MOVE_CONTROLLERS = ("velset", "veladd", "velmul", "posset", "posadd")


def _seq_move(params: dict[str, Any], time: int, ctype: str) -> dict[str, Any] | None:
    """Uebersetzt einen Bewegungs-Controller in einen Eintrag der Move-Spur."""
    default = 1.0 if ctype == "velmul" else 0.0
    x_raw = params.get("x")
    y_raw = params.get("y")
    if x_raw is None and y_raw is None:
        return None
    return {
        "time": time,
        "type": ctype,
        "x": _seq_float(x_raw, default),
        "y": _seq_float(y_raw, default),
    }


# Helper spawnen Helper; tiefer als drei Ebenen wird es reine Upkeep-Logik.
def _seq_float_list(raw: Any, count: int, default: float | list[float]) -> list[float]:
    """Liest ``a, b, c``-Fliesskommazahlen; Komponenten mit Ausdruecken bleiben neutral."""
    values = list(default) if isinstance(default, list) else [default] * count
    if raw is None:
        return values
    parts = str(raw).split(",")
    for i in range(min(count, len(parts))):
        match = re.fullmatch(r"\s*(-?\d+(?:\.\d+)?)\s*", parts[i])
        if match:
            values[i] = float(match.group(1))
    return values


def _seq_afterimage(params: dict[str, Any], time: int, retime: bool) -> dict[str, Any]:
    """Uebersetzt ``AfterImage``/``AfterImageTime`` in einen Nachzieh-Eintrag.

    Die Defaults spiegeln Ikemens ``newAfterImage`` (src/char.go): das erste
    Nachbild startet auf ``palbright``/``palcontrast``, jedes weitere addiert
    ``paladd`` und multipliziert mit ``palmul``. Ohne diese Basiswerte waeren
    die Nachbilder farblich identische Klone des Koerpers.
    """
    if retime:
        return {"time": time, "duration": _seq_int(params.get("time"), 1), "retime": 1}
    trans = str(params.get("trans") or "none").lower()
    return {
        "time": time,
        "duration": _seq_int(params.get("time"), 1),
        "retime": 0,
        "length": max(1, _seq_int(params.get("length"), 20)),
        "timegap": max(1, _seq_int(params.get("timegap"), 1)),
        "framegap": max(1, _seq_int(params.get("framegap"), 4)),
        # Startwerte des ersten Nachbilds.
        "palbright": _seq_int_list(params.get("palbright"), 3, 30),
        "palcontrast": _seq_int_list(params.get("palcontrast"), 3, [120, 120, 220]),
        "palpostbright": _seq_int_list(params.get("palpostbright"), 3, 0),
        "palcolor": _seq_int(params.get("palcolor"), 256),
        "palinvertall": 1 if _seq_int(params.get("palinvertall"), 0) else 0,
        # Zuwachs je weiterem Nachbild.
        "paladd": _seq_int_list(params.get("paladd"), 3, [10, 10, 25]),
        "palmul": _seq_float_list(params.get("palmul"), 3, [0.65, 0.65, 0.75]),
        "trans": trans,
        "alpha": _seq_alpha(trans, params.get("alpha")),
    }


def _seq_modify_explod(params: dict[str, Any], time: int) -> dict[str, Any] | None:
    """Uebersetzt ein ``ModifyExplod`` in eine Aenderung an einem Explod per ID.

    Nur statisch aufloesbare Werte werden uebernommen; MUGEN-Autoren rechnen hier
    haeufig mit ``Time``/``Var()``, was ohne Interpreter nicht auswertbar ist.
    """
    explod_id = _seq_int(params.get("id"), -1)
    if explod_id < 0:
        return None
    entry: dict[str, Any] = {"time": time, "id": explod_id}
    if params.get("scale") is not None:
        entry["scale"] = _seq_scale(params.get("scale"))
    if params.get("sprpriority") is not None:
        entry["sprpriority"] = _seq_anim_number(params.get("sprpriority")) or 0
    if params.get("removetime") is not None:
        entry["removetime"] = _seq_int(params.get("removetime"), -2)
    if params.get("angle") is not None:
        entry["angle"] = _seq_float(params.get("angle"), 0.0)
    if params.get("velocity") is not None or params.get("vel") is not None:
        entry["vel"] = _seq_vec2(params.get("velocity") or params.get("vel"), (0.0, 0.0))
    if params.get("accel") is not None:
        entry["accel"] = _seq_vec2(params.get("accel"), (0.0, 0.0))
    return entry if len(entry) > 2 else None


_HELPER_MAX_DEPTH = 3


def _state_entry_anim(state: dict[str, Any]) -> int | None:
    """Animation, mit der ein Statedef sichtbar startet.

    Meist steht sie im Header. Die Sprung- und Trefferzustaende lassen ihn aber
    leer und setzen die Animation im ersten Tick per ``ChangeAnim`` — Statedef 50
    waehlt so zwischen neutralem, vorwaertsem und rueckwaertsem Sprung.
    """
    header_anim = _seq_anim_number((state.get("header") or {}).get("anim"))
    if header_anim is not None:
        return header_anim
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        if (params.get("type") or "").lower() != "changeanim":
            continue
        lines = _trigger_lines(ctrl.get("triggers"))
        if lines and not all(
            _ALWAYS_TRIGGER_RE.match(line) or _ENTRY_TRIGGER_RE.match(line)
            for line in lines
        ):
            continue
        resolved = _seq_resolve_anim(params.get("value"))
        if resolved is not None:
            return resolved
    return None


def _pick_followup(
    state: dict[str, Any],
    by_number: dict[int, dict[str, Any]],
    key_for: Any,
    seen: set[int],
) -> tuple[dict[str, Any] | None, Any]:
    """Waehlt den ``ChangeState``, der den Move sichtbar fortsetzt.

    Ein Helper-State hat meist mehrere Ausgaenge: den eigentlichen Folgeschritt
    und daneben Aufraeum-Ziele wie 10000 oder DestroySelf. Der sichtbare ist der,
    dessen Ziel-Statedef eine Animation im Atlas hat.
    """
    fallback: tuple[dict[str, Any] | None, Any] = (None, None)
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        if (params.get("type") or "").lower() not in ("changestate", "selfstate"):
            continue
        number = _seq_anim_number(params.get("value"))
        target = by_number.get(number) if number is not None else None
        if target is None or number in seen:
            continue
        if key_for(_state_entry_anim(target)) is not None:
            return target, ctrl
        if fallback[0] is None:
            fallback = (target, ctrl)
    return fallback


# Trigger, die ohne Spielzustand auswertbar sind: dauerhaft wahr bzw. beim Eintritt.
_ALWAYS_TRIGGER_RE = re.compile(r"^\s*1\s*$")
_ENTRY_TRIGGER_RE = re.compile(r"^\s*time\s*(=|<=)\s*0\s*$", re.IGNORECASE)


def _trigger_lines(triggers: dict[str, list[str]] | None) -> list[str]:
    """Alle Bedingungszeilen eines Controllers als flache Liste."""
    lines: list[str] = []
    for values in (triggers or {}).values():
        lines.extend(str(value) for value in values)
    return lines


def _axis_pair(params: dict[str, Any], default: list[float]) -> list[float]:
    """Liest die getrennten ``x``/``y``-Parameter; nicht lesbare Achsen bleiben."""
    values = list(default)
    for index, name in enumerate(("x", "y")):
        raw = params.get(name)
        if raw is None:
            continue
        try:
            values[index] = float(str(raw).split(",")[0])
        except ValueError:
            pass
    return values


def _actor_motion(state: dict[str, Any]) -> dict[str, Any]:
    """Startgeschwindigkeit und Dauerbeschleunigung eines Helfer-States.

    MUGEN bewegt einen Helfer wie einen Charakter: ``velset`` setzt beim
    Eintritt die Geschwindigkeit, ein in jedem Tick feuerndes ``veladd`` wirkt
    als Beschleunigung — bei geworfenen Objekten ist das die Schwerkraft, die
    aus dem geraden Wurf erst die Flugkurve macht. Bedingte Varianten desselben
    Controllers bleiben aussen vor, weil ihr Zustand hier nicht bekannt ist.
    """
    header = state.get("header") or {}
    velocity = _seq_vec2(header.get("velset"), (0.0, 0.0))
    acceleration = [0.0, 0.0]
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        ctype = (params.get("type") or "").lower()
        if ctype not in ("velset", "veladd"):
            continue
        lines = _trigger_lines(ctrl.get("triggers"))
        if not lines:
            continue
        always = all(_ALWAYS_TRIGGER_RE.match(line) for line in lines)
        if ctype == "veladd" and always:
            acceleration = _axis_pair(params, acceleration)
        elif ctype == "velset" and (always or all(
                _ALWAYS_TRIGGER_RE.match(line) or _ENTRY_TRIGGER_RE.match(line)
                for line in lines)):
            velocity = _axis_pair(params, velocity)
    return {"vel": velocity, "accel": acceleration}


def _collect_actors(
    state: dict[str, Any],
    by_number: dict[int, dict[str, Any]],
    elem_ticks: dict[int, list[int]] | None,
    key_for: Any,
    base_time: int,
    base_pos: list[int],
    visited: frozenset[int],
    depth: int,
    out: list[dict[str, Any]],
) -> None:
    """Laeuft einen Helper-State als eigenen Akteur ab und haengt ihn an ``out``.

    In MUGEN ist ein Helper ein vollwertiger Charakter (Ikemen: ``Char`` mit
    ``helperIndex != 0``) mit eigener Animation, Position und State-Machine.
    ``ChangeState`` *ersetzt* dabei seine Animation, statt eine zweite
    danebenzulegen — deshalb wird die Kette als ``anims`` gefuehrt und nicht als
    Liste unabhaengiger Effekte. Weitere Helfer werden zu eigenen Akteuren;
    ``visited`` bricht die Zyklen selbst-respawnender Helfer.
    """
    if depth > _HELPER_MAX_DEPTH:
        return
    header = state.get("header") or {}
    actor: dict[str, Any] = {
        "time": base_time,
        "pos": list(base_pos),
        "anims": [],
        "effects": [],
        "sounds": [],
        "hits": [],
        "binds": [],
        # Ein Helfer ist ein Charakter und bringt seine eigene Zeichenprioritaet
        # mit (Ikemen sortiert Charaktere und Effekte in einer Liste, anim.go).
        "sprpriority": _seq_int(header.get("sprpriority"), 0),
        **_actor_motion(state),
    }
    out.append(actor)

    seen: set[int] = set(visited)
    current_state: dict[str, Any] | None = state
    offset = base_time

    while current_state is not None:
        number = current_state.get("number")
        if isinstance(number, int):
            seen.add(number)
        current = _seq_anim_number((current_state.get("header") or {}).get("anim"))
        key = key_for(current)
        if key is not None and not any(a["time"] == offset for a in actor["anims"]):
            actor["anims"].append({"anim": key, "time": offset})

        for ctrl in current_state.get("controllers") or []:
            params = ctrl.get("params") or {}
            ctype = (params.get("type") or "").lower()
            tick, period = _seq_trigger_timing(ctrl.get("triggers"), elem_ticks, current)

            if ctype == "changeanim":
                changed = _seq_anim_number(params.get("value"))
                if changed is not None:
                    current = changed
                    changed_key = key_for(changed)
                    # Gleicher Tick = alternativer Zweig, nicht der naechste Schritt.
                    if changed_key is not None and not any(
                            a["time"] == offset + tick for a in actor["anims"]):
                        actor["anims"].append({"anim": changed_key, "time": offset + tick})
                continue

            if ctype == "playsnd":
                sound = _seq_sound(params.get("value"))
                if sound is not None:
                    actor["sounds"].append({
                        "group": sound[0], "index": sound[1],
                        "time": offset + tick, "period": period,
                        "volume": _seq_volume(params),
                        "loop": 1 if _seq_int(params.get("loop"), 0) else 0,
                    })
                continue

            if ctype == "hitdef":
                hit = _seq_hitdef(params, offset + tick, key_for)
                if hit is not None:
                    actor["hits"].append(hit)
                continue

            if ctype in ("bindtoparent", "bindtoroot"):
                # Der Helfer klebt am Charakter statt frei zu fliegen; `pos`
                # ist der Versatz zu dessen Achse (Ikemen: bindPos/bindTime).
                bind_pos = _seq_pos(params.get("pos"))
                actor["binds"].append({
                    "time": offset + tick,
                    "pos": bind_pos,
                    "duration": _seq_int(params.get("time"), 1),
                })
                continue

            if ctype == "helper":
                nested_no = _seq_anim_number(params.get("stateno"))
                nested = by_number.get(nested_no) if nested_no is not None else None
                if nested is not None and nested_no not in seen:
                    nested_pos = _seq_pos(params.get("pos"))
                    _collect_actors(
                        nested, by_number, elem_ticks, key_for, offset + tick,
                        [base_pos[0] + nested_pos[0], base_pos[1] + nested_pos[1]],
                        frozenset(seen | {nested_no}), depth + 1, out)
                continue

            if ctype in ("explod", "projectile"):
                pos_raw = params.get("pos") if params.get("pos") is not None else params.get("offset")
                pos = _seq_pos(pos_raw)
                raw_anim = ((params.get("projanim") or params.get("anim"))
                            if ctype == "projectile" else params.get("anim"))
                num = _seq_resolve_anim(raw_anim)
                fx_key = key_for(num if num is not None else current)
                if fx_key is not None:
                    entry = _explod_entry(
                        params, fx_key, offset + tick, period, pos,
                        ctype == "projectile", key_for)
                    actor["effects"].append(entry)
                    if ctype == "projectile":
                        removal = _projectile_removal(params, entry, key_for)
                        if removal is not None:
                            actor["effects"].append(removal)
                continue

        follow_state, follow_ctrl = _pick_followup(current_state, by_number, key_for, seen)
        if follow_state is None:
            break
        offset += _followup_tick(
            follow_ctrl.get("triggers"), elem_ticks, current, current_state)
        current_state = follow_state



def _seq_vec2(raw: Any, default: tuple[float, float]) -> list[float]:
    """Liest ein ``x, y``-Zahlenpaar; nicht auswertbare Achsen behalten den Default."""
    values = list(default)
    if raw is None:
        return values
    parts = [p.strip() for p in str(raw).split(",")]
    for i in range(min(2, len(parts))):
        try:
            values[i] = float(parts[i])
        except ValueError:
            pass
    return values


def _explod_entry(
    params: dict[str, Any],
    key: str,
    spawn_time: int,
    period: int,
    pos: list[int],
    is_projectile: bool = False,
    key_for: Any = None,
) -> dict[str, Any]:
    """Baut den Laufzeit-Eintrag eines Explod/Projectile/Helper-Effekts.

    Eine gemeinsame Stelle fuer den direkten Statedef-Controller und die
    Explods innerhalb eines Helper-States, damit beide Wege dieselben Felder
    liefern. Defaults folgen Ikemens ``Explod.initFromChar``. Projectiles tragen
    dieselben Werte unter eigenen Parameternamen und haengen nie am Charakter.
    """
    trans = str(params.get("trans") or "none").lower()
    scale_raw = params.get("projscale") if is_projectile else params.get("scale")
    prio_raw = params.get("projsprpriority") if is_projectile else params.get("sprpriority")
    remove_raw = params.get("projremovetime") if is_projectile else params.get("removetime")
    angle_raw = params.get("projangle") if is_projectile else params.get("angle")
    # Projectile bremst ueber `velmul`, Explod ueber `friction` (src/char.go).
    damp_raw = params.get("velmul") if is_projectile else params.get("friction")
    vel = _seq_vec2(params.get("velocity") or params.get("vel"), (0.0, 0.0))
    # Ikemen spiegelt ein rueckwaerts fliegendes Projektil (`commitProjectile`):
    # facing kippt, waehrend die Weltrichtung der Bewegung gleich bleibt.
    facing = -1 if is_projectile and vel[0] < 0 else _seq_int(params.get("facing"), 1)
    # Ein Projectile-Controller ist zugleich ein HitDef (`isHitDefParam` in
    # src/bytecode.go). Funke und Trefferton haengen am Geschoss, nicht am
    # Charakter — sie fallen an, wenn es einschlaegt, nicht beim Abschuss.
    hit = _seq_hitdef(params, 0, key_for) if is_projectile and key_for is not None else None
    # `projhitanim` ersetzt beim Einschlag die fliegende Animation (`ProjHit`
    # in Projectile.update) — das Geschoss zerplatzt, statt weiterzufliegen.
    hit_anim = None
    if is_projectile and key_for is not None:
        hit_num = _seq_resolve_anim(params.get("projhitanim"))
        hit_anim = key_for(hit_num) if hit_num is not None else None
    entry = {
        "anim": key,
        "time": spawn_time,
        "period": period,
        "id": _seq_int(params.get("id") or params.get("projid"), -1),
        "pos": pos,
        "postype": str(params.get("postype") or "p1").lower(),
        "sprpriority": _seq_anim_number(prio_raw) or 0,
        "layer": _seq_layer(params),
        "scale": _seq_scale(scale_raw),
        "angle": _seq_float(angle_raw, 0.0),
        # MUGEN-Pixel pro Tick; wirkt erst, wenn bindtime abgelaufen ist.
        "vel": vel,
        "accel": _seq_vec2(params.get("accel"), (0.0, 0.0)),
        "friction": _seq_vec2(damp_raw, (1.0, 1.0)),
        "bindtime": 0 if is_projectile else _seq_int(params.get("bindtime"), 1),
        "facing": 1 if facing >= 0 else -1,
        "vfacing": 1 if _seq_int(params.get("vfacing"), 1) >= 0 else -1,
        # -2 = bis Anim-Ende, -1 = dauerhaft, >=0 = Ticks.
        "removetime": _seq_int(remove_raw, -1 if is_projectile else -2),
        "trans": trans,
        "alpha": _seq_alpha(trans, params.get("alpha")),
    }
    if hit is not None:
        entry["hit"] = hit
    if hit_anim is not None:
        entry["hitanim"] = hit_anim
    return entry


# Ein Projektil, das laenger als 10 Sekunden fliegt, hat den Move ohnehin verlassen.
_MAX_FLIGHT_TICKS = 600


def _projectile_flight(entry: dict[str, Any], ticks: int) -> list[int]:
    """Position eines Projektils nach ``ticks`` Ticks, relativ zum Spawnpunkt.

    Gerechnet wird im facing-relativen Raum, in dem auch ``pos`` steht: Ikemen
    bewegt mit ``velocity * facing``, und beide Vorzeichen kippen gemeinsam.
    Reihenfolge und Abbremsschwelle folgen der Laufzeit in
    ``MugenMoveStage.StepEffectMotion``, damit der Auflöse-Effekt genau dort
    erscheint, wo das Geschoss zu sehen war.
    """
    x, y = float(entry["pos"][0]), float(entry["pos"][1])
    vx, vy = entry["vel"]
    ax, ay = entry["accel"]
    mx, my = entry["friction"]
    for _ in range(min(max(0, ticks), _MAX_FLIGHT_TICKS)):
        x += vx
        y += vy
        vx = vx * mx + ax
        vy = vy * my + ay
        if abs(mx) < 1.0 and abs(vx) < 0.1:
            vx = 0.0
        if abs(my) < 1.0 and abs(vy) < 0.1:
            vy = 0.0
    return [int(round(x)), int(round(y))]


def _projectile_removal(
    params: dict[str, Any],
    entry: dict[str, Any],
    key_for: Any,
) -> dict[str, Any] | None:
    """Der Auflöse-Effekt eines Projektils an seinem Endpunkt.

    Laeuft ``projremovetime`` ab, tauscht Ikemen die Animation gegen
    ``projremanim`` und fliegt mit ``remvelocity`` ohne Beschleunigung weiter
    (``Projectile.update`` in src/char.go). Fehlt ``projremanim``, greift
    ``projhitanim`` — dieselbe Ersatzkette wie im Projectile-Controller.
    Ohne diesen Eintrag verschwindet ein Geschoss im Showcase einfach, statt
    sichtbar zu zerplatzen.
    """
    life = entry["removetime"]
    if life < 0:
        return None
    raw = params.get("projremanim")
    if raw is None:
        raw = params.get("projhitanim")
    num = _seq_resolve_anim(raw)
    key = key_for(num) if num is not None else None
    if key is None:
        return None
    removal = dict(entry)
    removal.pop("hit", None)
    removal.pop("hitanim", None)
    removal.update({
        "anim": key,
        "time": entry["time"] + life,
        "period": 0,
        "pos": _projectile_flight(entry, life),
        "vel": _seq_vec2(params.get("remvelocity"), (0.0, 0.0)),
        "accel": [0.0, 0.0],
        "friction": [1.0, 1.0],
        "removetime": -2,
    })
    return removal


def _helper_state_numbers(by_number: dict[int, dict[str, Any]]) -> set[int]:
    """Statedefs, die MUGEN ausschliesslich als Helfer eines Moves startet.

    Nur direkt per ``Helper stateno`` gespawnte Zustaende zaehlen. Ihren
    ``ChangeState``-Ketten zu folgen waere falsch: die enden regelmaessig in
    Standard-Zustaenden wie 0 (Stand) oder 5100 (Aufprall), und die sind
    eigenstaendige Zustaende des Charakters, keine Move-Fragmente.
    """
    reserved = set(range(0, 200)) | set(range(5000, 6000))
    found: set[int] = set()
    for state in by_number.values():
        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            if (params.get("type") or "").lower() != "helper":
                continue
            number = _seq_anim_number(params.get("stateno"))
            if number is not None and number in by_number and number not in reserved:
                found.add(number)
    return found


def _anim_total_ticks(elem_ticks: dict[int, list[int]] | None, anim_no: int | None) -> int:
    """Gesamtlaenge einer Action in Ticks (0, wenn unbekannt)."""
    if not elem_ticks or anim_no is None:
        return 0
    return sum(elem_ticks.get(anim_no) or [])


# Trigger, die den Zustand erst am Ende der laufenden Animation verlassen.
_ANIM_END_TRIGGERS = ("animtime",)
# Trigger, die beim Einschlag greifen. Der Wechsel passiert dann nicht am
# Animationsende, sondern sobald die HitDef trifft — zuzueglich ihrer pausetime,
# waehrend der die Zustandsmaschine des Angreifers steht (char.go: pauseBool).
_HIT_TRIGGERS = ("movehit", "movecontact", "moveguarded")


def _hit_contact_tick(
    state: dict[str, Any] | None,
    elem_ticks: dict[int, list[int]] | None,
    anim_no: int | None,
) -> int:
    """Frueheste Tick, an dem ein Angriff dieses States treffen kann."""
    best = 0
    for ctrl in (state or {}).get("controllers") or []:
        params = ctrl.get("params") or {}
        if (params.get("type") or "").lower() not in ("hitdef", "projectile"):
            continue
        tick = _seq_trigger_time(ctrl.get("triggers"), elem_ticks, anim_no)
        tick += _seq_anim_number(params.get("pausetime")) or 0
        if tick > 0 and (best == 0 or tick < best):
            best = tick
    return best


def _followup_tick(
    triggers: Any,
    elem_ticks: dict[int, list[int]] | None,
    anim_no: int | None,
    state: dict[str, Any] | None = None,
) -> int:
    """Zeitpunkt eines ``ChangeState`` innerhalb eines Moves.

    Der haeufigste Ausloeser ist ``AnimTime = 0`` — der Wechsel passiert also,
    wenn die laufende Animation durch ist. Fuer alles andere gilt der normale
    Trigger-Tick.
    """
    tick, _ = _seq_trigger_timing(triggers, elem_ticks, anim_no)
    if tick > 0:
        return tick
    text = json.dumps(triggers or {}).lower()
    if any(marker in text for marker in _HIT_TRIGGERS):
        return _hit_contact_tick(state, elem_ticks, anim_no) or _anim_total_ticks(
            elem_ticks, anim_no)
    return (_anim_total_ticks(elem_ticks, anim_no)
            if any(marker in text for marker in _ANIM_END_TRIGGERS) else 0)


# Wie viele ``ChangeState``-Schritte eine Body-Kette weiterverfolgt wird.
_BODY_CHAIN_MAX = 4


def _chain_body_followups(
    state: dict[str, Any],
    state_no: int,
    last_anim: int | None,
    by_number: dict[int, dict[str, Any]],
    key_for: Any,
    elem_ticks: dict[int, list[int]] | None,
    body_keys: list[dict[str, Any]],
) -> None:
    """Setzt die Body-Kette ueber ``ChangeState`` hinweg fort.

    MUGEN spielt kurze Uebergaenge einmal ab und wechselt bei ``AnimTime = 0``
    in den Folgezustand: Statedef 10 (Stehen -> Ducken, zwei Frames) geht nach
    11 (Hocke) ueber. Ohne diesen Schritt wiederholt die Showcase die zwei
    Frames endlos und es sieht aus wie ein Flackern.
    """
    if not body_keys:
        return
    seen = {state_no}
    node: dict[str, Any] | None = state
    offset = body_keys[-1]["time"]
    current = last_anim

    for _ in range(_BODY_CHAIN_MAX):
        follow, ctrl = _pick_followup(node, by_number, key_for, seen)
        number = follow.get("number") if follow is not None else None
        # Statedef 0 ist der Stand: dort ist der Move zu Ende, nicht Teil davon.
        if number is None or number == 0:
            return
        step = _followup_tick((ctrl or {}).get("triggers"), elem_ticks, current, node)
        # Ohne messbare Restlaufzeit ist der Zustand ein Haltezustand (Hocke,
        # Guard). MUGEN verlaesst ihn erst auf Eingabe — die Kette endet hier.
        if step <= 0:
            return
        offset += step
        current = _state_entry_anim(follow)
        key = key_for(current)
        if key is None or any(entry["anim"] == key for entry in body_keys):
            return
        body_keys.append({"anim": key, "time": offset})
        seen.add(number)
        node = follow


def _helper_effects(
    helper_state: dict[str, Any],
    base_pos: list[int],
    base_time: int,
    key_for: Any,
    elem_ticks: dict[int, list[int]] | None = None,
    by_number: dict[int, dict[str, Any]] | None = None,
    visited: frozenset[int] = frozenset(),
    depth: int = 0,
) -> list[dict[str, Any]]:
    """Sichtbare Effekte *innerhalb* eines Helper-States.

    Viele Moves spawnen einen Helper, dessen eigener State erst die Klinge, den
    Strahl oder die Aura traegt. Offsets und Spawn-Ticks sind relativ zum Helfer,
    also werden beide auf dessen Werte addiert. Der Helfer selbst ist sichtbar,
    wenn seine Header-Animation im Atlas existiert, und er wechselt per
    ``ChangeState`` in Folge-States weiter — Start-, Treffer- und Endanimation
    eines Moves liegen in MUGEN genau so verteilt. ``visited`` bricht die Zyklen,
    die sich selbst neu spawnende Helfer bilden.
    """
    if depth > _HELPER_MAX_DEPTH:
        return []
    current = _seq_anim_number((helper_state.get("header") or {}).get("anim"))
    out: list[dict[str, Any]] = []
    for ctrl in helper_state.get("controllers") or []:
        params = ctrl.get("params") or {}
        ctype = (params.get("type") or "").lower()
        if ctype == "changeanim":
            changed = _seq_anim_number(params.get("value"))
            if changed is not None:
                current = changed
            continue

        if ctype in ("changestate", "selfstate"):
            next_no = _seq_anim_number(params.get("value"))
            nxt = by_number.get(next_no) if by_number and next_no is not None else None
            if nxt is None or next_no in visited:
                continue
            follow_time = base_time + _followup_tick(
                ctrl.get("triggers"), elem_ticks, current, helper_state)
            next_anim = _state_entry_anim(nxt)
            next_key = key_for(next_anim)
            if next_key is not None:
                out.append(_explod_entry(
                    {"type": "explod", "removetime": -2}, next_key, follow_time, 0, list(base_pos)))
            out.extend(_helper_effects(
                nxt, base_pos, follow_time, key_for, elem_ticks,
                by_number, visited | {next_no}, depth + 1))
            continue

        if ctype not in ("explod", "helper", "projectile"):
            continue
        pos_raw = params.get("pos") if params.get("pos") is not None else params.get("offset")
        pos = _seq_pos(pos_raw)
        spawn_time, period = _seq_trigger_timing(
            ctrl.get("triggers"), elem_ticks, current)
        abs_pos = [base_pos[0] + pos[0], base_pos[1] + pos[1]]

        if ctype == "helper":
            nested_no = _seq_anim_number(params.get("stateno"))
            nested = by_number.get(nested_no) if by_number and nested_no is not None else None
            if nested is not None and nested_no not in visited:
                out.extend(_helper_effects(
                    nested, abs_pos, base_time + spawn_time, key_for, elem_ticks,
                    by_number, visited | {nested_no}, depth + 1))
            continue

        if ctype == "projectile":
            # `projanim = Anim` meint die gerade laufende Animation des Helfers.
            num = _seq_resolve_anim(params.get("projanim") or params.get("anim"))
            key = key_for(num if num is not None else current)
            if key is not None:
                entry = _explod_entry(
                    params, key, base_time + spawn_time, period, abs_pos, True, key_for)
                out.append(entry)
                removal = _projectile_removal(params, entry, key_for)
                if removal is not None:
                    out.append(removal)
            continue

        key = key_for(_seq_resolve_anim(params.get("anim")))
        if key is not None:
            out.append(_explod_entry(params, key, base_time + spawn_time, period, abs_pos))
    return out


def _apply_removals(
    effects: list[dict[str, Any]],
    removals: list[tuple[int, int]],
) -> None:
    """Uebersetzt ``RemoveExplod``-Controller in ein ``removeat`` je Effekt.

    Statt zur Laufzeit IDs zu verwalten, wird der frueheste passende
    Loeschzeitpunkt nach dem Spawn direkt in den Effekt geschrieben. Eine
    ID von -1 im Controller trifft alle Explods des States.
    """
    if not removals:
        return
    for entry in effects:
        for remove_id, remove_time in removals:
            if remove_id not in (-1, entry["id"]):
                continue
            if remove_time <= entry["time"]:
                continue
            current = entry.get("removeat")
            if current is None or remove_time < current:
                entry["removeat"] = remove_time


def _build_sequences(
    states: list[dict[str, Any]],
    valid_keys: set[str],
    elem_ticks: dict[int, list[int]] | None = None,
) -> list[dict[str, Any]]:
    """Group animations that belong together, per MUGEN state machine.

    For each `[Statedef N]` we read the visible body chain (`header.anim` +
    ordered `ChangeAnim`, each with its `time = N` tick) and the effects it
    spawns (`Explod`, `Projectile`, `Helper`). Both are emitted as timed objects
    so a viewer can composite them the way MUGEN does — the body switching over
    time with the effects overlaid on top, not a flat sequential list:

        anims:   {anim, time}
        effects: {anim, time, period, pos:[x,y], postype, sprpriority,
                  scale:[sx,sy], facing, vfacing, removetime, trans, alpha}

    `time` comes from the controller's `time = N` / `animelem = N` trigger, the
    rest from its params. Only *compound* moves are emitted (more than one body
    anim OR at least one effect). Animation numbers with no matching atlas key
    are dropped. Negative statedefs are skipped — MUGEN's per-frame global states.
    """
    by_number: dict[int, dict[str, Any]] = {}
    for s in states:
        n = s.get("number")
        if isinstance(n, int):
            by_number.setdefault(n, s)

    def key_for(num: int | None) -> str | None:
        if num is None:
            return None
        candidate = f"action_{num}"
        return candidate if candidate in valid_keys else None

    helper_states = _helper_state_numbers(by_number)
    sequences: list[dict[str, Any]] = []
    for state in states:
        state_no = state.get("number")
        if not isinstance(state_no, int):
            continue
        # -1 puffert nur Eingaben und schaltet States um, hat also nie ein Bild.
        # -2/-3 laufen ebenfalls in jedem Frame, tragen dort aber die Dauereffekte
        # eines Charakters (Auren, Partikel, Spuren). Sie sind keine Moves und
        # bekommen deshalb keine Koerper-Animation, nur ihre Effektspur.
        is_global = state_no < 0
        if state_no == -1:
            continue
        header = state.get("header") or {}
        body: list[dict[str, Any]] = []
        head_num = None if is_global else _seq_anim_number(header.get("anim"))
        if head_num is not None:
            body.append({"num": head_num, "time": 0})
        # `animelem` bezieht sich immer auf die gerade laufende Animation.
        current_anim = head_num
        # MUGEN traegt die Zeichenprioritaet des Charakters im Statedef-Header und
        # aendert sie danach ueber `SprPriority`. Ohne diese Spur bleibt der Body
        # immer auf 0 und Hyper-Panels mit hoher Prioritaet decken ihn zu.
        prio_entries: list[dict[str, Any]] = []
        head_prio = _seq_anim_number(header.get("sprpriority"))
        if head_prio is not None:
            prio_entries.append({"time": 0, "value": head_prio})
        # `StateTypeSet physics = ...` schaltet die Koerperphysik mitten im State um:
        # ein Sprung faengt am Boden an und wird erst beim Absprung zu `A`.
        physics_entries: list[dict[str, Any]] = []
        # `Turn` dreht die Blickrichtung, `AssertSpecial invisible` blendet den
        # Koerper aus — beides gilt ab seinem Tick bis zum Ende des States.
        turn_entries: list[dict[str, Any]] = []
        invisible_entries: list[dict[str, Any]] = []
        # `AngleSet` merkt sich einen Winkel, `AngleDraw` zeichnet damit. Ohne
        # diese Spur stehen gedrehte Posen (Sturz, Wirbel, Schleuder) aufrecht.
        angle_entries: list[dict[str, Any]] = []
        current_angle = 0.0
        effect_entries: list[dict[str, Any]] = []
        sound_entries: list[dict[str, Any]] = []
        palfx_entries: list[dict[str, Any]] = []
        pause_entries: list[dict[str, Any]] = []
        move_entries: list[dict[str, Any]] = []
        hit_entries: list[dict[str, Any]] = []
        afterimage_entries: list[dict[str, Any]] = []
        modify_entries: list[dict[str, Any]] = []
        bodytrans_entries: list[dict[str, Any]] = []
        victim_entries: list[dict[str, Any]] = []
        helper_actors: list[dict[str, Any]] = []
        removals: list[tuple[int, int]] = []
        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            ctype = (params.get("type") or "").lower()
            if ctype == "changeanim":
                num = _seq_resolve_anim(params.get("value"))
                if num is not None:
                    body.append({
                        "num": num,
                        "time": _seq_trigger_time(
                            ctrl.get("triggers"), elem_ticks, current_anim),
                    })
                    current_anim = num
                continue

            # ChangeAnim2 setzt die Animation des GEGNERS, gezeichnet aus den
            # Sprites dieses Charakters — so entstehen Wurf-, Griff- und
            # Opferdarstellungen. Die Spur treibt in der Showcase die Puppe.
            if ctype == "changeanim2":
                num = _seq_anim_number(params.get("value"))
                key = key_for(num)
                if key is not None:
                    victim_entries.append({
                        "anim": key,
                        "time": _seq_trigger_time(
                            ctrl.get("triggers"), elem_ticks, current_anim),
                    })
                continue

            if ctype == "playsnd":
                sound = _seq_sound(params.get("value"))
                if sound is not None:
                    play_time, play_period = _seq_trigger_timing(
                        ctrl.get("triggers"), elem_ticks, current_anim)
                    sound_entries.append({
                        "group": sound[0],
                        "index": sound[1],
                        "time": play_time,
                        "period": play_period,
                        "volume": _seq_volume(params),
                        "loop": 1 if _seq_int(params.get("loop"), 0) else 0,
                    })
                continue

            if ctype == "removeexplod":
                remove_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                # Ohne `id` raeumt MUGEN alle Explods des Chars ab.
                removals.append((_seq_int(params.get("id"), -1), remove_time))
                continue

            if ctype in ("palfx", "allpalfx"):
                fx_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                entry = _seq_palfx(params, fx_time, "all" if ctype == "allpalfx" else "self")
                if entry is not None:
                    palfx_entries.append(entry)
                continue

            if ctype in ("superpause", "pause"):
                pause_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                pause_entries.append({
                    "time": pause_time,
                    "duration": _seq_int(params.get("time"), 30 if ctype == "superpause" else 0),
                })
                continue

            if ctype in _MOVE_CONTROLLERS:
                move_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                entry = _seq_move(params, move_time, ctype)
                if entry is not None:
                    move_entries.append(entry)
                continue

            if ctype == "hitdef":
                hit_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                entry = _seq_hitdef(params, hit_time, key_for)
                if entry is not None:
                    hit_entries.append(entry)
                continue

            if ctype in ("afterimage", "afterimagetime"):
                ai_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                afterimage_entries.append(
                    _seq_afterimage(params, ai_time, ctype == "afterimagetime"))
                continue

            if ctype == "modifyexplod":
                mod_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                entry = _seq_modify_explod(params, mod_time)
                if entry is not None:
                    modify_entries.append(entry)
                continue

            if ctype == "sprpriority":
                prio_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                prio_entries.append({
                    "time": prio_time,
                    "value": _seq_int(params.get("value"), 0),
                })
                continue

            if ctype == "statetypeset":
                new_phys = str(params.get("physics") or "").strip().upper()[:1]
                if new_phys:
                    phys_time, _ = _seq_trigger_timing(
                        ctrl.get("triggers"), elem_ticks, current_anim)
                    physics_entries.append({"time": phys_time, "value": new_phys})
                continue

            if ctype == "turn":
                turn_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                turn_entries.append({"time": turn_time})
                continue

            if ctype == "assertspecial":
                flags = {str(params.get(k) or "").strip().lower()
                         for k in ("flag", "flag2", "flag3")}
                if "invisible" not in flags:
                    continue
                hide_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                invisible_entries.append({"time": hide_time})
                continue

            if ctype in ("angledraw", "angleset", "angleadd"):
                angle_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                raw_angle = params.get("value")
                if raw_angle is not None:
                    value = _seq_float(raw_angle, 0.0)
                    current_angle = current_angle + value if ctype == "angleadd" else value
                angle_entries.append({
                    "time": angle_time,
                    "angle": current_angle,
                    # Nur `AngleDraw` kennt einen eigenen Skalierungsfaktor.
                    "scale": _seq_scale(params.get("scale")) if ctype == "angledraw" else [1.0, 1.0],
                })
                continue

            if ctype == "trans":
                trans_time, _ = _seq_trigger_timing(
                    ctrl.get("triggers"), elem_ticks, current_anim)
                trans_mode = str(params.get("trans") or "none").lower()
                bodytrans_entries.append({
                    "time": trans_time,
                    "trans": trans_mode,
                    "alpha": _seq_alpha(trans_mode, params.get("alpha")),
                })
                continue

            num: int | None = None
            helper_state: dict[str, Any] | None = None
            if ctype == "explod":
                num = _seq_resolve_anim(params.get("anim"))
            elif ctype == "projectile":
                num = _seq_resolve_anim(
                    params.get("projanim") if params.get("projanim") is not None else params.get("anim")
                )
                if num is None:
                    # `projanim = Anim` verweist auf die gerade laufende Animation
                    # des Chars — statisch ist das genau `current_anim`.
                    num = current_anim
            elif ctype == "helper":
                helper_state = by_number.get(_seq_anim_number(params.get("stateno")) or -1)
                if helper_state is not None:
                    num = _seq_resolve_anim((helper_state.get("header") or {}).get("anim"))

            spawn_time, spawn_period = _seq_trigger_timing(
                ctrl.get("triggers"), elem_ticks, current_anim)
            # Projectiles carry the spawn offset in `offset`, Explod/Helper in `pos`.
            pos_raw = params.get("pos") if params.get("pos") is not None else params.get("offset")
            base_pos = _seq_pos(pos_raw)

            # Helper sind eigene Charaktere mit eigener Animationskette — sie
            # werden als Akteure gefuehrt, nicht als flache Effekte.
            if helper_state is not None:
                helper_no = _seq_anim_number(params.get("stateno"))
                _collect_actors(
                    helper_state, by_number, elem_ticks, key_for, spawn_time, base_pos,
                    frozenset({state_no, helper_no} - {None}), 0, helper_actors)
                continue

            key = key_for(num)
            if key is None:
                continue
            entry = _explod_entry(
                params, key, spawn_time, spawn_period, base_pos,
                ctype == "projectile", key_for)
            effect_entries.append(entry)
            if ctype == "projectile":
                removal = _projectile_removal(params, entry, key_for)
                if removal is not None:
                    effect_entries.append(removal)

        # Order-preserving unique body chain (several ChangeAnim can repeat the
        # same anim across trigger branches), then map to atlas keys. The tick is
        # kept so a viewer can switch the body over time instead of showing a list.
        body_keys: list[dict[str, Any]] = []
        body_seen: set[str] = set()
        # Ein ChangeAnim in -2/-3 gilt nur fuer den gerade laufenden State und
        # ergaebe als eigene Koerperspur ein falsches Bild.
        if is_global:
            body.clear()
        body_ticks: set[int] = set()
        last_body_num: int | None = None
        for item in body:
            key = key_for(item["num"])
            if key is None or key in body_seen:
                continue
            # Mehrere ChangeAnim auf demselben Tick sind sich ausschliessende
            # Zweige (etwa Vel X > 0 gegen Vel X < 0), keine Abfolge. MUGEN waehlt
            # einen davon; ohne Trigger-Auswertung gilt der zuerst deklarierte —
            # bei Tick 0 also die Animation aus dem Statedef-Header.
            if item["time"] in body_ticks:
                continue
            body_ticks.add(item["time"])
            body_seen.add(key)
            body_keys.append({"anim": key, "time": item["time"]})
        body_keys.sort(key=lambda b: b["time"])
        if body_keys:
            last_key = body_keys[-1]["anim"]
            last_body_num = next(
                (item["num"] for item in body if key_for(item["num"]) == last_key), None)
        if not is_global:
            _chain_body_followups(
                state, state_no, last_body_num, by_number, key_for, elem_ticks, body_keys)
        # Dedupe by full spawn identity, not by anim: MUGEN builds beams/auras by
        # spawning the SAME anim at several offsets/ticks, so collapsing per anim
        # would delete most of an effect. Only exact repeats (the same controller
        # reached through several trigger branches) are dropped.
        body_set = body_seen
        seen: set[tuple[str, int, int, int, int]] = set()
        effects: list[dict[str, Any]] = []
        for entry in effect_entries:
            if entry["anim"] in body_set:
                continue
            ident = (entry["anim"], entry["time"], entry["period"],
                     entry["pos"][0], entry["pos"][1])
            if ident in seen:
                continue
            seen.add(ident)
            effects.append(entry)
        _apply_removals(effects, removals)
        effects.sort(key=lambda e: (e["time"], e["sprpriority"]))

        sound_seen: set[tuple[int, int, int]] = set()
        sounds: list[dict[str, Any]] = []
        for entry in sound_entries:
            ident = (entry["group"], entry["index"], entry["time"])
            if ident in sound_seen:
                continue
            sound_seen.add(ident)
            sounds.append(entry)
        sounds.sort(key=lambda s: s["time"])
        palfx_entries.sort(key=lambda p: p["time"])
        pause_entries.sort(key=lambda p: p["time"])
        move_entries.sort(key=lambda m: m["time"])
        hit_entries.sort(key=lambda h: h["time"])
        afterimage_entries.sort(key=lambda a: a["time"])
        modify_entries.sort(key=lambda m: m["time"])
        bodytrans_entries.sort(key=lambda t: t["time"])
        victim_entries.sort(key=lambda v: v["time"])
        prio_entries.sort(key=lambda p: p["time"])
        physics_entries.sort(key=lambda p: p["time"])
        turn_entries.sort(key=lambda t: t["time"])
        invisible_entries.sort(key=lambda t: t["time"])
        angle_entries.sort(key=lambda a: a["time"])

        for actor in helper_actors:
            actor["anims"].sort(key=lambda a: a["time"])
            actor["effects"].sort(key=lambda e: (e["time"], e["sprpriority"]))
            actor["sounds"].sort(key=lambda s: s["time"])
            actor["hits"].sort(key=lambda h: h["time"])
        helper_actors = [a for a in helper_actors
                         if a["anims"] or a["effects"] or a["sounds"] or a["hits"]]
        helper_actors.sort(key=lambda a: a["time"])

        # Alles mit sichtbarem Inhalt kommt mit. Frueher fielen Statedefs mit nur
        # einer Animation heraus — genau dort liegen aber die charaktereigenen
        # Zustaende, die kein Standard-MUGEN-Band abdeckt.
        if not body_keys and not effects and not helper_actors and not victim_entries:
            continue
        sequences.append({
            "state": state_no,
            # Reihenfolge der Namensquellen: der Statedef-Kommentar ist am
            # aussagekraeftigsten, danach die Block-Ueberschrift im CNS, zuletzt
            # die MUGEN-Reserved-Action-Konvention.
            "label": ((state.get("label") or "").strip()
                      or (state.get("section") or "").strip()
                      or (f"Dauereffekte ({state_no})" if is_global else "")
                      or (_action_label(body[0]["num"]) if body else "")),
            "group": (state.get("section") or "").strip(),
            "category": _state_category(state_no, body_keys, helper_states),
            "anims": body_keys,
            "effects": effects,
            "sounds": sounds,
            "palfx": palfx_entries,
            "pauses": pause_entries,
            "moves": move_entries,
            "hits": hit_entries,
            "afterimage": afterimage_entries,
            "modifies": modify_entries,
            "bodytrans": bodytrans_entries,
            "victim": victim_entries,
            "sprpriority": prio_entries,
            "bodyangle": angle_entries,
            "helpers": helper_actors,
            # `physics` steuert, wie MUGEN den Koerper jeden Tick nachfuehrt:
            # A = Schwerkraft, S/C = Reibung auf vel.x, N = gar nichts.
            "physics": str(header.get("physics") or "").strip().upper()[:1],
            "physicschanges": physics_entries,
            "turns": turn_entries,
            "invisible": invisible_entries,
            "airborne": 1 if str(header.get("physics") or "").strip().upper() == "A" else 0,
        })

    sequences.sort(key=lambda s: s["state"])
    return sequences


def convert_character(
    char_dir: Path,
    atlas_name: str | None = None,
    directions_mode: int = 2,
) -> Path:
    animations_path = char_dir / "animations.json"
    atlas_sprites_path = char_dir / "atlas.sprites.json"
    if not animations_path.is_file():
        raise FileNotFoundError(animations_path)
    if not atlas_sprites_path.is_file():
        raise FileNotFoundError(atlas_sprites_path)

    if directions_mode not in (2, 8):
        raise ValueError(f"directions_mode must be 2 or 8, got {directions_mode}")

    _set_char_size_scale(char_dir)

    animations = _load_json(animations_path)
    atlas_sprites = _load_json(atlas_sprites_path)
    sprite_index = _build_sprite_index(atlas_sprites)

    # MUGEN AIR-Nummern liegen per Konvention in 0..9999. Alles darüber stammt
    # erfahrungsgemäß aus Parser-Glitches (zerschossene Section-Header, Tokens
    # aus Kommentaren, etc.) und wird konsequent verworfen.
    MUGEN_MAX_VALID_ACTION = 9999

    out_animations: dict[str, Any] = {}
    converted = 0
    skipped: list[int] = []
    out_of_range: list[int] = []
    duplicates: list[int] = []
    identical_dropped: list[int] = []
    # Per base action number: frame signatures already emitted, to drop
    # byte-identical redundant re-declarations losslessly.
    seen_signatures: dict[str, set[str]] = {}
    for action in animations.get("actions", []):
        number = action.get("number")
        if not isinstance(number, int):
            continue
        if number < 0: #or number > MUGEN_MAX_VALID_ACTION:
            out_of_range.append(number)
            continue
        anim = _build_animation(action, sprite_index, directions_mode=directions_mode)
        if anim is None:
            skipped.append(number)
            continue

        # Drop redundant re-declarations of the same action number that carry
        # byte-identical frames — pure MUGEN authoring duplication, no data lost.
        base_key = f"action_{number}"
        signature = json.dumps(anim["frames"], separators=(",", ":"), sort_keys=True)
        signatures = seen_signatures.setdefault(base_key, set())
        if signature in signatures:
            identical_dropped.append(number)
            continue
        signatures.add(signature)

        # Always emit the lossless key first so aliases overwrite nothing.
        # Bei Kollisionen (gleiche AIR-Nummer mehrfach, aber UNTERSCHIEDLICHER
        # Inhalt) Suffix _b, _c, ... anhängen statt stillem Overwrite.
        key = base_key
        if key in out_animations:
            duplicates.append(number)
            suffix_ord = ord("b")
            while key in out_animations and suffix_ord <= ord("z"):
                key = f"{base_key}_{chr(suffix_ord)}"
                suffix_ord += 1
        anim["label"] = _action_label(number)
        out_animations[key] = anim

        alias = _ACTION_ALIASES.get(number)
        if alias and alias not in out_animations:
            # Same animation object — duplicating a reference is fine,
            # FLARE only reads, never mutates.
            out_animations[alias] = anim
        converted += 1

    if not out_animations:
        raise RuntimeError(f"No convertible actions found in {animations_path}")

    orphan_sprites = _orphan_sprite_animations(animations, sprite_index)
    for key, anim in orphan_sprites.items():
        out_animations.setdefault(key, anim)

    if "stance" not in out_animations:
        # Riftstorm code (movement, bootstrap) assumes "stance" exists. Warn loudly.
        print(
            f"[warn] {char_dir.name}: no MUGEN action 0 — 'stance' alias is missing.",
            file=sys.stderr,
        )

    # Resolve the atlas page filenames from the packer's sidecar. Falls back to
    # the single legacy atlas.png when no page list is present (older sidecars).
    pages_meta = atlas_sprites.get("pages")
    if isinstance(pages_meta, list) and pages_meta:
        images = [
            str(p.get("file"))
            for p in sorted(pages_meta, key=lambda p: int(p.get("index", 0)))
            if p.get("file")
        ]
    else:
        images = [str((atlas_sprites.get("atlas") or {}).get("file") or "atlas.png")]

    # Belong-together hints from the state machine (states.json). Baked into the
    # FLARE doc so the runtime/showcase can group body chains + spawned effects
    # without shipping states.json. Empty when no states.json is present.
    sequences: list[dict[str, Any]] = []
    seq_states_path = char_dir / "states.json"
    if seq_states_path.is_file():
        try:
            seq_states_doc = _load_json(seq_states_path)
            sequences = _build_sequences(
                seq_states_doc.get("states") or [], set(out_animations.keys()),
                _anim_elem_ticks(animations)
            )
        except Exception as ex:  # noqa: BLE001
            print(f"      [warn] states.json unreadable (sequences): {ex}", file=sys.stderr)

    movement = _char_movement(char_dir)
    flare_doc = {
        # "image" stays for backward compatibility (single-page consumers);
        # "images" carries every page in order (index 0 == atlasPage 0).
        "image": images[0],
        "images": images,
        # MUGEN `movement.yaccel`: Pixel pro Tick^2, die einen Char in der Luft ziehen.
        "gravity": movement["yaccel"],
        "standFriction": movement["stand_friction"],
        "crouchFriction": movement["crouch_friction"],
        "animations": out_animations,
        "sequences": sequences,
    }

    name = atlas_name or char_dir.name
    out_path = char_dir / f"{name}.json"
    with out_path.open("w", encoding="utf-8") as fh:
        json.dump(flare_doc, fh, indent=2)
    print(
        f"[ok] {char_dir.name}: {converted} actions converted, "
        f"{len(out_animations)} animation keys "
        f"({sum(1 for k in out_animations if not k.startswith(('action_', 'sprites_')))} aliases), "
        f"{len(orphan_sprites)} orphan sprite groups, "
        f"{len(skipped)} skipped, "
        f"{len(out_of_range)} out-of-range, "
        f"{len(duplicates)} duplicate-suffixed, "
        f"{len(identical_dropped)} identical-dropped, "
        f"{len(sequences)} sequences, "
        f"directions={directions_mode}"
    )
    if skipped:
        print(f"      skipped (no resolvable sprites): {skipped}")
    if out_of_range:
        print(
            f"      out-of-range action numbers (>{MUGEN_MAX_VALID_ACTION}, "
            f"verworfen): {out_of_range}",
            file=sys.stderr,
        )
    if duplicates:
        print(
            f"      duplicate action numbers (mit _b/_c suffix gespeichert): {duplicates}",
            file=sys.stderr,
        )
    if identical_dropped:
        print(
            f"      identische Duplikate verworfen (verlustfrei): {identical_dropped}",
            file=sys.stderr,
        )

    # Optional stats sidecar derived from MUGEN constants.json.
    constants_path = char_dir / "constants.json"
    if constants_path.is_file():
        constants = _load_json(constants_path)
        # char.json is written by mugen_import.py and carries displayName from
        # the MUGEN .def (info.displayname → cd.display_name). It's optional;
        # if missing we fall back to the folder name.
        display_name = char_dir.name
        char_meta_path = char_dir / "char.json"
        if char_meta_path.is_file():
            try:
                char_meta = _load_json(char_meta_path)
                display_name = (
                    char_meta.get("displayName")
                    or char_meta.get("name")
                    or char_dir.name
                )
            except Exception as ex:  # noqa: BLE001
                print(f"      [warn] char.json unreadable: {ex}", file=sys.stderr)
        stats = _build_stats(constants, display_name=display_name)
        # Provenienz: damit Runtime/Tooling sehen, ob der Atlas nur 2D-Side-View
        # (2) oder echte 8-Richtungs-Daten (8) trägt.
        stats["directions"] = directions_mode

        # Optional skills array sourced from states.json (HitDef controllers).
        # Independent of constants.json — a character without constants would
        # still benefit from skills, but in practice both are written by the
        # MUGEN importer side by side, so we keep them under the same branch.
        states_path = char_dir / "states.json"
        skills: list[dict[str, Any]] = []
        if states_path.is_file():
            try:
                states_doc = _load_json(states_path)
                skills = _build_skills(states_doc.get("states") or [], _ACTION_ALIASES)
            except Exception as ex:  # noqa: BLE001
                print(f"      [warn] states.json unreadable: {ex}", file=sys.stderr)
        stats["skills"] = skills

        stats_path = char_dir / f"{name}.stats.json"
        with stats_path.open("w", encoding="utf-8") as fh:
            json.dump(stats, fh, indent=2)
        print(
            f"      stats: name='{stats['displayName']}' "
            f"hp={stats['maxHp']} mp={stats['maxMana']} "
            f"str={stats['strength']} arm={stats['armor']} "
            f"radius={stats['hitRadius']}m scale=({stats['visualScale']['x']},"
            f"{stats['visualScale']['y']}) walk={stats['walkSpeed']}m/s "
            f"run={stats['runSpeed']}m/s atk={stats['attackRange']}m "
            f"proj={stats['projectileRange']}m "
            f"skills={len(stats.get('skills') or [])}"
        )
    else:
        print(f"      no constants.json found — stats sidecar skipped")

    return out_path


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Convert MUGEN importer output to FLARE NPC JSON.")
    parser.add_argument("char_dirs", nargs="+", help="One or more MUGEN character directories.")
    parser.add_argument("--atlas-name", default=None, help="Override atlas/JSON name (default = folder name).")
    parser.add_argument(
        "--directions",
        type=int,
        choices=(2, 8),
        default=2,
        help=(
            "Directional sprite mode. 2 (default) = reine 2D-Side-View: nur "
            "E-Frames, linke Slots werden gespiegelt, kein Rotations-Artefakt. "
            "8 = Legacy, liest per-direction MUGEN-Daten (selten sinnvoll, da "
            "99% aller MUGEN-Chars nur E haben)."
        ),
    )
    args = parser.parse_args(argv)

    for raw in args.char_dirs:
        path = Path(raw)
        if not path.is_dir():
            print(f"[skip] not a directory: {path}", file=sys.stderr)
            continue
        convert_character(path, args.atlas_name, directions_mode=args.directions)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
