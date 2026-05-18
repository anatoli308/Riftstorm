"""One-shot field inventory for spells/_templates.json."""
from __future__ import annotations

import json
from pathlib import Path

PATH = Path(r"d:\Riftstorm\Assets\StreamingAssets\spells\_templates.json")


def main() -> None:
    data = json.loads(PATH.read_text(encoding="utf-8"))
    keys: dict[str, int] = {}
    for entry in data.values():
        for k in entry.keys():
            keys[k] = keys.get(k, 0) + 1
    print(f"TOTAL_ENTRIES = {len(data)}")
    print(f"UNIQUE_KEYS   = {len(keys)}")
    for k in sorted(keys):
        print(f"  {k:35s}  {keys[k]:5d}")


if __name__ == "__main__":
    main()
