"""Sucht in jedem Charakterordner die StCommon-Datei — unabhaengig vom Dateinamen.

MUGEN laedt die gemeinsamen System-States (Laufen, Springen, Trefferreaktionen,
Aufstehen) aus einer Datei, die im ``.def`` unter ``[Files] stcommon`` steht. Viele
Charaktere benennen sie um oder legen sie in einen Unterordner, sodass der Name
nicht mehr passt. Erkannt wird sie deshalb am Inhalt: an den Statedef-Nummern,
die nur in dieser Datei vorkommen.
"""
from __future__ import annotations

import os
import re
from typing import Optional

_STATE_EXTS = (".st", ".cns", ".txt")
_STATEDEF_RE = re.compile(r"(?im)^\s*\[\s*statedef\s+(-?\d+)")

# Statedefs, die praktisch nur in einer StCommon-Datei zusammen auftreten:
# Bewegung/Sprung (40-155), Aufstehen (170-191) und die komplette Trefferband 5000+.
_MARKERS = frozenset({
    0, 20, 40, 50, 51, 52, 105, 120, 130, 140, 150, 151, 152, 170, 175, 190, 191,
    5000, 5001, 5010, 5020, 5030, 5040, 5050, 5070, 5080, 5100, 5110, 5120,
    5150, 5200, 5210, 5500,
})

# Ab so vielen Treffern gilt eine Datei als StCommon. Ein normaler Move-Satz
# erreicht das nicht, weil ihm die 5000er-Trefferreaktionen fehlen.
_MIN_MARKERS = 12

# Ohne diese beiden ist es keine vollstaendige StCommon.
_REQUIRED = frozenset({5000, 5100})


def statedef_numbers(path: str) -> set[int]:
    """Alle Statedef-Nummern einer Datei. Tolerant gegen Shift-JIS-Kommentare."""
    try:
        with open(path, "rb") as fh:
            text = fh.read().decode("latin-1", errors="ignore")
    except OSError:
        return set()
    return {int(m) for m in _STATEDEF_RE.findall(text)}


def common_state_score(path: str) -> int:
    """Wie sehr sieht die Datei nach StCommon aus? 0 = gar nicht."""
    numbers = statedef_numbers(path)
    if not _REQUIRED <= numbers:
        return 0
    hits = len(_MARKERS & numbers)
    return hits if hits >= _MIN_MARKERS else 0


def find_common_state(folder: str, exclude: Optional[set[str]] = None) -> Optional[str]:
    """Beste StCommon-Kandidatin unterhalb von ``folder``, sonst ``None``."""
    skip = {os.path.normcase(p) for p in (exclude or set())}
    best: Optional[str] = None
    best_score = 0
    for root, _dirs, names in os.walk(folder):
        for name in sorted(names):
            if not name.lower().endswith(_STATE_EXTS):
                continue
            path = os.path.join(root, name)
            if os.path.normcase(path) in skip:
                continue
            score = common_state_score(path)
            if score > best_score:
                best, best_score = path, score
    return best
