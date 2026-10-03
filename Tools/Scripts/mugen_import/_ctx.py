"""Zeigt die Kommentarzeilen direkt ueber ausgewaehlten Statedefs."""
from __future__ import annotations

import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mugen.air import _read_text

_DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mugen_data")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    char = sys.argv[1]
    wanted = {f"[statedef {n}]" for n in sys.argv[2:]}
    for path in glob.glob(os.path.join(_DATA, char, "**", "*.*"), recursive=True):
        if not path.lower().endswith((".cns", ".st", ".txt")):
            continue
        lines = _read_text(path).splitlines()
        for i, line in enumerate(lines):
            if line.strip().lower().replace(" ,", ",").split(",")[0] + "]" not in wanted \
                    and line.strip().lower() not in wanted:
                continue
            print(f"\n--- {os.path.basename(path)}:{i + 1}  {line.strip()}")
            for prev in lines[max(0, i - 7):i]:
                print(f"    {prev.strip()!r}")


if __name__ == "__main__":
    main()
