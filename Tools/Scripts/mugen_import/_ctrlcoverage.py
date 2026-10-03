"""Welche State-Controller kommen vor und welche uebersetzt der Konverter?

Beantwortet die Frage "was fehlt noch" mit Zahlen statt Bauchgefuehl.
"""
from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "out"

# Alles, was mugen_to_flare.py als Typ-String vergleicht, gilt als uebersetzt.
CONVERTER = (HERE / "mugen_to_flare.py").read_text("utf-8")
HANDLED = {m.lower() for m in re.findall(r'["\'](\w+)["\']', CONVERTER)}


def main() -> None:
    counts: Counter[str] = Counter()
    for states_file in sorted(OUT.glob("*/states.json")):
        doc = json.loads(states_file.read_text("utf-8"))
        for state in doc.get("states") or []:
            for ctrl in state.get("controllers") or []:
                ctype = ((ctrl.get("params") or {}).get("type") or "").strip().lower()
                if ctype:
                    counts[ctype] += 1

    missing = [(n, c) for c, n in counts.items() if c not in HANDLED]
    handled = sum(n for c, n in counts.items() if c in HANDLED)
    print(f"Controller gesamt {sum(counts.values())}  uebersetzt {handled}  "
          f"ignoriert {sum(n for n, _ in missing)}")
    print("\nHaeufigste ignorierte Controller:")
    for count, ctype in sorted(missing, reverse=True)[:30]:
        print(f"  {count:6d}  {ctype}")


if __name__ == "__main__":
    main()
