"""
Konvertiert FLARE `.sa`-Animations-Dumps in die Runtime-JSON-Frames-Liste,
die `SpellAnimationDefinition` unter `Assets/StreamingAssets/spells/animations/`
erwartet.

Quelle: `C:\\Users\\anato\\Downloads\\steam-main\\scripts\\animation\\<name>.sa`
Ziel:   `D:\\Riftstorm\\Assets\\StreamingAssets\\spells\\animations\\<name>.json`

`.sa`-Format (zeilenbasiert)::

    ratio=<int>
    size=<int>            # Source-Canvas-Kantenlaenge in Source-Pixeln
    filename=<stem>
    loopstart=<int>
    loopend=<int>
    delay=<int>
    <leerzeile>
    <1-based-index>,<x>,<y>
    ...

`x,y` = Top-Left-Blit-Offset des per-Frame-PNGs innerhalb der `size x size`
Canvas-Box (Origin oben-links, FLARE-down).

Die JSON-Frames-Liste enthaelt nach Konvertierung nur noch `{ "x": .., "y": .. }`
pro Frame. Frame-Breite/-Hoehe werden zur Laufzeit aus den PNGs gelesen.

Idempotent: ueberschreibt nur `frames` (und stellt sicher, dass `frames_count`
zur Liste passt); alle anderen JSON-Felder bleiben unangetastet.

Aufruf::

    python Tools/Scripts/flare_import/sa_to_json.py
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

DEFAULT_SA_DIR = Path(r"C:\Users\anato\Downloads\steam-main\scripts\animation")
DEFAULT_JSON_DIR = Path(__file__).resolve().parents[3] / "Assets" / "StreamingAssets" / "spells" / "animations"


def parse_sa(path: Path) -> list[dict[str, int]] | None:
    """Liest die Frame-Liste (x,y pro Index) aus einer `.sa`-Datei.

    Returns `None` bei strukturellen Fehlern, sonst eine 0-basierte Liste
    (1-basierter `.sa`-Index wird auf 0-basiert konvertiert).
    """
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        print(f"[skip] {path.name}: {exc}", file=sys.stderr)
        return None

    lines = text.splitlines()
    frames_by_index: dict[int, tuple[int, int]] = {}
    in_frames = False
    for raw in lines:
        line = raw.strip()
        if not line:
            in_frames = True
            continue
        if not in_frames:
            continue
        parts = line.split(",")
        if len(parts) != 3:
            continue
        try:
            idx = int(parts[0])
            x = int(parts[1])
            y = int(parts[2])
        except ValueError:
            continue
        if idx < 1:
            continue
        frames_by_index[idx] = (x, y)

    if not frames_by_index:
        return None

    max_idx = max(frames_by_index)
    out: list[dict[str, int]] = []
    for one_based in range(1, max_idx + 1):
        x, y = frames_by_index.get(one_based, (0, 0))
        out.append({"x": x, "y": y})
    return out


def merge_into_json(json_path: Path, frames: list[dict[str, int]]) -> bool:
    """Schreibt `frames` in das JSON. Gibt True zurueck, wenn der Inhalt
    veraendert wurde (oder neu geschrieben werden musste)."""
    raw = json_path.read_text(encoding="utf-8")
    data = json.loads(raw)
    old = data.get("frames")
    data["frames"] = frames
    if isinstance(data.get("frames_count"), int) and data["frames_count"] != len(frames):
        data["frames_count"] = len(frames)
    if old == frames:
        return False
    new_text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    json_path.write_text(new_text, encoding="utf-8", newline="\n")
    return True


def main(sa_dir: Path = DEFAULT_SA_DIR, json_dir: Path = DEFAULT_JSON_DIR) -> int:
    if not sa_dir.is_dir():
        print(f"[error] .sa source dir not found: {sa_dir}", file=sys.stderr)
        return 1
    if not json_dir.is_dir():
        print(f"[error] JSON target dir not found: {json_dir}", file=sys.stderr)
        return 1

    updated = 0
    unchanged = 0
    skipped_no_json = 0
    skipped_bad_sa = 0

    for sa_path in sorted(sa_dir.glob("*.sa")):
        stem = sa_path.stem
        json_path = json_dir / f"{stem}.json"
        if not json_path.is_file():
            skipped_no_json += 1
            continue
        frames = parse_sa(sa_path)
        if frames is None:
            skipped_bad_sa += 1
            continue
        if merge_into_json(json_path, frames):
            updated += 1
            print(f"[upd] {stem} -> {len(frames)} frames")
        else:
            unchanged += 1

    print()
    print(f"updated         : {updated}")
    print(f"unchanged       : {unchanged}")
    print(f"skipped (no json): {skipped_no_json}")
    print(f"skipped (bad sa) : {skipped_bad_sa}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
