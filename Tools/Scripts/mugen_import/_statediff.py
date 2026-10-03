"""Stellt einen Statedef aus den CNS-Daten dem exportierten Ergebnis gegenueber.

Links die Controller, die MUGEN in diesem State ausfuehrt, rechts das, was im
FLARE-Dokument davon angekommen ist. Damit laesst sich pruefen, ob ein Move
vollstaendig uebernommen wurde.

Aufruf: python _statediff.py <Charakter> <state> [<state> ...]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DEPLOY = ROOT / "Assets" / "StreamingAssets" / "Custom_Characters"
OUT = Path(__file__).resolve().parent / "out"

INTERESTING = ("explod", "projectile", "helper", "changeanim", "changeanim2",
               "changestate", "selfstate", "playsnd", "hitdef", "afterimage",
               "palfx", "allpalfx", "posadd", "posset", "velset", "veladd",
               "sprpriority", "angledraw", "angleset", "envshake", "superpause",
               "pause", "removeexplod", "modifyexplod", "targetstate", "destroyself")


def dump_source(char: str, numbers: set[int]) -> None:
    states = json.loads((OUT / char / "states.json").read_text(encoding="utf-8"))["states"]
    for state in states:
        if state.get("number") not in numbers:
            continue
        header = state.get("header") or {}
        print(f"--- CNS Statedef {state['number']}  \"{(state.get('label') or '').strip()}\"")
        print(f"    header: anim={header.get('anim')} type={header.get('type')} "
              f"movetype={header.get('movetype')} physics={header.get('physics')} "
              f"ctrl={header.get('ctrl')} sprpriority={header.get('sprpriority')}")
        for ctrl in state.get("controllers") or []:
            params = ctrl.get("params") or {}
            ctype = str(params.get("type", "")).lower()
            if ctype not in INTERESTING:
                continue
            keys = {k: v for k, v in params.items() if k != "type"}
            trig = ctrl.get("triggers") or {}
            flat = "; ".join(f"{k}={v}" for k, v in trig.items())
            print(f"    {ctype:<14} {json.dumps(keys, ensure_ascii=False)[:150]}")
            print(f"                   [{flat[:130]}]")


def dump_export(char: str, numbers: set[int]) -> None:
    flare = json.loads((DEPLOY / char / f"{char}.json").read_text(encoding="utf-8"))
    found = {s["state"] for s in flare["sequences"]}
    for number in sorted(numbers):
        if number not in found:
            print(f"--- EXPORT Statedef {number}: NICHT EXPORTIERT")
            continue
        seq = next(s for s in flare["sequences"] if s["state"] == number)
        print(f"--- EXPORT Statedef {number}  \"{seq['label'][:40]}\" kat={seq['category']}")
        print(f"    anims:   {[(a['anim'], a['time']) for a in seq['anims']]}")
        print(f"    effects: {[(e['anim'], e['time'], e['postype']) for e in seq['effects']]}")
        print(f"    helpers: {len(seq['helpers'])}  hits: {len(seq['hits'])}  "
              f"sounds: {len(seq['sounds'])}")


def main() -> None:
    char = sys.argv[1]
    numbers = {int(n) for n in sys.argv[2:]}
    dump_source(char, numbers)
    print()
    dump_export(char, numbers)


if __name__ == "__main__":
    main()
