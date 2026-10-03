"""Voller Zustandsbericht eines Charakters: States, Ketten, Effekte, Sounds, Treffer."""
from __future__ import annotations

import collections
import json
import os
import sys

_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    char = sys.argv[1]
    limit = int(sys.argv[2]) if len(sys.argv) > 2 else 40

    with open(os.path.join(_OUT, char, f"{char}.json"), encoding="utf-8") as fh:
        flare = json.load(fh)
    with open(os.path.join(_OUT, char, "states.json"), encoding="utf-8") as fh:
        raw = json.load(fh)

    states = raw["states"]
    labels = {s["number"]: (s.get("label") or s.get("section") or "").strip() for s in states}
    sequences = sorted(flare["sequences"], key=lambda s: s["state"])

    print(f"Quellen: {raw.get('files')}")
    print(f"{len(flare['animations'])} Animationen   {len(states)} Statedefs   "
          f"{len(sequences)} Moves")

    totals = collections.Counter()
    for seq in sequences:
        totals["Anims"] += len(seq.get("anims", []))
        totals["Effekte"] += len(seq.get("effects", []))
        totals["Sounds"] += len(seq.get("sounds", []))
        totals["Helfer"] += len(seq.get("helpers", []))
        totals["Treffer"] += len(seq.get("hits", []))
        totals["PalFX"] += len(seq.get("palfx", []))
        totals["Pausen"] += len(seq.get("pauses", []))
        totals["Bewegung"] += len(seq.get("moves", []))
    print(f"Summen: {dict(totals)}")
    print(f"Ketten mit mehr als einer Animation: "
          f"{sum(1 for s in sequences if len(s.get('anims', [])) > 1)}")

    print(f"\n{'State':>7}  {'Kategorie':<9} {'A':>2} {'FX':>3} {'Snd':>3} {'Hlp':>3} "
          f"{'Hit':>3}  Name")
    for seq in sequences[:limit]:
        print(f"{seq['state']:>7}  {seq['category']:<9} "
              f"{len(seq.get('anims', [])):>2} {len(seq.get('effects', [])):>3} "
              f"{len(seq.get('sounds', [])):>3} {len(seq.get('helpers', [])):>3} "
              f"{len(seq.get('hits', [])):>3}  "
              f"{labels.get(seq['state']) or seq.get('label') or ''}")
    if len(sequences) > limit:
        print(f"… und {len(sequences) - limit} weitere")


if __name__ == "__main__":
    main()
