"""Roher Textsucher fuer MUGEN-Dateien mit gemischten Encodings."""
from __future__ import annotations

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mugen.air import _read_text  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_EXT = (".air", ".cns", ".st", ".def", ".txt")


def main() -> None:
    pattern = re.compile(sys.argv[1], re.IGNORECASE)
    roots = sys.argv[2:] or ["mugen_data"]
    base = os.path.dirname(os.path.abspath(__file__))

    for root in roots:
        start = os.path.join(base, "mugen_data", root) if root != "mugen_data" else os.path.join(base, root)
        for folder, _dirs, files in os.walk(start):
            for name in files:
                if not name.lower().endswith(_EXT):
                    continue
                path = os.path.join(folder, name)
                try:
                    lines = _read_text(path).splitlines()
                except OSError:
                    continue
                rel = os.path.relpath(path, start)
                for index, line in enumerate(lines, 1):
                    if pattern.search(line):
                        print(f"{rel}:{index}: {line.strip()}")


if __name__ == "__main__":
    main()
