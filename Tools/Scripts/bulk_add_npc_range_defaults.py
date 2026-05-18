"""One-shot bulk-editor: add aggro_range/melee_range/leash_range/move_speed
sentinel defaults to every entry in Assets/StreamingAssets/npc/_templates.json.

Sentinel convention (mirrors existing fields):
    aggro_range / melee_range / leash_range : -1  => "use source/inspector default"
    move_speed                                : 0  => "use spawner default"

NpcController.BindTemplate treats <=0 as "fall through to Inspector default",
so existing balancing is unchanged after this rewrite.
"""
from __future__ import annotations

import json
from pathlib import Path

PATH = Path(r"d:\Riftstorm\Assets\StreamingAssets\npc\_templates.json")


def main() -> None:
    data = json.loads(PATH.read_text(encoding="utf-8"))
    touched = 0
    for entry in data.values():
        changed = False
        if "aggro_range" not in entry:
            entry["aggro_range"] = -1
            changed = True
        if "melee_range" not in entry:
            entry["melee_range"] = -1
            changed = True
        if "leash_range" not in entry:
            entry["leash_range"] = -1
            changed = True
        if "move_speed" not in entry:
            entry["move_speed"] = 0
            changed = True
        if changed:
            touched += 1

    PATH.write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"entries_total={len(data)} entries_touched={touched}")


if __name__ == "__main__":
    main()
