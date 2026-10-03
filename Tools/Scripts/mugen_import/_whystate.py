"""Warum fehlt ein Statedef im Export? Zeigt Header-Anim und Controller."""
from __future__ import annotations

import collections
import json
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_OUT = os.path.join(_HERE, "out")

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")


def main() -> None:
    char = sys.argv[1]
    with open(os.path.join(_OUT, char, "states.json"), encoding="utf-8") as fh:
        raw = json.load(fh)
    states = raw if isinstance(raw, list) else raw.get("states", [])
    by_number = {int(s["number"]): s for s in states if "number" in s}

    with open(os.path.join(_OUT, char, f"{char}.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)
    exported = {int(s["state"]) for s in manifest.get("sequences", []) if "state" in s}
    animations = set(manifest.get("animations", {}))

    reasons: collections.Counter[str] = collections.Counter()
    samples: dict[str, list[str]] = collections.defaultdict(list)

    for number in sorted(set(by_number) - exported):
        state = by_number[number]
        header = state.get("header", {}) or {}
        anim = header.get("anim")
        controllers = state.get("controllers", []) or []
        kinds = {str(c.get("type", "")).lower() for c in controllers}

        if number < 0:
            reason = "negativ (globale States, korrekt uebersprungen)"
        elif anim is None and not (kinds & {"changeanim", "changeanim2", "explod", "helper", "projectile"}):
            reason = "kein anim im Header und kein ChangeAnim/Explod/Helper"
        elif anim is not None and f"action_{anim}" not in animations:
            reason = f"Header-anim {anim} existiert nicht im Atlas"
        else:
            reason = "andere Ursache"
        reasons[reason] += 1
        if len(samples[reason]) < 12:
            samples[reason].append(f"{number}(anim={anim}, ctrl={len(controllers)})")

    print(f"{char}: {len(by_number)} Statedefs, {len(exported)} exportiert, "
          f"{len(by_number) - len(exported)} fehlen\n")
    for reason, count in reasons.most_common():
        print(f"  {count:4d}  {reason}")
        print(f"        z.B. {', '.join(samples[reason])}")


if __name__ == "__main__":
    main()
