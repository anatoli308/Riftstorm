"""Audit: was referenziert die .def eines Chars, was liegt ungenutzt herum.

Prueft je Charakter-Ordner alle .def-Varianten, zeigt fehlende Referenzen und
State-behaftete Dateien, die keine .def laedt.
"""
from __future__ import annotations

import os
import re
import sys

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
if _THIS_DIR not in sys.path:
    sys.path.insert(0, _THIS_DIR)

from mugen.air import _read_text
from mugen.def_parser import parse_def

STATE_EXTS = (".st", ".cns", ".txt")
STATEDEF_RE = re.compile(r"(?im)^\s*\[\s*statedef")


def statedef_count(path: str) -> int:
    try:
        return len(STATEDEF_RE.findall(_read_text(path)))
    except Exception:
        return 0


def audit(char_folder: str) -> None:
    name = os.path.basename(char_folder.rstrip("\\/"))
    defs = sorted(n for n in os.listdir(char_folder) if n.lower().endswith(".def"))
    print(f"\n=== {name} ===")
    print(f"  .def files: {len(defs)} -> {', '.join(defs)}")

    all_referenced: set[str] = set()
    for d in defs:
        cd = parse_def(os.path.join(char_folder, d))
        refs = [cd.sprite_file, cd.anim_file, cd.cmd_file, cd.snd_file, cd.cns_file]
        refs += cd.state_files + [cd.common_state_file] + list(cd.palettes.values())
        total = 0
        missing = []
        for rel in refs:
            if not rel:
                continue
            p = os.path.join(char_folder, rel)
            if os.path.isfile(p):
                all_referenced.add(os.path.normcase(os.path.abspath(p)))
                if os.path.splitext(p)[1].lower() in STATE_EXTS:
                    total += statedef_count(p)
            else:
                missing.append(rel)
        mark = " <-- auto-picked" if d == defs[0] else ""
        print(f"    {d:34} statedefs={total:4}{mark}")
        if missing:
            print(f"      MISSING: {', '.join(missing)}")

    stray = []
    for root, _dirs, names in os.walk(char_folder):
        for n in names:
            if not n.lower().endswith(STATE_EXTS):
                continue
            p = os.path.join(root, n)
            if os.path.normcase(os.path.abspath(p)) in all_referenced:
                continue
            c = statedef_count(p)
            if c:
                stray.append(f"{os.path.relpath(p, char_folder)} ({c})")
    if stray:
        print(f"  unreferenced by ANY .def: {len(stray)}")
        for s in stray:
            print(f"    {s}")


if __name__ == "__main__":
    root = sys.argv[1]
    for entry in sorted(os.listdir(root)):
        folder = os.path.join(root, entry)
        if os.path.isdir(folder):
            audit(folder)
