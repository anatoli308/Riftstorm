"""Zaehlt Statedefs und Effekt-Controller je MUGEN-Statefile (Diagnose)."""
import glob
import io
import os
import re
import sys

PATTERNS = {
    "explod": r"(?i)type\s*=\s*explod",
    "helper": r"(?i)type\s*=\s*helper",
    "changeanim": r"(?i)type\s*=\s*changeanim",
    "projectile": r"(?i)type\s*=\s*projectile",
}


def main(root: str) -> None:
    files = sorted(
        p for p in glob.glob(os.path.join(root, "**", "*"), recursive=True)
        if os.path.isfile(p) and os.path.splitext(p)[1].lower() in (".st", ".cns", ".txt")
    )
    for path in files:
        text = io.open(path, encoding="utf-8", errors="replace").read()
        defs = len(re.findall(r"(?im)^\s*\[\s*statedef", text))
        counts = {k: len(re.findall(v, text)) for k, v in PATTERNS.items()}
        rel = os.path.relpath(path, root)
        print(f"{rel:32} statedefs={defs:4}  " + " ".join(f"{k}={v}" for k, v in counts.items()))


if __name__ == "__main__":
    main(sys.argv[1])
