"""Wie oft aendert StateTypeSet die Physik mitten im State?

Relevanz-Check fuer die neu eingebaute Reibung/Schwerkraft.
"""
from __future__ import annotations

import json
from collections import Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"


def main() -> None:
    changes: Counter[str] = Counter()
    affected = 0
    examples: list[str] = []
    for states_file in sorted(OUT.glob("*/states.json")):
        doc = json.loads(states_file.read_text("utf-8"))
        for state in doc.get("states") or []:
            header_phys = str((state.get("header") or {}).get("physics") or "").strip().upper()
            hit = False
            for ctrl in state.get("controllers") or []:
                params = ctrl.get("params") or {}
                if (params.get("type") or "").strip().lower() != "statetypeset":
                    continue
                phys = str(params.get("physics") or "").strip().upper()
                if not phys:
                    continue
                changes[f"{header_phys or '-'} -> {phys}"] += 1
                if phys != header_phys:
                    hit = True
            if hit:
                affected += 1
                if len(examples) < 8:
                    examples.append(f"{states_file.parent.name} Statedef {state.get('number')}")
    print(f"States mit echter Physik-Umschaltung: {affected}")
    for combo, count in changes.most_common():
        print(f"  {count:5d}  {combo}")
    print("\nBeispiele: " + ", ".join(examples))


if __name__ == "__main__":
    main()
