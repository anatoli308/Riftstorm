"""Welche [Size]/[Movement]-Konstanten weichen vom Engine-Default ab?

Nur Abweichungen sind interessant: alles andere deckt der Default ab.
"""
from __future__ import annotations

import json
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"

# Defaults aus Ikemen char.go CharSize.init / CharMovement.init.
DEFAULTS = {
    ("size", "draw_offset"): [0.0, 0.0],
    ("movement", "stand_friction"): 0.85,
    ("movement", "crouch_friction"): 0.82,
    ("movement", "yaccel"): 0.44,
    ("size", "xscale"): 1.0,
    ("size", "yscale"): 1.0,
}


def main() -> None:
    for char_dir in sorted(OUT.iterdir()):
        const_file = char_dir / "constants.json"
        if not const_file.exists():
            continue
        data = json.loads(const_file.read_text("utf-8"))
        hits = []
        for (section, key), default in DEFAULTS.items():
            value = (data.get(section) or {}).get(key)
            if value is None:
                hits.append(f"{key}=FEHLT")
            elif isinstance(default, list):
                if [float(v) for v in value] != default:
                    hits.append(f"{key}={value}")
            elif abs(float(value) - default) > 1e-6:
                hits.append(f"{key}={value}")
        print(f"{char_dir.name:16s} " + ("  ".join(hits) if hits else "alles Default"))


if __name__ == "__main__":
    main()
