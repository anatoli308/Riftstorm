"""Konvertiert MUGEN-Stages nach FLARE und legt sie im Unity-Projekt ab.

Warum das noetig ist: die Charakterdateien enthalten keinerlei Angabe darueber,
wo ein Kaempfer steht, wo der Boden liegt und wie breit der Kameraausschnitt
ist. Genau diese Werte braucht die Showcase aber, damit bildschirmverankerte
Effekte (``postype = back/left/right/front``), Projektilweiten und die
Trainingspuppe dort landen, wo MUGEN sie zeichnet.

Ergebnis pro Stage::

    Assets/StreamingAssets/Custom_Stages/<Name>/<Name>.json
    Assets/Art/Custom_Stages/<Name>/atlas.png
"""
from __future__ import annotations

import json
import os
import shutil
import sys

from mugen.atlas import pack_sprites, save_atlas
from mugen.sff import read_sff
from mugen.stage_def import StageBgLayer, StageDef, parse_stage_def

_HERE = os.path.dirname(os.path.abspath(__file__))
_PROJECT = os.path.abspath(os.path.join(_HERE, "..", "..", ".."))
_SOURCE_ROOT = os.path.join(_HERE, "mugen_stage_data")
_OUT_ROOT = os.path.join(_HERE, "out", "stages")
_STREAM_ROOT = os.path.join(_PROJECT, "Assets", "StreamingAssets", "Custom_Stages")
_ART_ROOT = os.path.join(_PROJECT, "Assets", "Art", "Custom_Stages")

_MS_PER_TICK = 1000.0 / 60.0

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")


def _layer_frames(layer: StageBgLayer, stage: StageDef) -> list[dict]:
    """Zeitachse einer BG-Ebene. ``ms = 0`` heisst: Bild bleibt stehen."""
    if layer.kind != "anim" or layer.action is None:
        return [{"sprite": layer.sprite, "ms": 0, "x": 0, "y": 0}] if layer.sprite else []

    action = next((a for a in stage.actions if a.number == layer.action), None)
    if action is None or not action.frames:
        return [{"sprite": layer.sprite, "ms": 0, "x": 0, "y": 0}] if layer.sprite else []

    return [
        {
            "sprite": f"{frame.group}_{frame.image}",
            "ms": 0 if frame.ticks < 0 else round(frame.ticks * _MS_PER_TICK),
            "x": frame.x_off,
            "y": frame.y_off,
        }
        for frame in action.frames
    ]


def _layer_payload(layer: StageBgLayer, frames: list[dict]) -> dict:
    return {
        "name": layer.name,
        "layer": layer.layer,
        "start": [layer.start[0], layer.start[1]],
        "delta": [layer.delta[0], layer.delta[1]],
        "scale": [layer.scale[0], layer.scale[1]],
        "tile": [layer.tile[0], layer.tile[1]],
        "trans": layer.trans,
        "frames": frames,
    }


def convert(folder: str) -> tuple[str, int, int]:
    """Baut eine Stage. Liefert (Name, Ebenen, Sprites)."""
    def_files = [f for f in sorted(os.listdir(folder)) if f.lower().endswith(".def")]
    if not def_files:
        raise FileNotFoundError(f"keine .def in {folder}")

    stage = parse_stage_def(os.path.join(folder, def_files[0]))
    name = os.path.basename(folder)

    layers: list[dict] = []
    used: set[str] = set()
    for layer in stage.layers:
        frames = _layer_frames(layer, stage)
        if not frames:
            continue
        used.update(frame["sprite"] for frame in frames)
        layers.append(_layer_payload(layer, frames))

    sff = read_sff(stage.sff)
    wanted = [s for s in sff.sprites if f"{s.group}_{s.image}" in used]
    pages = pack_sprites(wanted, max_width=4096)

    out_dir = os.path.join(_OUT_ROOT, name)
    os.makedirs(out_dir, exist_ok=True)
    save_atlas(pages, out_dir)

    sprites = [
        {
            "key": s.key, "x": s.x, "y": s.y, "w": s.w, "h": s.h,
            "pivotX": s.pivot_x, "pivotY": s.pivot_y, "page": s.page,
        }
        for page in pages for s in page.sprites
    ]
    images = [f"atlas.png" if i == 0 else f"atlas_{i}.png" for i in range(len(pages))]

    payload = {
        "name": stage.name,
        "folder": name,
        "localcoord": [stage.localcoord[0], stage.localcoord[1]],
        "zoffset": stage.zoffset,
        "p1start": [stage.p1start[0], stage.p1start[1]],
        "p2start": [stage.p2start[0], stage.p2start[1]],
        "player_distance": stage.player_distance,
        "camera_start": [stage.camera_start[0], stage.camera_start[1]],
        "camera_bounds": list(stage.camera_bounds),
        "screen_bounds": [stage.screen_bounds[0], stage.screen_bounds[1]],
        "image": images[0],
        "images": images,
        "sprites": sprites,
        "layers": layers,
    }
    with open(os.path.join(out_dir, f"{name}.json"), "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2, ensure_ascii=False)

    return name, len(layers), len(sprites)


def deploy(name: str) -> tuple[int, int]:
    """JSON nach StreamingAssets, Atlas-Seiten nach Art."""
    src = os.path.join(_OUT_ROOT, name)
    manifest = os.path.join(src, f"{name}.json")
    if not os.path.isfile(manifest):
        return 0, 0

    stream_dst = os.path.join(_STREAM_ROOT, name)
    art_dst = os.path.join(_ART_ROOT, name)
    os.makedirs(stream_dst, exist_ok=True)
    os.makedirs(art_dst, exist_ok=True)
    shutil.copy2(manifest, os.path.join(stream_dst, f"{name}.json"))

    with open(manifest, encoding="utf-8") as fh:
        images = json.load(fh).get("images", [])
    for image in images:
        shutil.copy2(os.path.join(src, image), os.path.join(art_dst, image))
    return 1, len(images)


def main() -> int:
    if not os.path.isdir(_SOURCE_ROOT):
        print(f"FEHLER: {_SOURCE_ROOT} fehlt")
        return 1

    folders = sorted(
        os.path.join(_SOURCE_ROOT, d) for d in os.listdir(_SOURCE_ROOT)
        if os.path.isdir(os.path.join(_SOURCE_ROOT, d))
    )
    failures: list[str] = []
    for folder in folders:
        try:
            name, layer_count, sprite_count = convert(folder)
            streamed, arted = deploy(name)
            print(f"{name:12s} {layer_count:2d} Ebenen  {sprite_count:3d} Sprites"
                  f"  -> {streamed} JSON, {arted} Atlas")
        except Exception as exc:  # pragma: no cover - Diagnose beim Import
            failures.append(f"{os.path.basename(folder)}: {exc}")
            print(f"FEHLER {os.path.basename(folder)}: {exc}")

    print(f"\n{len(folders) - len(failures)}/{len(folders)} Stages konvertiert")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
