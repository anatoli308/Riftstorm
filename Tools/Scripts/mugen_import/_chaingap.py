"""Zaehlt ChangeState-Ziele, die die Body-Kette verwirft.

Zwei Ursachen werden getrennt ausgewiesen:
  header-anim fehlt  -> das Ziel holt seine Animation erst per ChangeAnim
  trigger zeitlos    -> Haltezustand (Eingabe noetig), kein Kettenglied
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"
_INT_RE = re.compile(r"-?\d+")
_ENTRY_RE = re.compile(r"^\s*(1|time\s*(=|<=)\s*0)\s*$", re.IGNORECASE)


def entry_anim(state: dict) -> tuple[int | None, str]:
    """Animation beim Betreten: Header, sonst erster ChangeAnim am Eintritt."""
    raw = (state.get("header") or {}).get("anim")
    if raw is not None and _INT_RE.fullmatch(str(raw).strip()):
        return int(str(raw).strip()), "header"
    for ctrl in state.get("controllers") or []:
        params = ctrl.get("params") or {}
        if (params.get("type") or "").lower() != "changeanim":
            continue
        lines = [ln for group in (ctrl.get("triggers") or {}).values() for ln in group]
        if lines and not all(_ENTRY_RE.match(ln) for ln in lines):
            continue
        found = _INT_RE.search(str(params.get("value") or ""))
        if found is not None:
            return int(found.group()), "changeanim"
    return None, "keine"


def main(names: list[str]) -> None:
    total = Counter()
    for char_dir in sorted(OUT.iterdir()):
        if not char_dir.is_dir() or (names and char_dir.name not in names):
            continue
        states_file = char_dir / "states.json"
        anims_file = char_dir / "animations.json"
        if not states_file.exists() or not anims_file.exists():
            continue
        states = json.loads(states_file.read_text("utf-8"))["states"]
        actions = json.loads(anims_file.read_text("utf-8"))["actions"]
        known = {a["number"] for a in actions}
        by_number = {s["number"]: s for s in states}

        per = Counter()
        for state in states:
            for ctrl in state.get("controllers") or []:
                params = ctrl.get("params") or {}
                if (params.get("type") or "").lower() not in ("changestate", "selfstate"):
                    continue
                found = _INT_RE.search(str(params.get("value") or ""))
                target = by_number.get(int(found.group())) if found else None
                if target is None:
                    continue
                number, source = entry_anim(target)
                if number is None or number not in known:
                    per["ziel ohne anim"] += 1
                elif source == "changeanim":
                    per["NUR per changeanim"] += 1
                else:
                    per["ok (header)"] += 1
        print(f"{char_dir.name:16s} " + "  ".join(f"{k}={v}" for k, v in sorted(per.items())))
        total.update(per)
    print("\nGESAMT: " + "  ".join(f"{k}={v}" for k, v in sorted(total.items())))


if __name__ == "__main__":
    main(sys.argv[1:])
