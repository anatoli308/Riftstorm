"""Zaehlt Controller, deren `[State N]`-Label nicht zum umschliessenden Statedef passt.

MUGEN ignoriert die Nummer im Controller-Label komplett (Ikemen prueft in
`compiler.go` nur das Schluesselwort). Autoren kopieren Bloecke aber samt alter
Nummer — jeder Treffer hier war vor der Parser-Korrektur ein Controller im
falschen State.

Aufruf: python _labelcheck.py
"""
from __future__ import annotations

import re
from pathlib import Path

DATA = Path(__file__).resolve().parent / "mugen_data"
STATEDEF_RE = re.compile(r"^\s*\[\s*statedef\s+(-?\d+)", re.IGNORECASE)
STATE_RE = re.compile(r"^\s*\[\s*state\s+(-?\d+)", re.IGNORECASE)


def main() -> None:
    total = 0
    for char_dir in sorted(p for p in DATA.iterdir() if p.is_dir()):
        misplaced = 0
        blocks = 0
        for path in char_dir.rglob("*"):
            if path.suffix.lower() not in (".cns", ".st", ".txt") or "Backup" in path.parts:
                continue
            current: int | None = None
            for raw in path.read_text(encoding="utf-8", errors="ignore").splitlines():
                line = raw.split(";", 1)[0]
                m_def = STATEDEF_RE.match(line)
                if m_def is not None:
                    current = int(m_def.group(1))
                    continue
                m_state = STATE_RE.match(line)
                if m_state is None or current is None:
                    continue
                blocks += 1
                if int(m_state.group(1)) != current:
                    misplaced += 1
        if misplaced:
            print(f"{char_dir.name:16} {misplaced:5} von {blocks:5} Controllern falsch beschriftet")
        total += misplaced
    print(f"\nGESAMT falsch beschriftet: {total}")


if __name__ == "__main__":
    main()
