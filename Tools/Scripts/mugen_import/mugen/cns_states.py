"""MUGEN state machine parser (CNS / ST / common state files).

Parses `[Statedef N]` blocks and their nested `[State N, label]` state
controllers. Each controller has free-form `key=value` parameters such as
type, trigger1..N, triggerall, value, time, persistent, ignorehitpause, etc.

We retain ALL key/value pairs verbatim so the Unity side can interpret any
controller type without us having to enumerate every one.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

from .air import _read_text


@dataclass
class StateController:
    """One `[State N, label]` block. All params kept as raw strings."""

    label: str = ""
    parent_state: int = 0
    params: dict[str, str] = field(default_factory=dict)
    triggers: dict[str, list[str]] = field(default_factory=dict)
    # triggers: {"triggerall": [...], "trigger1": [...], "trigger2": [...], ...}


@dataclass
class StateDef:
    """One `[Statedef N]` block plus its child controllers."""

    number: int
    label: str = ""
    header: dict[str, str] = field(default_factory=dict)
    controllers: list[StateController] = field(default_factory=list)


_STATEDEF_RE = re.compile(
    r"^\s*\[\s*Statedef\s+(-?\d+)\s*(?:,\s*([^\]]*?))?\s*\]\s*$", re.IGNORECASE
)
_STATE_RE = re.compile(
    r"^\s*\[\s*State\s+(-?\d+)\s*(?:,\s*([^\]]*?))?\s*\]\s*$", re.IGNORECASE
)
_KV_RE = re.compile(r"^\s*([^=;]+?)\s*=\s*(.*?)\s*$")
_TRIGGER_KEY_RE = re.compile(r"^(triggerall|trigger\d+)$", re.IGNORECASE)


def parse_states(path: str) -> list[StateDef]:
    """Parse all Statedef + State blocks in a CNS/ST/CMD file."""
    text = _read_text(path)
    states: list[StateDef] = []
    current_def: StateDef | None = None
    current_ctrl: StateController | None = None

    for raw_line in text.splitlines():
        line = raw_line.split(";", 1)[0].rstrip()
        if not line.strip():
            continue

        m_def = _STATEDEF_RE.match(line)
        if m_def is not None:
            current_def = StateDef(
                number=int(m_def.group(1)),
                label=(m_def.group(2) or "").strip(),
            )
            states.append(current_def)
            current_ctrl = None
            continue

        m_state = _STATE_RE.match(line)
        if m_state is not None:
            number = int(m_state.group(1))
            label = (m_state.group(2) or "").strip()
            # `[State N, label]` blocks attach to the most recent statedef.
            # If none exists yet (e.g. CMD's bare [State -1] outside Statedef),
            # implicitly create a Statedef bucket for that number.
            if current_def is None or current_def.number != number:
                # find or create matching statedef bucket
                bucket = next((s for s in states if s.number == number), None)
                if bucket is None:
                    bucket = StateDef(number=number)
                    states.append(bucket)
                current_def = bucket
            current_ctrl = StateController(
                label=label, parent_state=number,
            )
            current_def.controllers.append(current_ctrl)
            continue

        m_kv = _KV_RE.match(line)
        if m_kv is None:
            continue
        key = m_kv.group(1).strip().lower()
        value = m_kv.group(2).strip().strip('"')

        if current_ctrl is not None:
            if _TRIGGER_KEY_RE.match(key):
                current_ctrl.triggers.setdefault(key, []).append(value)
            else:
                current_ctrl.params[key] = value
        elif current_def is not None:
            current_def.header[key] = value

    return states


def merge_states(*state_lists: list[StateDef]) -> list[StateDef]:
    """Merge multiple parsed state-file results.

    Statedefs with the same number from later files override the earlier
    header but APPEND controllers (matches MUGEN behavior for split state files).
    """
    by_number: dict[int, StateDef] = {}
    order: list[int] = []
    for states in state_lists:
        for sd in states:
            existing = by_number.get(sd.number)
            if existing is None:
                by_number[sd.number] = StateDef(
                    number=sd.number,
                    label=sd.label,
                    header=dict(sd.header),
                    controllers=list(sd.controllers),
                )
                order.append(sd.number)
                continue
            if sd.label:
                existing.label = sd.label
            existing.header.update(sd.header)
            existing.controllers.extend(sd.controllers)
    return [by_number[n] for n in order]


def to_jsonable(states: list[StateDef]) -> list[dict]:
    """Convert to JSON-serializable structures."""
    out: list[dict] = []
    for sd in states:
        out.append({
            "number": sd.number,
            "label": sd.label,
            "header": sd.header,
            "controllers": [
                {
                    "label": c.label,
                    "params": c.params,
                    "triggers": c.triggers,
                }
                for c in sd.controllers
            ],
        })
    return out
