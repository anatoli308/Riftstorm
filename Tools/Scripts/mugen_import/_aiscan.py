"""Prueft, wie viele AfterImage-Controller die PalFX-Parameter explizit setzen."""
from __future__ import annotations

import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent / "mugen_data"
TEXT_SUFFIXES = {".cns", ".st", ".txt"}
PARAMS = ("paladd", "palmul", "palbright", "palcontrast",
          "palpostbright", "palcolor", "palinvertall", "trans")

_BLOCK = re.compile(r"^\s*\[state[^\]]*\](.*?)(?=^\s*\[|\Z)", re.MULTILINE | re.DOTALL | re.IGNORECASE)
_TYPE = re.compile(r"^\s*type\s*=\s*([a-z0-9_]+)", re.MULTILINE | re.IGNORECASE)


def main() -> None:
    total = 0
    have: Counter[str] = Counter()
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        if "backup" in str(path).lower():
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        for body in _BLOCK.findall(text):
            match = _TYPE.search(body)
            if not match or match.group(1).lower() != "afterimage":
                continue
            total += 1
            low = body.lower()
            for param in PARAMS:
                if re.search(rf"^\s*{param}\s*=", low, re.MULTILINE):
                    have[param] += 1

    print(f"AfterImage-Controller gesamt: {total}")
    for param in PARAMS:
        print(f"  {param:<14} gesetzt in {have[param]:>3}  -> Default in {total - have[param]:>3}")


if __name__ == "__main__":
    main()
