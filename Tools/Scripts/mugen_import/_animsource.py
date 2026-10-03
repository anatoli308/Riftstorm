"""Sucht, welcher State eine bestimmte Animationsnummer verwendet.

Beantwortet die Frage, ob eine im Atlas vorhandene, aber nirgends eingeplante
Animation ueberhaupt eine Quelle in den CNS-Daten hat — oder ob sie dort
schlicht nicht vorkommt und damit auch in MUGEN nie zu sehen waere.

Aufruf: python _animsource.py <Charakter> <nummer> [<nummer> ...]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"

ANIM_PARAMS = ("anim", "value", "projanim", "projhitanim", "projremanim")


def main() -> None:
    name = sys.argv[1]
    wanted = {int(n) for n in sys.argv[2:]}
    states = json.loads((OUT / name / "states.json").read_text(encoding="utf-8"))["states"]

    for state in states:
        state_id = state.get("number")
        for ctrl in state.get("controllers", []):
            params = ctrl.get("params", {})
            ctype = str(params.get("type", "")).lower()
            for key in ANIM_PARAMS:
                raw = params.get(key)
                if raw is None:
                    continue
                text = str(raw).strip().strip('"')
                if not text.lstrip("-").isdigit():
                    continue
                if int(text) not in wanted:
                    continue
                if key == "value" and ctype not in ("changeanim", "changeanim2"):
                    continue
                print(f"anim {text:<8} state {str(state_id):<10} ctrl={ctype:<12} param={key} "
                      f"label={ctrl.get('label', '')}")


if __name__ == "__main__":
    main()
