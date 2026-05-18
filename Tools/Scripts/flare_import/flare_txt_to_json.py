"""Convert FLARE-style sprite definition .txt files into Riftstorm FLARE NPC JSON.

Reads files of the form:

    image=npc_zombie.png

    [stance]
    frames=4
    duration=533ms
    type=back_forth
    frame=F,D,x,y,w,h,ox,oy
    ...

…and emits JSON matching FlareAtlasDef:

    {
      "image": "...",
      "animations": {
        "stance": {
          "frames_count": 4,
          "duration_ms": 533,
          "type": "back_forth",
          "frames": [
            [ {x,y,w,h,ox,oy}, ... 8 cells ],
            ...
          ]
        }
      }
    }

Direction indices follow FLARE / Riftstorm convention (0..7).

Existing .meta files in the target directory are NEVER touched.

Usage:

    python flare_txt_to_json.py <source_dir> <target_dir> [--ext .txt]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

DIRECTION_COUNT = 8

ALLOWED_TYPES = {"back_forth", "looped", "play_once"}


def parse_flare_txt(path: Path) -> Dict[str, Any]:
    """Parse a FLARE .txt and return a dict matching FlareAtlasDef."""
    image: Optional[str] = None
    animations: Dict[str, Dict[str, Any]] = {}
    current: Optional[str] = None
    current_meta: Dict[str, Any] = {}
    # frames map: anim_name -> { frame_index -> [cell_or_None * 8] }
    frames_map: Dict[str, Dict[int, List[Optional[Dict[str, int]]]]] = {}

    with path.open("r", encoding="utf-8", errors="replace") as fp:
        for raw in fp:
            line = raw.strip()
            if not line or line.startswith("#") or line.startswith(";"):
                continue
            if line.startswith("[") and line.endswith("]"):
                current = line[1:-1].strip()
                current_meta = {}
                animations[current] = current_meta
                frames_map.setdefault(current, {})
                continue
            if "=" not in line:
                continue
            key, _, value = line.partition("=")
            key = key.strip()
            value = value.strip()
            if current is None:
                # Global section (only "image=" expected).
                if key == "image":
                    image = value
                continue
            if key == "frames":
                try:
                    current_meta["frames_count"] = int(value)
                except ValueError:
                    pass
            elif key == "duration":
                # "533ms" -> 533. Also accept "0.5s" defensively.
                m = re.match(r"^(\d+(?:\.\d+)?)\s*(ms|s)?$", value)
                if m:
                    n = float(m.group(1))
                    unit = m.group(2) or "ms"
                    if unit == "s":
                        n *= 1000.0
                    current_meta["duration_ms"] = int(round(n))
            elif key == "type":
                if value in ALLOWED_TYPES:
                    current_meta["type"] = value
            elif key == "frame":
                parts = [p.strip() for p in value.split(",")]
                if len(parts) < 8:
                    continue
                try:
                    f = int(parts[0])
                    d = int(parts[1])
                    x = int(parts[2])
                    y = int(parts[3])
                    w = int(parts[4])
                    h = int(parts[5])
                    ox = int(parts[6])
                    oy = int(parts[7])
                except ValueError:
                    continue
                if d < 0 or d >= DIRECTION_COUNT:
                    continue
                anim_frames = frames_map[current]
                row = anim_frames.get(f)
                if row is None:
                    row = [None] * DIRECTION_COUNT
                    anim_frames[f] = row
                row[d] = {"x": x, "y": y, "w": w, "h": h, "ox": ox, "oy": oy}

    # Materialize frames as ordered list per animation.
    for name, meta in animations.items():
        frames_count = int(meta.get("frames_count", 0))
        anim_frames = frames_map.get(name, {})
        if frames_count <= 0 and anim_frames:
            frames_count = max(anim_frames.keys()) + 1
            meta["frames_count"] = frames_count
        ordered: List[List[Optional[Dict[str, int]]]] = []
        for f in range(frames_count):
            row = anim_frames.get(f)
            if row is None:
                row = [None] * DIRECTION_COUNT
            ordered.append(row)
        meta["frames"] = ordered
        meta.setdefault("duration_ms", 0)
        meta.setdefault("type", "looped")
        meta.setdefault("frames_count", frames_count)

    return {
        "image": image or "",
        "animations": animations,
    }


def convert_one(src: Path, dst: Path) -> Dict[str, Any]:
    data = parse_flare_txt(src)
    dst.parent.mkdir(parents=True, exist_ok=True)
    with dst.open("w", encoding="utf-8", newline="\n") as fp:
        json.dump(data, fp, indent=2)
        fp.write("\n")
    return _summarize(data)


def _summarize(data: Dict[str, Any]) -> Dict[str, Any]:
    summary: Dict[str, Any] = {"anims": {}}
    for name, anim in data.get("animations", {}).items():
        frames = anim.get("frames", []) or []
        total = 0
        null = 0
        per_frame: List[int] = []
        for row in frames:
            non_null = 0
            for cell in row:
                total += 1
                if cell is None:
                    null += 1
                else:
                    non_null += 1
            per_frame.append(non_null)
        summary["anims"][name] = {
            "frames_count": anim.get("frames_count", 0),
            "cells": total,
            "null": null,
            "per_frame_dirs": per_frame,
        }
    return summary


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert FLARE .txt -> Riftstorm NPC JSON.")
    parser.add_argument("source", type=Path, help="Source directory with .txt files.")
    parser.add_argument("target", type=Path, help="Target directory for .json files.")
    parser.add_argument("--ext", default=".txt", help="Source file extension (default .txt).")
    parser.add_argument("--dry-run", action="store_true", help="Parse but do not write.")
    parser.add_argument("--verbose", action="store_true", help="Verbose per-anim summary.")
    args = parser.parse_args()

    if not args.source.is_dir():
        print(f"Source not found: {args.source}", file=sys.stderr)
        return 2
    files = sorted(p for p in args.source.iterdir() if p.is_file() and p.suffix == args.ext)
    if not files:
        print(f"No '{args.ext}' files found in {args.source}", file=sys.stderr)
        return 1

    total_files = 0
    total_warn = 0
    for src in files:
        dst = args.target / (src.stem + ".json")
        try:
            if args.dry_run:
                data = parse_flare_txt(src)
                summary = _summarize(data)
            else:
                summary = convert_one(src, dst)
        except Exception as ex:  # noqa: BLE001
            print(f"FAIL {src.name}: {ex}", file=sys.stderr)
            total_warn += 1
            continue
        total_files += 1
        warn = []
        for name, info in summary["anims"].items():
            if info["null"] > 0:
                warn.append(f"{name}({info['null']} null)")
        flag = ""
        if warn:
            flag = " WARN: " + ", ".join(warn)
            total_warn += 1
        print(f"OK  {src.name} -> {dst.name}{flag}")
        if args.verbose:
            for name, info in summary["anims"].items():
                print(f"     {name:8s} frames={info['frames_count']} per_frame_dirs={info['per_frame_dirs']}")
    print(f"\nConverted {total_files} files. Warnings: {total_warn}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
