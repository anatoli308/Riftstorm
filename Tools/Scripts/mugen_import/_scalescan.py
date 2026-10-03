"""Zaehlt, wie MUGEN-Daten Skalierung benutzen: [Size] xscale und Explod scale."""
from __future__ import annotations

import collections
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mugen_data")
_SECTION = re.compile(r"^\[(.+?)\]")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def _lines(path: str):
    with open(path, "rb") as fh:
        for raw in fh.read().decode("latin-1", errors="ignore").splitlines():
            yield raw.split(";", 1)[0].strip()


def main() -> None:
    for char in sorted(os.listdir(_ROOT)):
        folder = os.path.join(_ROOT, char)
        if not os.path.isdir(folder):
            continue

        xscale = ""
        for path in glob.glob(os.path.join(folder, "**", "*.cns"), recursive=True):
            section = ""
            for line in _lines(path):
                head = _SECTION.match(line)
                if head:
                    section = head.group(1).strip().lower()
                    continue
                if section == "size" and line.lower().startswith("xscale"):
                    xscale = line.split("=", 1)[1].strip()

        kinds: collections.Counter[str] = collections.Counter()
        scales: collections.Counter[str] = collections.Counter()
        for pattern in ("*.cns", "*.st", "*.state", "*.txt"):
            for path in glob.glob(os.path.join(folder, "**", pattern), recursive=True):
                ctrl = ""
                for line in _lines(path):
                    low = line.lower()
                    if low.startswith("type"):
                        ctrl = low.split("=", 1)[1].strip()
                        if ctrl in ("explod", "modifyexplod", "projectile"):
                            kinds[ctrl] += 1
                        continue
                    if ctrl == "explod" and low.startswith("scale"):
                        scales[line.split("=", 1)[1].strip()] += 1
        print(f"{char:12s} size.xscale={xscale or '-':6s} "
              f"explods={kinds['explod']:4d} davon mit scale={sum(scales.values()):4d} "
              f"| haeufigste: {scales.most_common(3)}")


if __name__ == "__main__":
    main()
