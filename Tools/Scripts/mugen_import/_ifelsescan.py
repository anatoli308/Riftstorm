"""Listet die Bedingungen aller ifelse-Ausdruecke in ChangeAnim-Werten."""
from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"


def main() -> None:
    conds: Counter[str] = Counter()
    for char_dir in sorted(OUT.iterdir()):
        states_file = char_dir / "states.json"
        if not states_file.exists():
            continue
        for state in json.loads(states_file.read_text("utf-8"))["states"]:
            for ctrl in state.get("controllers") or []:
                params = ctrl.get("params") or {}
                if (params.get("type") or "").lower() != "changeanim":
                    continue
                value = str(params.get("value") or "")
                for m in re.finditer(r"ifelse\s*\(([^,]*)", value, re.IGNORECASE):
                    conds[m.group(1).strip().lower()] += 1
    for cond, count in conds.most_common():
        print(f"{count:4d}  {cond}")


if __name__ == "__main__":
    main()
