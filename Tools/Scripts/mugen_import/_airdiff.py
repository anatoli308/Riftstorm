"""Vergleicht die Actions der .air-Datei mit den exportierten Animationen."""
from __future__ import annotations

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mugen.air import _read_text  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_BASE = os.path.dirname(os.path.abspath(__file__))


def _air_actions(char: str) -> list[int]:
    root = os.path.join(_BASE, "mugen_data", char)
    numbers: list[int] = []
    for folder, _dirs, files in os.walk(root):
        for name in files:
            if not name.lower().endswith(".air"):
                continue
            for line in _read_text(os.path.join(folder, name)).splitlines():
                stripped = line.strip().lower()
                if stripped.startswith("[begin action"):
                    numbers.append(int(line.strip().split()[-1].rstrip("]")))
    return numbers


def main() -> None:
    char = sys.argv[1]
    with open(os.path.join(_BASE, "out", char, f"{char}.json"), encoding="utf-8") as fh:
        exported = set(json.load(fh)["animations"])

    air = _air_actions(char)
    missing = [n for n in sorted(set(air)) if f"action_{n}" not in exported]
    print(f"{char}: {len(set(air))} Actions in .air, {len(exported)} exportiert, "
          f"{len(missing)} fehlen komplett")
    for number in missing:
        print(f"  action_{number}")


if __name__ == "__main__":
    main()
