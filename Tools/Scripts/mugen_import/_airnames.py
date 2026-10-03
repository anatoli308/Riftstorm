"""Kommentar-Ueberschriften zu jeder [Begin Action] aus einer .air-Datei."""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mugen.air import _read_text  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    char = sys.argv[1]
    wanted = {int(a) for a in sys.argv[2:]} if len(sys.argv) > 2 else None
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mugen_data", char)

    for name in sorted(os.listdir(root)):
        if not name.lower().endswith(".air"):
            continue
        text = _read_text(os.path.join(root, name))
        pending: list[str] = []
        for raw in text.splitlines():
            line = raw.strip()
            if line.startswith(";"):
                comment = line.lstrip(";").strip()
                if comment and set(comment) - set("-=*+_ "):
                    pending.append(comment)
                continue
            low = line.lower()
            if low.startswith("[begin action"):
                number = int(line.split()[-1].rstrip("]"))
                if wanted is None or number in wanted:
                    head = " | ".join(pending[-3:]) if pending else ""
                    print(f"action_{number:<8} {head}")
                pending = []
            elif line:
                pending = []


if __name__ == "__main__":
    main()
