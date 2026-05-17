"""MUGEN CNS constants parser.

The CNS file is an INI-like format with sections:
    [Data]      - life, power, attack, defence, sparkno, KO.echo, volume, ...
    [Size]      - xscale, yscale, ground/air.back/front, head/mid pos, ...
    [Velocity]  - walk/run/jump/airjump velocities (X,Y pairs)
    [Movement]  - airjump.num, airjump.height, yaccel, friction
    [Quotes]    - victory quotes (sparse: victory1..victory100)

This module ignores [Statedef *] and [State *] blocks — those are handled by
cns_states.py. We only extract the numeric character constants here.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

from .air import _read_text


@dataclass
class CnsConstants:
    """Flat normalized character constants.

    Stored as nested dicts mapping lowercased section -> key -> raw string.
    A convenience `to_typed()` returns a structured dict ready for JSON.
    """

    sections: dict[str, dict[str, str]] = field(default_factory=dict)

    def get(self, section: str, key: str, default: str = "") -> str:
        return self.sections.get(section.lower(), {}).get(key.lower(), default)

    def get_int(self, section: str, key: str, default: int = 0) -> int:
        raw = self.get(section, key)
        return _parse_int(raw, default)

    def get_float(self, section: str, key: str, default: float = 0.0) -> float:
        raw = self.get(section, key)
        return _parse_float(raw, default)

    def get_xy(self, section: str, key: str) -> tuple[float, float]:
        raw = self.get(section, key)
        if not raw:
            return (0.0, 0.0)
        parts = [p.strip() for p in raw.split(",")]
        if len(parts) < 2:
            return (_parse_float(parts[0], 0.0), 0.0)
        return (_parse_float(parts[0], 0.0), _parse_float(parts[1], 0.0))

    def to_typed(self) -> dict:
        """Return a JSON-friendly typed snapshot of the well-known fields."""
        d = self
        return {
            "data": {
                "life": d.get_int("data", "life"),
                "power": d.get_int("data", "power"),
                "attack": d.get_int("data", "attack"),
                "defence": d.get_int("data", "defence"),
                "fall_defence_up": d.get_int("data", "fall.defence_up"),
                "liedown_time": d.get_int("data", "liedown.time"),
                "airjuggle": d.get_int("data", "airjuggle"),
                "sparkno": d.get_int("data", "sparkno"),
                "guard_sparkno": d.get_int("data", "guard.sparkno"),
                "ko_echo": d.get_int("data", "ko.echo"),
                "volume": d.get_int("data", "volume"),
                "int_persist_index": d.get_int("data", "intpersistindex"),
                "float_persist_index": d.get_int("data", "floatpersistindex"),
            },
            "size": {
                "xscale": d.get_float("size", "xscale", 1.0),
                "yscale": d.get_float("size", "yscale", 1.0),
                "ground_back": d.get_int("size", "ground.back"),
                "ground_front": d.get_int("size", "ground.front"),
                "air_back": d.get_int("size", "air.back"),
                "air_front": d.get_int("size", "air.front"),
                "height": d.get_int("size", "height"),
                "attack_dist": d.get_int("size", "attack.dist"),
                "proj_attack_dist": d.get_int("size", "proj.attack.dist"),
                "proj_doscale": d.get_int("size", "proj.doscale"),
                "head_pos": list(d.get_xy("size", "head.pos")),
                "mid_pos": list(d.get_xy("size", "mid.pos")),
                "shadowoffset": d.get_int("size", "shadowoffset"),
                "draw_offset": list(d.get_xy("size", "draw.offset")),
            },
            "velocity": {
                "walk_fwd": d.get_float("velocity", "walk.fwd"),
                "walk_back": d.get_float("velocity", "walk.back"),
                "run_fwd": list(d.get_xy("velocity", "run.fwd")),
                "run_back": list(d.get_xy("velocity", "run.back")),
                "jump_neu": list(d.get_xy("velocity", "jump.neu")),
                "jump_back": d.get_float("velocity", "jump.back"),
                "jump_fwd": d.get_float("velocity", "jump.fwd"),
                "runjump_back": list(d.get_xy("velocity", "runjump.back")),
                "runjump_fwd": list(d.get_xy("velocity", "runjump.fwd")),
                "airjump_neu": list(d.get_xy("velocity", "airjump.neu")),
                "airjump_back": d.get_float("velocity", "airjump.back"),
                "airjump_fwd": d.get_float("velocity", "airjump.fwd"),
            },
            "movement": {
                "airjump_num": d.get_int("movement", "airjump.num"),
                "airjump_height": d.get_int("movement", "airjump.height"),
                "yaccel": d.get_float("movement", "yaccel"),
                "stand_friction": d.get_float("movement", "stand.friction"),
                "crouch_friction": d.get_float("movement", "crouch.friction"),
            },
            "quotes": _collect_quotes(d.sections.get("quotes", {})),
        }


_SECTION_RE = re.compile(r"^\s*\[([^\]]+)\]\s*$")
_KV_RE = re.compile(r"^\s*([^=;]+?)\s*=\s*(.*?)\s*$")


# Sections handled elsewhere (state machine).
_STATE_SECTION_PREFIXES = ("statedef", "state ")


def parse_cns(path: str) -> CnsConstants:
    """Parse the constants part of a CNS file (ignores state machine blocks)."""
    text = _read_text(path)
    sections: dict[str, dict[str, str]] = {}
    current: str = ""
    skip_current = False

    for raw_line in text.splitlines():
        line = raw_line.split(";", 1)[0].rstrip()
        if not line.strip():
            continue

        m_sec = _SECTION_RE.match(line)
        if m_sec:
            current = m_sec.group(1).strip().lower()
            skip_current = current.startswith(_STATE_SECTION_PREFIXES)
            if not skip_current:
                sections.setdefault(current, {})
            continue

        if skip_current or not current:
            continue

        m_kv = _KV_RE.match(line)
        if m_kv:
            key = m_kv.group(1).strip().lower()
            value = m_kv.group(2).strip().strip('"')
            sections[current][key] = value

    return CnsConstants(sections=sections)


def merge(*configs: CnsConstants) -> CnsConstants:
    """Merge multiple CNS configs. Later configs override earlier ones."""
    out: dict[str, dict[str, str]] = {}
    for cfg in configs:
        for sec, kv in cfg.sections.items():
            target = out.setdefault(sec, {})
            target.update(kv)
    return CnsConstants(sections=out)


def _collect_quotes(quotes: dict[str, str]) -> list[str]:
    items: list[tuple[int, str]] = []
    for key, val in quotes.items():
        m = re.match(r"victory(\d+)", key)
        if not m:
            continue
        items.append((int(m.group(1)), val))
    items.sort()
    return [v for _, v in items]


def _parse_int(s: str, default: int) -> int:
    if not s:
        return default
    try:
        return int(float(s.strip()))
    except ValueError:
        return default


def _parse_float(s: str, default: float) -> float:
    if not s:
        return default
    try:
        return float(s.strip())
    except ValueError:
        return default
