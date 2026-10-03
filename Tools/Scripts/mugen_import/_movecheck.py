"""Zeigt je Charakter, wie viele Moves eine Folgeanimation und bewegte Helfer haben.

Damit laesst sich pruefen, ob die Ketten (`ChangeState`) und die Flugbahnen der
geworfenen Objekte tatsaechlich im Export ankommen.

Aufruf: python _movecheck.py [Charakter ...]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

DEPLOY = Path(__file__).resolve().parents[3] / "Assets" / "StreamingAssets" / "Custom_Characters"


def main() -> None:
    names = sys.argv[1:] or sorted(p.name for p in DEPLOY.iterdir() if p.is_dir())
    totals = [0, 0, 0, 0]
    for name in names:
        path = DEPLOY / name / f"{name}.json"
        if not path.exists():
            continue
        flare = json.loads(path.read_text(encoding="utf-8"))
        sequences = flare["sequences"]
        chained = sum(1 for s in sequences if len(s.get("anims") or []) > 1)
        helpers = [h for s in sequences for h in (s.get("helpers") or [])]
        moving = sum(1 for h in helpers
                     if any(h.get("vel") or []) or any(h.get("accel") or []))
        ranked = sum(1 for h in helpers if h.get("sprpriority"))
        print(f"{name:16} sequenzen={len(sequences):4}  mit_kette={chained:4}  "
              f"helfer={len(helpers):4}  bewegt={moving:4}  mit_prio={ranked:4}")
        totals[0] += len(sequences)
        totals[1] += chained
        totals[2] += len(helpers)
        totals[3] += moving
    print(f"\nGESAMT sequenzen={totals[0]} mit_kette={totals[1]} "
          f"helfer={totals[2]} bewegt={totals[3]}")


if __name__ == "__main__":
    main()
