"""Ungenutzte Atlas-Animationen mit ihrer .air-Ueberschrift.

Zeigt, welche Actions zwar im Atlas liegen, aber von keiner Sequenz aufgerufen
werden - genau die Posen, die im Move-Modus des Showcase sonst fehlen.
"""
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


def _air_headings(char: str) -> dict[int, str]:
    root = os.path.join(_BASE, "mugen_data", char)
    headings: dict[int, str] = {}
    for folder, _dirs, files in os.walk(root):
        for name in sorted(files):
            if not name.lower().endswith(".air"):
                continue
            pending: list[str] = []
            for raw in _read_text(os.path.join(folder, name)).splitlines():
                line = raw.strip()
                if line.startswith(";"):
                    comment = line.lstrip(";").strip()
                    if comment and set(comment) - set("-=*+_ "):
                        pending.append(comment)
                    continue
                if line.lower().startswith("[begin action"):
                    number = int(line.split()[-1].rstrip("]"))
                    headings.setdefault(number, " | ".join(pending[-2:]))
                    pending = []
                elif line:
                    pending = []
    return headings


def _used_keys(sequences: list[dict]) -> set[str]:
    used: set[str] = set()
    for seq in sequences:
        for group in ("anims", "victim"):
            for entry in seq.get(group, []):
                used.add(entry["anim"])
        for fx in seq.get("effects", []):
            used.add(fx["anim"])
        for hit in seq.get("hits", []):
            if hit.get("spark"):
                used.add(hit["spark"])
        for helper in seq.get("helpers", []):
            for entry in helper.get("anims", []):
                used.add(entry["anim"])
            for fx in helper.get("effects", []):
                used.add(fx["anim"])
    return used


def _number(key: str) -> int:
    parts = key.rsplit("_", 1)
    return int(parts[1]) if len(parts) > 1 and parts[1].lstrip("-").isdigit() else 0


def main() -> None:
    char = sys.argv[1]
    with open(os.path.join(_BASE, "out", char, f"{char}.json"), encoding="utf-8") as fh:
        flare = json.load(fh)

    animations = flare["animations"]
    used = _used_keys(flare["sequences"])
    headings = _air_headings(char)
    unused = sorted((k for k in animations if k not in used), key=_number)

    print(f"{char}: {len(animations)} Animationen, {len(used)} benutzt, "
          f"{len(unused)} von keinem Move angesprochen")
    for key in unused:
        anim = animations[key]
        label = anim.get("label") or headings.get(_number(key), "")
        print(f"  {key:<18} {anim['frames_count']:>3}F {anim['duration_ms']:>6}ms  {label}")


if __name__ == "__main__":
    main()
