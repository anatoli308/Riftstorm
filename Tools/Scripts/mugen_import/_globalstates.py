"""Zaehlt die sichtbaren Controller in den globalen Statedefs -1/-2/-3.

MUGEN fuehrt diese Bloecke in JEDEM Frame aus, unabhaengig vom aktuellen State.
Dort liegen Dauereffekte wie Auren, Partikel und Spuren. Werden sie beim Import
uebersprungen, tauchen die zugehoerigen Animationen nirgends auf.
"""
from __future__ import annotations

import json
from collections import Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"
VISIBLE = ("explod", "projectile", "helper", "afterimage", "palfx", "allpalfx",
           "playsnd", "envshake", "modifyexplod", "removeexplod")


def main() -> None:
    for char in sorted(p.name for p in OUT.iterdir() if p.is_dir()):
        path = OUT / char / "states.json"
        if not path.exists():
            continue
        states = json.loads(path.read_text(encoding="utf-8"))["states"]
        tally: Counter[str] = Counter()
        for state in states:
            if not isinstance(state.get("number"), int) or state["number"] >= 0:
                continue
            for ctrl in state.get("controllers", []):
                ctype = str((ctrl.get("params") or {}).get("type", "")).lower()
                if ctype in VISIBLE:
                    tally[ctype] += 1
        if tally:
            print(f"{char:<16} " + "  ".join(f"{k}={v}" for k, v in sorted(tally.items())))


if __name__ == "__main__":
    main()
