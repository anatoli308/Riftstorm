"""
Split Assets/StreamingAssets/spells/_templates.json into three files by icon prefix:

- _templates.json : pure spells (icon does NOT start with item_/icon_item_)
- _items.json     : consumables/items (icon starts with item_ or icon_item_,
                    but NOT icon_item_scroll)
- _scrolls.json   : scrolls (icon starts with icon_item_scroll)

Runtime behaviour is unchanged: SpellCatalogLoader merges all three files
into the same SpellTemplate dictionary on first access.

The original _templates.json is backed up to _templates.json.bak (only if no
backup exists yet) before being rewritten. Key order from the source file is
preserved (Python 3.7+ dicts are insertion-ordered).
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SPELLS_DIR = REPO_ROOT / "Assets" / "StreamingAssets" / "spells"
SOURCE = SPELLS_DIR / "_templates.json"
ITEMS_OUT = SPELLS_DIR / "_items.json"
SCROLLS_OUT = SPELLS_DIR / "_scrolls.json"
BACKUP = SPELLS_DIR / "_templates.json.bak"


def classify(icon: str) -> str:
    """Return 'scroll', 'item' or 'spell' based on the icon stem prefix."""
    if not icon:
        return "spell"
    stem = icon.rsplit("/", 1)[-1].lower()
    if stem.startswith("icon_item_scroll"):
        return "scroll"
    if stem.startswith("item_") or stem.startswith("icon_item_"):
        return "item"
    return "spell"


def main() -> None:
    if not SOURCE.exists():
        raise SystemExit(f"Source file not found: {SOURCE}")

    # Idempotenz: liegt bereits ein Backup vor, gilt das als Single Source of
    # Truth. Sonst wuerde ein zweiter Lauf das bereits gefilterte _templates.json
    # einlesen und _items.json / _scrolls.json mit {} ueberschreiben.
    read_from = BACKUP if BACKUP.exists() else SOURCE
    print(f"reading from: {read_from.name}")
    with read_from.open("r", encoding="utf-8") as fh:
        data = json.load(fh)

    if not isinstance(data, dict):
        raise SystemExit("Expected top-level JSON object {entry: template, ...}.")

    spells: dict = {}
    items: dict = {}
    scrolls: dict = {}

    for entry, tpl in data.items():
        icon = tpl.get("icon", "") if isinstance(tpl, dict) else ""
        kind = classify(icon)
        if kind == "scroll":
            scrolls[entry] = tpl
        elif kind == "item":
            items[entry] = tpl
        else:
            spells[entry] = tpl

    if not BACKUP.exists():
        shutil.copy2(SOURCE, BACKUP)
        print(f"backup written: {BACKUP.name}")
    else:
        print(f"backup already exists, leaving untouched: {BACKUP.name}")

    def write(path: Path, payload: dict) -> None:
        with path.open("w", encoding="utf-8", newline="\n") as fh:
            json.dump(payload, fh, ensure_ascii=False, indent=2)
            fh.write("\n")

    write(SOURCE, spells)
    write(ITEMS_OUT, items)
    write(SCROLLS_OUT, scrolls)

    total = len(spells) + len(items) + len(scrolls)
    print(f"source entries:   {len(data)}")
    print(f"  spells:         {len(spells):>5}  -> {SOURCE.name}")
    print(f"  items:          {len(items):>5}  -> {ITEMS_OUT.name}")
    print(f"  scrolls:        {len(scrolls):>5}  -> {SCROLLS_OUT.name}")
    if total != len(data):
        raise SystemExit(f"partition lost entries: {len(data)} -> {total}")


if __name__ == "__main__":
    main()
