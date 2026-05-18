"""
Source/Stendhal .psi -> JSON Konverter (Partikel-Systeme).

Bytewise 1:1 Port von ``source_client/ParticleSystemInfo.cpp``:

    int   spriteData       // low16 = atlas tile index (4 cols, 32px), high16 != 6 => additive
    int   emission         // particles per second
    float lifetime         // system lifetime in seconds (0 = endless)
    float particleLifeMin
    float particleLifeMax
    float direction        // radians
    float spread           // radians
    int   relative         // 0/1 follow caster (1 => emit relative to caster motion)
    float speedMin / speedMax
    float gravityMin / gravityMax
    float radialAccelMin / radialAccelMax
    float tangentialAccelMin / tangentialAccelMax
    float sizeStart / sizeEnd / sizeVar
    float spinStart / spinEnd / spinVar
    float colorStart.r / g / b / a
    float colorEnd.r   / g / b / a
    float colorVar / alphaVar

= 32 * 4 byte = 128 byte (genau).

Ausgabeformat (gesammelt in einer Datei, Key = .psi-Name ohne Endung):
{
  "casting_holy": {
    "tile_x": 96, "tile_y": 0, "add_blend": true,
    "emission": 200, "lifetime": -1.0,
    "particle_life_min": 2.413, "particle_life_max": 0.0,
    "direction": 6.283, "spread": 0.0, "relative": 0,
    "speed_min": -28.571, "speed_max": -114.286,
    "gravity_min": 0.0, "gravity_max": 0.0,
    "radial_accel_min": 0.4, "radial_accel_max": 1.087,
    "tangential_accel_min": 0.0, "tangential_accel_max": 0.0,
    "size_start": 0.976, "size_end": 0.755, "size_var": 0.755,
    "spin_start": 0.572, "spin_end": 1.0, "spin_var": 0.0,
    "color_start": [0.325, 0.0079, 0.0, 0.0],
    "color_end":   [0.0, 0.0, 0.0, 0.0],
    "color_var": 0.0, "alpha_var": 0.0
  },
  ...
}

Aufruf:
    python psi_to_json.py <src_dir> <dst_file>

Default-Pfade:
    python psi_to_json.py
        ->  C:\\Users\\anato\\Downloads\\steam-main\\scripts\\particles
        ->  d:\\Riftstorm\\Assets\\StreamingAssets\\particles\\_particles.json
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

_PSI_SIZE = 128
_TILE = 32
_COLS = 4


def parse_psi(path: Path) -> dict:
    raw = path.read_bytes()
    if len(raw) != _PSI_SIZE:
        raise ValueError(
            f"{path.name}: erwartet {_PSI_SIZE} Byte, gelesen {len(raw)}."
        )

    # Layout (exakt dem cpp-Reader folgend, 32 * 4 byte = 128):
    #   i32  spriteData
    #   i32  emission
    #   5xf  lifetime, particleLifeMin, particleLifeMax, direction, spread
    #   i32  relative
    #   14xf speedMin/Max, gravityMin/Max, radialMin/Max, tangentMin/Max,
    #        sizeStart/End/Var, spinStart/End/Var
    #   8xf  colorStart RGBA + colorEnd RGBA
    #   2xf  colorVar, alphaVar
    fmt = "<i i 5f i 14f 8f 2f"
    assert struct.calcsize(fmt) == _PSI_SIZE, struct.calcsize(fmt)
    (
        sprite_data,
        emission,
        lifetime,
        plife_min,
        plife_max,
        direction,
        spread,
        relative,
        speed_min,
        speed_max,
        gravity_min,
        gravity_max,
        radial_min,
        radial_max,
        tangent_min,
        tangent_max,
        size_start,
        size_end,
        size_var,
        spin_start,
        spin_end,
        spin_var,
        cs_r,
        cs_g,
        cs_b,
        cs_a,
        ce_r,
        ce_g,
        ce_b,
        ce_a,
        color_var,
        alpha_var,
    ) = struct.unpack(fmt, raw)

    low16 = sprite_data & 0xFFFF
    high16 = (sprite_data >> 16) & 0xFFFF
    tile_x = _TILE * (low16 % _COLS)
    tile_y = _TILE * (low16 // _COLS)
    add_blend = high16 != 6

    return {
        "tile_x": tile_x,
        "tile_y": tile_y,
        "add_blend": add_blend,
        "emission": emission,
        "lifetime": float(lifetime),
        "particle_life_min": float(plife_min),
        "particle_life_max": float(plife_max),
        "direction": float(direction),
        "spread": float(spread),
        "relative": int(relative),
        "speed_min": float(speed_min),
        "speed_max": float(speed_max),
        "gravity_min": float(gravity_min),
        "gravity_max": float(gravity_max),
        "radial_accel_min": float(radial_min),
        "radial_accel_max": float(radial_max),
        "tangential_accel_min": float(tangent_min),
        "tangential_accel_max": float(tangent_max),
        "size_start": float(size_start),
        "size_end": float(size_end),
        "size_var": float(size_var),
        "spin_start": float(spin_start),
        "spin_end": float(spin_end),
        "spin_var": float(spin_var),
        "color_start": [float(cs_r), float(cs_g), float(cs_b), float(cs_a)],
        "color_end": [float(ce_r), float(ce_g), float(ce_b), float(ce_a)],
        "color_var": float(color_var),
        "alpha_var": float(alpha_var),
    }


def main(argv: list[str]) -> int:
    if len(argv) >= 2:
        src_dir = Path(argv[1])
    else:
        src_dir = Path(r"C:\Users\anato\Downloads\steam-main\scripts\particles")
    if len(argv) >= 3:
        dst_file = Path(argv[2])
    else:
        dst_file = Path(
            r"d:\Riftstorm\Assets\StreamingAssets\particles\_particles.json"
        )

    if not src_dir.is_dir():
        print(f"[psi_to_json] Quellordner fehlt: {src_dir}", file=sys.stderr)
        return 1

    out: dict[str, dict] = {}
    skipped: list[str] = []
    for psi in sorted(src_dir.glob("*.psi")):
        try:
            out[psi.stem] = parse_psi(psi)
        except (OSError, ValueError, struct.error) as ex:
            skipped.append(f"{psi.name}: {ex}")

    dst_file.parent.mkdir(parents=True, exist_ok=True)
    dst_file.write_text(
        json.dumps(out, indent=2, ensure_ascii=False, sort_keys=True),
        encoding="utf-8",
    )
    print(
        f"[psi_to_json] {len(out)} Partikel-Systeme -> {dst_file}"
        f" ({len(skipped)} uebersprungen)"
    )
    for s in skipped:
        print(f"  skip {s}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
