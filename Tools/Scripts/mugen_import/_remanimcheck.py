"""Prueft, ob die Aufloese-Animation eines Projektils im Export ankommt.

Sucht je Charakter die `Projectile`-Controller mit `projremanim`/`projhitanim`
und meldet, ob im fertigen FLARE-Dokument ein Effekt mit dieser Action steht.
"""
from __future__ import annotations

import glob
import json
import os
import sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")


def _anim_no(raw: object) -> int | None:
    for part in str(raw or "").replace(",", " ").split():
        try:
            return int(part)
        except ValueError:
            continue
    return None


def main(names: list[str]) -> None:
    for states_path in sorted(glob.glob(os.path.join(OUT, "*", "states.json"))):
        char = os.path.basename(os.path.dirname(states_path))
        if names and char not in names:
            continue
        flare_path = os.path.join(OUT, char, f"{char}.json")
        if not os.path.exists(flare_path):
            continue
        with open(states_path, encoding="utf-8") as fh:
            states = json.load(fh)
        with open(flare_path, encoding="utf-8") as fh:
            flare = json.load(fh)

        wanted: set[str] = set()
        for state in states.get("states", []):
            for ctrl in state.get("controllers") or []:
                params = ctrl.get("params") or {}
                if (params.get("type") or "").lower() != "projectile":
                    continue
                raw = params.get("projremanim")
                if raw is None:
                    raw = params.get("projhitanim")
                num = _anim_no(raw)
                if num is not None and num >= 0:
                    wanted.add(f"action_{num}")
        if not wanted:
            continue

        present: set[str] = set()
        for seq in flare.get("sequences", []):
            pool = list(seq.get("effects") or [])
            for helper in seq.get("helpers") or []:
                pool.extend(helper.get("effects") or [])
            for fx in pool:
                present.add(fx.get("anim"))
                if fx.get("hitanim"):
                    present.add(fx["hitanim"])

        in_atlas = set(flare.get("animations") or {})
        miss = sorted(wanted - present)
        no_anim = [m for m in miss if m not in in_atlas]
        unplaced = [m for m in miss if m in in_atlas]
        print(f"{char:16} erwartet={len(wanted):3} vorhanden={len(wanted & present):3} "
              f"ohne_action={len(no_anim):3} nicht_platziert={unplaced if unplaced else '-'}")


if __name__ == "__main__":
    main(sys.argv[1:])
