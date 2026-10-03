"""Zaehlt, wie viele bereits importierte Ueberschriften der aktuelle Filter verwirft."""
from __future__ import annotations

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.cns_states import _is_heading

_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    total = 0
    for char in sorted(os.listdir(_OUT)):
        path = os.path.join(_OUT, char, "states.json")
        if not os.path.isfile(path):
            continue
        with open(path, encoding="utf-8") as fh:
            states = json.load(fh).get("states", [])
        bad = [s for s in states if s.get("section") and not _is_heading(s["section"])]
        total += len(bad)
        if bad:
            print(f"{char:14s} {len(bad):3d} verwerfbar  "
                  f"z.B. {[s['section'][:32] for s in bad[:3]]}")
    print(f"\nGesamt verwerfbar: {total}")


if __name__ == "__main__":
    main()
