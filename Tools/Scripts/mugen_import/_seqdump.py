"""Zeigt rohe Statedef-Timeline vs. exportierte Sequenz fuer einen State."""
from __future__ import annotations

import json
import os
import sys


def main(char: str, state: int) -> None:
    here = os.path.dirname(os.path.abspath(__file__))
    raw = json.load(open(os.path.join(here, "out", char, "states.json"), encoding="utf-8"))
    doc = json.load(open(os.path.join(here, "out", char, f"{char}.json"), encoding="utf-8"))

    sd = next((s for s in raw["states"] if int(s.get("number", -99999)) == state), None)
    print(f"=== RAW statedef {state} ===")
    if sd is None:
        print("  (not found)")
    else:
        print(f"  header: {json.dumps(sd.get('header', {}), ensure_ascii=False)}")
        for i, ctrl in enumerate(sd.get("controllers", [])):
            params = ctrl.get("params") or {}
            ctype = (params.get("type") or "").lower()
            if ctype not in ("changeanim", "changeanim2", "explod", "helper",
                             "projectile", "changestate", "selfstate", "destroyself"):
                continue
            trig = ctrl.get("triggers")
            shown = {k: v for k, v in params.items()
                     if k in ("anim", "value", "stateno", "postype", "pos", "id", "trans")}
            print(f"  [{i:3}] {ctype:12} {shown} triggers={trig}")

    print(f"\n=== EXPORTED sequence {state} ===")
    seq = next((s for s in doc.get("sequences", []) if s["state"] == state), None)
    if seq is None:
        print("  (not exported)")
        return
    print(f"  label: {seq.get('label')}")
    print(f"  anims: {seq.get('anims')}")
    for e in seq.get("effects", []):
        print(f"    fx t={e['time']:4} {e['anim']:16} pos={e['pos']} prio={e['sprpriority']} rm={e['removetime']}")


if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]))
