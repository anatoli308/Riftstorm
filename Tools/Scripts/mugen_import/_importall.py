"""Importiert alle Charaktere aus mugen_data und baut die FLARE-Dokumente.

Pro Charakter optional eine explizite .def-Variante, wenn die alphabetisch
erste nicht die inhaltsreichste ist.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

from mugen_deploy import deploy_character

_HERE = os.path.dirname(os.path.abspath(__file__))

# Char-Namen/Autoren sind haeufig japanisch; cp1252-stdout wuerde den Lauf abbrechen.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

# Chars deren alphabetisch erste .def NICHT die gewuenschte Variante ist.
DEF_OVERRIDE = {
    "Seren": "Seren_Lewd_Ikemen_Full.def",
}


def run(cmd: list[str]) -> tuple[int, str]:
    proc = subprocess.run(cmd, cwd=_HERE, capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return proc.returncode, proc.stdout + proc.stderr


def deploy(char: str) -> tuple[int, int]:
    """Verteilt Runtime-Dateien mit demselben Deploy-Helper wie die Einzel-CLI."""
    return deploy_character(Path(_HERE) / "out" / char / f"{char}.json")


def _only_filter(argv: list[str]) -> set[str]:
    """Liest ``--only Name[,Name...]`` und liefert die Namen in Kleinschreibung."""
    names: set[str] = set()
    for i, arg in enumerate(argv):
        if arg == "--only" and i + 1 < len(argv):
            names.update(n.strip().lower() for n in argv[i + 1].split(",") if n.strip())
        elif arg.startswith("--only="):
            names.update(n.strip().lower() for n in arg[len("--only="):].split(",") if n.strip())
    return names


def main() -> int:
    # Der SFF/Atlas-Schritt dominiert die Laufzeit und aendert sich selten;
    # --flare-only konvertiert nur neu aus dem bereits importierten out/.
    flare_only = "--flare-only" in sys.argv
    only = _only_filter(sys.argv)
    data_root = os.path.join(_HERE, "mugen_data")
    chars = sorted(d for d in os.listdir(data_root)
                   if os.path.isdir(os.path.join(data_root, d))
                   and (not only or d.lower() in only))
    failures: list[str] = []

    for char in chars:
        print(f"\n########## {char} ##########", flush=True)
        if not flare_only:
            cmd = [sys.executable, "mugen_import.py", os.path.join("mugen_data", char),
                   "--out", os.path.join("out", char), "--no-individual-pngs"]
            if char in DEF_OVERRIDE:
                cmd += ["--def-file", DEF_OVERRIDE[char]]
            rc, out = run(cmd)
            for line in out.splitlines():
                if any(k in line for k in ("WARNING", "NOTE", "ERROR", "states:",
                                           "actions:", "Reading", "commands:",
                                           "StCommon")):
                    print("  " + line.strip(), flush=True)
            if rc != 0:
                failures.append(f"{char} (import rc={rc})")
                print(f"  !! import failed:\n{out[-1500:]}", flush=True)
                continue
        elif not os.path.isdir(os.path.join(_HERE, "out", char)):
            continue

        rc, out = run([
            sys.executable, "mugen_to_flare.py", os.path.join("out", char), "--no-deploy",
        ])
        for line in out.splitlines():
            if any(k in line for k in ("warn", "ERROR", "sequences", "effects", "skills")):
                print("  " + line.strip(), flush=True)
        if rc != 0:
            failures.append(f"{char} (flare rc={rc})")
            print(f"  !! flare failed:\n{out[-1500:]}", flush=True)
            continue

        try:
            streamed, arted = deploy(char)
        except (OSError, ValueError) as error:
            failures.append(f"{char} (deploy)")
            print(f"  !! deploy failed: {error}", flush=True)
            continue
        print(f"  [deploy] {streamed} -> StreamingAssets/Custom_Characters/{char}, "
              f"{arted} -> Art/Custom_Characters/{char}", flush=True)

    print("\n========== SUMMARY ==========")
    print(f"chars: {len(chars)}, failures: {len(failures)}")
    for f in failures:
        print(f"  FAILED: {f}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
