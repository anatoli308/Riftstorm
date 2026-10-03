"""Erklaert einzelne Actions: Frames, Sprite-Gruppen, Dauer und wer sie benutzt."""
from __future__ import annotations

import json
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_OUT = os.path.join(_HERE, "out")

# MUGENs reservierte Action-Nummern. Quelle: MUGEN-Dokumentation (Common States).
_RESERVED = {
    0: "Stand (Idle)", 5: "Turning", 10: "Stand -> Crouch", 11: "Crouching (Idle)",
    12: "Crouch -> Stand", 20: "Walk forward", 21: "Walk backward",
    40: "Jump start", 41: "Jump up (neutral)", 42: "Jump forward", 43: "Jump backward",
    44: "Jump up (landing)", 45: "Run forward", 46: "Hop backward", 47: "Landing",
    50: "Jump up (in air)", 100: "Run forward", 105: "Hop backward",
    120: "Guard start (stand)", 130: "Guard (stand)", 131: "Guard hit (stand)",
    132: "Guard -> Stand", 140: "Guard start (crouch)", 150: "Guard (crouch)",
    5000: "Stand get-hit (light, shaking)", 5001: "Stand get-hit (light, back)",
    5010: "Stand get-hit (medium, shaking)", 5011: "Stand get-hit (medium, back)",
    5020: "Stand get-hit (hard, shaking)", 5021: "Stand get-hit (hard, back)",
    5030: "Crouch get-hit (shaking)", 5031: "Crouch get-hit (back)",
    5050: "Air get-hit (shaking)", 5051: "Air get-hit (back)",
    5060: "Air get-hit (falling)", 5070: "Tripped (shaking)", 5071: "Tripped (knocked away)",
    5080: "Air get-hit (fall, up)", 5081: "Air get-hit (fall, down)",
    5100: "Hit the ground (bounce)", 5101: "Downed get-hit (bounce off ground)",
    5110: "Downed (lying)", 5120: "Downed get-hit (from lying)",
    5150: "Downed (dead)", 5160: "Downed (dead, blinking)",
    5200: "Get up (fall recovery)", 5210: "Get up (from lying)",
    5300: "Continue", 5500: "Time over", 5900: "Initialize (round start)",
}

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    char = sys.argv[1]
    wanted = [int(a) for a in sys.argv[2:]]

    with open(os.path.join(_OUT, char, f"{char}.json"), encoding="utf-8") as fh:
        flare = json.load(fh)
    with open(os.path.join(_OUT, char, "states.json"), encoding="utf-8") as fh:
        states = json.load(fh)["states"]

    animations = flare.get("animations", {})
    sequences = flare.get("sequences", [])

    for number in wanted:
        key = f"action_{number}"
        anim = animations.get(key)
        print(f"\n===== action_{number} =====")
        print(f"  MUGEN-Bedeutung: {_RESERVED.get(number, '(charaktereigen)')}")
        if anim is None:
            print("  NICHT im Atlas")
            continue

        frames = anim.get("frames") or []
        durations = anim.get("frame_durations_ms") or []

        print(f"  Typ {anim.get('type')}   {anim.get('frames_count')} Frames   "
              f"{anim.get('duration_ms')} ms   loop_start={anim.get('loop_start', 0)}")
        print(f"  Frame-Dauern: {durations[:14]}{' …' if len(durations) > 14 else ''}")
        print(f"  Frame-Groessen: "
              f"{[(c[0].get('w'), c[0].get('h')) for c in frames[:8] if c]}")

        users = [s for s in sequences if any(a.get("anim") == key for a in s.get("anims", []))]
        for seq in users[:6]:
            label = seq.get("label") or ""
            print(f"    benutzt von State {seq['state']} [{seq['category']}] {label}")
        if not users:
            print("    von keinem State direkt gestartet")

        state = next((s for s in states if s.get("number") == number), None)
        if state is not None:
            print(f"    Statedef {number}: {(state.get('label') or state.get('section') or '')!r} "
                  f"anim={(state.get('header') or {}).get('anim')} "
                  f"ctrl={len(state.get('controllers') or [])}")


if __name__ == "__main__":
    main()
