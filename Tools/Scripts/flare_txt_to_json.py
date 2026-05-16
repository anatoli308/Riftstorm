"""
FLARE/Steam .txt → .json Konverter (Player + NPC Sprite-Definitionen).

Eingabeformat (Beispiel):
    image=foo.png

    [stance]
    frames=4
    duration=800ms
    type=back_forth
    frame=A,B,x,y,w,h,ox,oy
    ...

Ausgabeformat (matcht den existierenden player_male/<item>.json-Stil):
{
  "image": "foo.png",
  "animations": {
    "stance": {
      "frames_count": 4,
      "duration_ms": 800,
      "type": "back_forth",
      "frames": [   # outer = anim_frame_index (Laenge = frames_count)
        [           # inner = direction (Laenge = 8, fehlende = null)
          {"x":..., "y":..., "w":..., "h":..., "ox":..., "oy":...},
          ...
        ],
        ...
      ]
    },
    ...
  }
}

Aufruf:
    python flare_txt_to_json.py <src_dir> <dst_dir>
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path


def parse_duration(value: str) -> int:
    """`800ms` -> 800. Akzeptiert auch nackte Zahlen."""
    s = value.strip().lower()
    if s.endswith("ms"):
        s = s[:-2]
    return int(s)


def parse_file(txt_path: Path) -> dict:
    image: str | None = None
    animations: dict[str, dict] = {}
    current: dict | None = None
    # Sammelt frames als dict[(anim_idx, dir_idx)] = {x,y,w,h,ox,oy}
    current_frames: dict[tuple[int, int], dict] = {}

    def flush_current(name: str | None) -> None:
        if name is None or current is None:
            return
        frames_count = current.get("frames_count", 0)
        # Outer = frames_count, inner = 8
        outer: list[list[dict | None]] = [
            [None] * 8 for _ in range(frames_count)
        ]
        max_outer = -1
        for (a, b), rect in current_frames.items():
            max_outer = max(max_outer, a)
            if 0 <= a < frames_count and 0 <= b < 8:
                outer[a][b] = rect
            else:
                # Wenn die Indizes ausserhalb von frames_count/8 liegen,
                # vergroessern wir das Array bedarfsorientiert.
                while len(outer) <= a:
                    outer.append([None] * 8)
                if b >= 8:
                    for row in outer:
                        while len(row) <= b:
                            row.append(None)
                outer[a][b] = rect
        current["frames"] = outer
        animations[name] = current

    current_name: str | None = None

    for raw in txt_path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue

        section_match = re.match(r"^\[([^\]]+)\]$", line)
        if section_match:
            # Vorherige Section abschliessen
            flush_current(current_name)
            current_name = section_match.group(1).strip()
            current = {
                "frames_count": 0,
                "duration_ms": 0,
                "type": "play_once",
            }
            current_frames = {}
            continue

        if "=" not in line:
            continue

        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip()

        if current_name is None:
            # Header-Felder (image=...)
            if key == "image":
                image = value
            continue

        if key == "frames":
            current["frames_count"] = int(value)
        elif key == "duration":
            current["duration_ms"] = parse_duration(value)
        elif key == "type":
            current["type"] = value
        elif key == "frame":
            parts = [p.strip() for p in value.split(",")]
            if len(parts) != 8:
                raise ValueError(
                    f"{txt_path}: ungueltige frame-Zeile (erwartet 8 Felder): {raw!r}"
                )
            a, b, x, y, w, h, ox, oy = (int(p) for p in parts)
            current_frames[(a, b)] = {
                "x": x,
                "y": y,
                "w": w,
                "h": h,
                "ox": ox,
                "oy": oy,
            }
        # Andere Keys ignorieren (z.B. unbekannte Flags).

    # Letzte Section abschliessen
    flush_current(current_name)

    if image is None:
        raise ValueError(f"{txt_path}: kein image=... Header gefunden.")

    return {"image": image, "animations": animations}


def convert_dir(src_dir: Path, dst_dir: Path) -> tuple[int, list[str]]:
    dst_dir.mkdir(parents=True, exist_ok=True)
    written: list[str] = []
    for txt in sorted(src_dir.glob("*.txt")):
        data = parse_file(txt)
        out_path = dst_dir / f"{txt.stem}.json"
        out_path.write_text(
            json.dumps(data, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        written.append(out_path.name)
    return len(written), written


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print("Usage: python flare_txt_to_json.py <src_dir> <dst_dir>")
        return 2
    src = Path(argv[1])
    dst = Path(argv[2])
    if not src.is_dir():
        print(f"Quelle nicht gefunden: {src}")
        return 1
    count, names = convert_dir(src, dst)
    print(f"OK: {count} Dateien geschrieben nach {dst}")
    for n in names:
        print(f"  {n}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
