"""Zaehlt PalFX-Parameter in den CNS-Rohdaten, um Shader-Luecken zu bewerten."""
from __future__ import annotations

import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent / "mugen_data"
TEXT_SUFFIXES = {".cns", ".st", ".txt", ".air", ".cmd"}
PARAMS = ("hue", "invertall", "color", "add", "mul", "sinadd", "sinmul", "sincolor")


def main() -> None:
    counts: Counter[str] = Counter()
    per_char: dict[str, Counter[str]] = {}
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        if "backup" in str(path).lower():
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="ignore").lower()
        except OSError:
            continue
        char = path.relative_to(ROOT).parts[0]
        bucket = per_char.setdefault(char, Counter())
        for param in PARAMS:
            hits = len(re.findall(rf"^\s*{param}\s*=", text, re.MULTILINE))
            if hits:
                counts[param] += hits
                bucket[param] += hits

    print("PalFX-nahe Parameter in allen CNS/ST-Dateien:")
    for param in PARAMS:
        print(f"  {param:<10} {counts[param]}")
    print()
    print("hue je Charakter:")
    for char, bucket in sorted(per_char.items()):
        if bucket.get("hue"):
            print(f"  {char}: {bucket['hue']}")
    if not any(b.get("hue") for b in per_char.values()):
        print("  keiner")


if __name__ == "__main__":
    main()
