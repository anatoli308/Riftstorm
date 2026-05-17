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
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

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
    20:   "walk",
    21:   "walk_back",
    100:  "run",
    105:  "run_back",
    # Crouch
    10:   "crouch_down",
    11:   "crouch",
    12:   "crouch_up",
    # Jump
    40:   "jump_start",
    41:   "jump",
    50:   "jump_land",
    # Guard / block
    120:  "guard_start",
    130:  "guard",
    131:  "crouch_guard",
    132:  "air_guard",
    140:  "guard_end",
    150:  "guard_hit_stand",
    151:  "guard_hit_crouch",
    152:  "guard_hit_air",
    # Stand attacks (light/medium/heavy punches + kicks).
    # "swing" = primary stand light attack so it is plug-compatible with the
    # PlayerCombatVisuals m_AnimSwing default.
    200:  "swing",
    210:  "swing_medium",
    220:  "swing_heavy",
    230:  "crouch_swing",
    240:  "crouch_swing_medium",
    250:  "crouch_swing_heavy",
    400:  "kick",
    410:  "kick_medium",
    420:  "kick_heavy",
    430:  "crouch_kick",
    440:  "crouch_kick_medium",
    450:  "crouch_kick_heavy",
    # Air attacks
    600:  "air_swing",
    610:  "air_swing_medium",
    620:  "air_swing_heavy",
    630:  "air_kick",
    640:  "air_kick_medium",
    650:  "air_kick_heavy",
    # Get-hit / fall / die (5xxx range)
    5000: "hit",
    5001: "hit",
    5010: "hit_mid",
    5011: "hit_mid",
    5020: "hit_high",
    5021: "hit_high",
    5030: "hit_low",
    5031: "hit_low",
    5040: "hit_crouch",
    5050: "fall",
    5060: "fall",
    5070: "trip",
    5080: "fall",
    5090: "fall",
    5100: "land",
    5110: "liedown",
    5120: "getup",
    5150: "die",
    5160: "die",
    5170: "die",
    5200: "stand_recover",
    5210: "crouch_recover",
    5300: "getup",
    # Throws / hold (3xxx)
    800:  "throw",
    810:  "throw",
    820:  "thrown",
    # KO / victory / continue
    180:  "victory",
    181:  "victory",
    5900: "continue",
}


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
    for fr in reference_frames:
        dur = fr.get("duration")
        if isinstance(dur, (int, float)) and dur > 0:
            frame_durations_ms.append(max(1, int(round(float(dur) * 1000.0))))
        else:
            frame_durations_ms.append(one_tick_ms)
    total_ms = sum(frame_durations_ms)
    if total_ms <= 0:
        total_ms = max(one_tick_ms, frame_count * one_tick_ms)
    total_seconds = total_ms / 1000.0

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
        "type": _resolve_animation_type(reference_frames),
        "frames": frames,
    }


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
                "groundSlideTime": int(params.get("ground.slidetime") or 0),
                "groundHitTime": int(params.get("ground.hittime") or 0),
                "airHitTime": int(params.get("air.hittime") or 0),
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
    for action in animations.get("actions", []):
        number = action.get("number")
        if not isinstance(number, int):
            continue
        if number < 0 or number > MUGEN_MAX_VALID_ACTION:
            out_of_range.append(number)
            continue
        anim = _build_animation(action, sprite_index, directions_mode=directions_mode)
        if anim is None:
            skipped.append(number)
            continue

        # Always emit the lossless key first so aliases overwrite nothing.
        # Bei Kollisionen (gleiche AIR-Nummer mehrfach deklariert) Suffix
        # _b, _c, ... anhängen statt stillem Overwrite.
        base_key = f"action_{number}"
        key = base_key
        if key in out_animations:
            duplicates.append(number)
            suffix_ord = ord("b")
            while key in out_animations and suffix_ord <= ord("z"):
                key = f"{base_key}_{chr(suffix_ord)}"
                suffix_ord += 1
        out_animations[key] = anim

        alias = _ACTION_ALIASES.get(number)
        if alias and alias not in out_animations:
            # Same animation object — duplicating a reference is fine,
            # FLARE only reads, never mutates.
            out_animations[alias] = anim
        converted += 1

    if not out_animations:
        raise RuntimeError(f"No convertible actions found in {animations_path}")
    if "stance" not in out_animations:
        # Riftstorm code (movement, bootstrap) assumes "stance" exists. Warn loudly.
        print(
            f"[warn] {char_dir.name}: no MUGEN action 0 — 'stance' alias is missing.",
            file=sys.stderr,
        )

    flare_doc = {
        "image": "atlas.png",
        "animations": out_animations,
    }

    name = atlas_name or char_dir.name
    out_path = char_dir / f"{name}.json"
    with out_path.open("w", encoding="utf-8") as fh:
        json.dump(flare_doc, fh, indent=2)
    print(
        f"[ok] {char_dir.name}: {converted} actions converted, "
        f"{len(out_animations)} animation keys "
        f"({sum(1 for k in out_animations if not k.startswith('action_'))} aliases), "
        f"{len(skipped)} skipped, "
        f"{len(out_of_range)} out-of-range, "
        f"{len(duplicates)} duplicate-suffixed, "
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
