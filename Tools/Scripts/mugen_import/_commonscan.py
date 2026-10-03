"""Zeigt je Charakter alle State-Dateien und welche davon nach StCommon aussieht."""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.common_state import _MARKERS, _STATE_EXTS, common_state_score, statedef_numbers

_DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mugen_data")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    only = {a.lower() for a in sys.argv[1:]}
    for char in sorted(os.listdir(_DATA)):
        folder = os.path.join(_DATA, char)
        if not os.path.isdir(folder) or (only and char.lower() not in only):
            continue

        rows: list[tuple[int, int, str]] = []
        for root, _dirs, names in os.walk(folder):
            for name in sorted(names):
                if not name.lower().endswith(_STATE_EXTS):
                    continue
                path = os.path.join(root, name)
                numbers = statedef_numbers(path)
                if not numbers:
                    continue
                rows.append((
                    common_state_score(path),
                    len(numbers),
                    os.path.relpath(path, folder),
                ))

        rows.sort(key=lambda r: (-r[0], -r[1]))
        best = rows[0] if rows and rows[0][0] > 0 else None
        print(f"\n===== {char} =====")
        print(f"  StCommon: {best[2] if best else 'KEINE GEFUNDEN'}"
              f"{f'  ({best[0]}/{len(_MARKERS)} Marker, {best[1]} Statedefs)' if best else ''}")
        for score, count, rel in rows[:8]:
            flag = "*" if score > 0 else " "
            print(f"   {flag} {score:2d} Marker  {count:4d} Statedefs  {rel}")


if __name__ == "__main__":
    main()
