"""Listet Animationen, die in keiner Sequenz vorkommen.

Eine Animation kann im Atlas vorhanden und trotzdem im Showcase nie zu sehen
sein, wenn keine Sequenz sie als Koerper-Animation oder als Effekt einplant.
Ausgegeben wird zusaetzlich die Atlas-Position des ersten Frames, damit sich
ein Fund auf der Atlas-Seite wiederfinden laesst.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DEPLOY = ROOT / "Assets" / "StreamingAssets" / "Custom_Characters"


def referenced(flare: dict) -> set[str]:
    keys: set[str] = set()
    for seq in flare.get("sequences", []):
        for entry in seq.get("anims") or []:
            if isinstance(entry.get("anim"), str):
                keys.add(entry["anim"])
        for fx in seq.get("effects") or []:
            if isinstance(fx.get("anim"), str):
                keys.add(fx["anim"])
            if isinstance(fx.get("hitanim"), str):
                keys.add(fx["hitanim"])
    return keys


def main() -> None:
    name = sys.argv[1] if len(sys.argv) > 1 else "Annabelle"
    flare = json.loads((DEPLOY / name / f"{name}.json").read_text(encoding="utf-8"))
    used = referenced(flare)
    rows = []
    for key, anim in flare["animations"].items():
        if key in used:
            continue
        first = anim["frames"][0][0]
        rows.append((first["y"], first["x"], key, first["w"], first["h"], anim["frames_count"]))
    rows.sort()
    print(f"{name}: {len(flare['animations'])} Animationen, {len(used)} in Sequenzen, "
          f"{len(rows)} ohne Sequenz")
    for y, x, key, w, h, n in rows:
        print(f"   {key:<18} atlas=({x},{y}) {w}x{h} frames={n}")


if __name__ == "__main__":
    main()
