"""MUGEN -> Unity importer CLI.

Usage:
    python mugen_import.py <char_folder> [--out <dir>] [--no-individual-pngs]
                           [--max-width 4096] [--padding 2]
                           [--no-palettes] [--no-states] [--no-sounds]

Example:
    python mugen_import.py mugen_data/Mudpenis --out out/Mudpenis

Produces in <out>:
    char.json           - character metadata (from .def)
    atlas.png           - packed sprite atlas
    atlas.sprites.json  - per-sprite rects + pivots
    animations.json     - 8-direction animation lists (incl. Clsn boxes)
    sprites/<g>_<i>.png - (optional) individual sprite PNGs for debugging
    palettes.json       - all Pal1..Pal12 .act color tables
    palettes/lut.png    - 256xN LUT texture (one row per palette)
    constants.json      - CNS [Data]/[Size]/[Velocity]/[Movement]/[Quotes]
    states.json         - merged Statedef state machine (all .cns + .st files)
    commands.json       - .cmd commands + Statedef -1 listener controllers
    sounds.json         - .snd index
    sounds/<g>_<s>.wav  - exported RIFF WAV payloads
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Optional

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
if _THIS_DIR not in sys.path:
    sys.path.insert(0, _THIS_DIR)

from mugen.def_parser import parse_def, CharDef
from mugen.sff_v1 import read_sff_v1
from mugen.air import parse_air
from mugen.atlas import pack_sprites, save_atlas
from mugen.eight_dir import build_8dir, save_animations_json
from mugen.palette import discover_palettes, Palette
from mugen.cns_parser import parse_cns, CnsConstants
from mugen.cns_states import parse_states, merge_states, to_jsonable as states_to_json
from mugen.cmd_parser import parse_cmd, commands_to_jsonable
from mugen.snd_parser import read_snd, export_wavs, to_jsonable as snd_to_json


def main(argv: Optional[list[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Import a MUGEN character into a Unity-ready bundle.",
    )
    parser.add_argument("char_folder", help="Path to a MUGEN character folder (containing the .def)")
    parser.add_argument("--out", default=None, help="Output directory (default: ./out/<char>)")
    parser.add_argument("--max-width", type=int, default=4096, help="Atlas max width in pixels")
    parser.add_argument("--padding", type=int, default=2, help="Padding between sprites in atlas")
    parser.add_argument("--transparent-index", type=int, default=0,
                        help="Palette index treated as transparent (MUGEN default: 0)")
    parser.add_argument("--no-individual-pngs", action="store_true",
                        help="Skip writing per-sprite PNGs (atlas only)")
    parser.add_argument("--no-palettes", action="store_true", help="Skip palette extraction")
    parser.add_argument("--no-states", action="store_true", help="Skip CNS/CMD state machine parsing")
    parser.add_argument("--no-sounds", action="store_true", help="Skip .snd extraction")
    parser.add_argument("--def-file", default=None,
                        help="Explicit .def filename (otherwise auto-detected in folder)")
    args = parser.parse_args(argv)

    char_folder = os.path.abspath(args.char_folder)
    if not os.path.isdir(char_folder):
        print(f"ERROR: not a directory: {char_folder}", file=sys.stderr)
        return 2

    def_path = _locate_def(char_folder, args.def_file)
    if def_path is None:
        print(f"ERROR: no .def file found in {char_folder}", file=sys.stderr)
        return 2

    print(f"[mugen-import] Reading {def_path}")
    cd: CharDef = parse_def(def_path)
    print(f"[mugen-import] Char: {cd.display_name or cd.name!r} by {cd.author!r}")

    sff_path = os.path.join(char_folder, cd.sprite_file) if cd.sprite_file else ""
    air_path = os.path.join(char_folder, cd.anim_file) if cd.anim_file else ""

    if not os.path.isfile(sff_path):
        print(f"ERROR: SFF not found: {sff_path}", file=sys.stderr)
        return 2
    if not os.path.isfile(air_path):
        print(f"ERROR: AIR not found: {air_path}", file=sys.stderr)
        return 2

    out_dir = args.out or os.path.join(
        os.getcwd(), "out", _safe_name(cd.name or os.path.basename(char_folder)),
    )
    os.makedirs(out_dir, exist_ok=True)

    # --- sprites + animations ------------------------------------------------
    print(f"[mugen-import] Parsing SFF v1: {sff_path}")
    sff = read_sff_v1(sff_path)
    print(f"[mugen-import]   sprites: {len(sff.sprites)} (groups={sff.num_groups}, images={sff.num_images})")

    print(f"[mugen-import] Parsing AIR: {air_path}")
    actions = parse_air(air_path)
    total_frames = sum(len(a.frames) for a in actions)
    print(f"[mugen-import]   actions: {len(actions)} (total frames: {total_frames})")

    if not args.no_individual_pngs:
        sprites_dir = os.path.join(out_dir, "sprites")
        os.makedirs(sprites_dir, exist_ok=True)
        for s in sff.sprites:
            if s.width <= 0 or s.height <= 0:
                continue
            img = s.to_pil(transparent_index=args.transparent_index)
            img.save(os.path.join(sprites_dir, f"{s.group}_{s.image}.png"), "PNG")
        print(f"[mugen-import]   wrote {len(sff.sprites)} individual PNGs to {sprites_dir}")

    print("[mugen-import] Packing atlas...")
    atlas = pack_sprites(
        sff.sprites,
        max_width=args.max_width,
        padding=args.padding,
        transparent_index=args.transparent_index,
    )
    png_path, sprites_json = save_atlas(atlas, out_dir)
    print(f"[mugen-import]   atlas: {atlas.width}x{atlas.height} -> {png_path}")

    atlas_keys = {s.key for s in atlas.sprites}
    print("[mugen-import] Building 8-direction animations...")
    exported = build_8dir(actions, atlas_keys)
    anim_json = save_animations_json(exported, out_dir)
    print(f"[mugen-import]   wrote {anim_json}")

    # --- palettes ------------------------------------------------------------
    palettes: list[Palette] = []
    if not args.no_palettes and cd.palettes:
        print(f"[mugen-import] Loading palettes ({len(cd.palettes)} slots referenced)...")
        palettes = discover_palettes(char_folder, cd.palettes)
        _write_palettes(palettes, out_dir)
        print(f"[mugen-import]   loaded {len(palettes)} .act palettes")

    # --- CNS constants -------------------------------------------------------
    constants: CnsConstants | None = None
    cns_path = _resolve_in_char(char_folder, cd.cns_file)
    if cns_path:
        print(f"[mugen-import] Parsing CNS constants: {cd.cns_file}")
        constants = parse_cns(cns_path)
        with open(os.path.join(out_dir, "constants.json"), "w", encoding="utf-8") as fh:
            json.dump(constants.to_typed(), fh, indent=2)
        print("[mugen-import]   wrote constants.json")

    # --- state machine (multi-file) -----------------------------------------
    states_files: list[str] = []
    if not args.no_states:
        # The main CNS may also contain Statedef blocks (old MUGEN style).
        if cns_path:
            states_files.append(cns_path)
        for rel in cd.state_files:
            p = _resolve_in_char(char_folder, rel)
            if p:
                states_files.append(p)
        if cd.common_state_file:
            p = _resolve_in_char(char_folder, cd.common_state_file)
            if p:
                states_files.append(p)

    if states_files:
        print(f"[mugen-import] Parsing state machine across {len(states_files)} file(s)...")
        all_states = [parse_states(p) for p in states_files]
        merged = merge_states(*all_states)
        with open(os.path.join(out_dir, "states.json"), "w", encoding="utf-8") as fh:
            json.dump({
                "files": [os.path.relpath(p, char_folder) for p in states_files],
                "states": states_to_json(merged),
            }, fh, indent=2)
        print(f"[mugen-import]   states: {len(merged)} statedef(s) -> states.json")

    # --- commands ------------------------------------------------------------
    cmd_path = _resolve_in_char(char_folder, cd.cmd_file)
    cmd_count = 0
    if not args.no_states and cmd_path:
        print(f"[mugen-import] Parsing CMD: {cd.cmd_file}")
        cmd = parse_cmd(cmd_path)
        cmd_count = len(cmd.commands)
        payload = commands_to_jsonable(cmd)
        payload["state_minus1"] = states_to_json(cmd.state_machine)
        with open(os.path.join(out_dir, "commands.json"), "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=2)
        print(f"[mugen-import]   commands: {cmd_count} -> commands.json")

    # --- sounds --------------------------------------------------------------
    snd_count = 0
    snd_path = _resolve_in_char(char_folder, cd.snd_file)
    if not args.no_sounds and snd_path:
        print(f"[mugen-import] Parsing SND: {cd.snd_file}")
        snd = read_snd(snd_path)
        sounds_dir = os.path.join(out_dir, "sounds")
        files = export_wavs(snd, sounds_dir)
        snd_count = sum(1 for f in files if f)
        with open(os.path.join(out_dir, "sounds.json"), "w", encoding="utf-8") as fh:
            json.dump(snd_to_json(snd, files), fh, indent=2)
        print(f"[mugen-import]   wrote {snd_count}/{len(snd.subfiles)} wavs -> {sounds_dir}")

    # --- char metadata -------------------------------------------------------
    char_json_path = os.path.join(out_dir, "char.json")
    with open(char_json_path, "w", encoding="utf-8") as fh:
        json.dump({
            "name": cd.name,
            "displayName": cd.display_name,
            "author": cd.author,
            "spriteFile": cd.sprite_file,
            "animFile": cd.anim_file,
            "cmdFile": cd.cmd_file,
            "sndFile": cd.snd_file,
            "cnsFile": cd.cns_file,
            "stateFiles": cd.state_files,
            "commonStateFile": cd.common_state_file,
            "palettes": {str(k): v for k, v in sorted(cd.palettes.items())},
            "counts": {
                "sprites": len(sff.sprites),
                "actions": len(actions),
                "frames": total_frames,
                "palettes": len(palettes),
                "commands": cmd_count,
                "sounds": snd_count,
            },
        }, fh, indent=2)
    print(f"[mugen-import]   wrote {char_json_path}")

    print(f"[mugen-import] Done. Output in: {out_dir}")
    return 0


def _write_palettes(palettes: list[Palette], out_dir: str) -> None:
    """Write palettes.json + a 256xN LUT PNG (one row per palette)."""
    if not palettes:
        return
    pal_dir = os.path.join(out_dir, "palettes")
    os.makedirs(pal_dir, exist_ok=True)
    payload = {
        "palettes": [
            {
                "name": p.name,
                "source": os.path.basename(p.source),
                "colors": [[r, g, b] for (r, g, b) in p.colors],
            }
            for p in palettes
        ],
    }
    with open(os.path.join(out_dir, "palettes.json"), "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)

    try:
        from PIL import Image
    except ImportError:
        return
    lut = Image.new("RGB", (256, len(palettes)), (0, 0, 0))
    px = lut.load()
    for y, pal in enumerate(palettes):
        for x, (r, g, b) in enumerate(pal.colors):
            px[x, y] = (r, g, b)
    lut.save(os.path.join(pal_dir, "lut.png"), "PNG")


def _resolve_in_char(char_folder: str, rel: str) -> str | None:
    if not rel:
        return None
    p = os.path.join(char_folder, rel)
    return p if os.path.isfile(p) else None


def _locate_def(folder: str, explicit: Optional[str]) -> Optional[str]:
    if explicit:
        p = os.path.join(folder, explicit) if not os.path.isabs(explicit) else explicit
        return p if os.path.isfile(p) else None
    for name in sorted(os.listdir(folder)):
        if name.lower().endswith(".def"):
            return os.path.join(folder, name)
    return None


def _safe_name(s: str) -> str:
    return "".join(c if c.isalnum() or c in ("-", "_") else "_" for c in s).strip("_") or "char"


if __name__ == "__main__":
    sys.exit(main())
