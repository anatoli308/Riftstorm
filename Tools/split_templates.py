"""
Read-only splitter for Assets/StreamingAssets/items/_templates.json.

Produces two sibling files without touching the original:

  * _templates_handauthored.json
      Only entries with generated == 0 (or missing). These are the
      hand-authored, narrative / quest / vendor / boss-drop / starter
      items that we actually care about for early progression.

  * _templates_archetypes_seed.json
      One representative example per archetype family (derived from a
      stable name suffix bucket). Intended as a seed pool for the
      future LootSystem so it can roll new item variants by archetype
      instead of pulling from the 16k generated entries.

Invariants:
  * The original _templates.json is read-only here; we only write the
    two new sibling files.
  * Output is sorted by integer entry id so diffs stay stable.
  * Counts and file sizes are printed at the end as a sanity check.

Usage:
  python Tools/split_templates.py
"""

from __future__ import annotations

import json
import os
import sys
from collections import defaultdict


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_PATH = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "items", "_templates.json")
OUT_HAND = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "items", "_templates_handauthored.json")
OUT_SEED = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "items", "_templates_archetypes_seed.json")


def archetype_key(template: dict) -> str:
    """
    Derive a stable family bucket from the template name.

    Generated entries in _templates.json are named like
    "<Adjective> <Noun>" (e.g. "Vincent's Old Sword", "Soulless Coif").
    The last word is a reliable proxy for the equip archetype
    (Sword, Coif, Greaves, Shank, Stave, ...). Falls back to
    "model:<modelId>" if the name is empty.
    """
    name = (template.get("name") or "").strip()
    if not name:
        model = template.get("model")
        return f"model:{model}" if model else "unknown"
    # Strip a trailing roman numeral / digit grade suffix (rare in this dataset
    # but harmless if present): "Coif II" -> "Coif".
    parts = name.split()
    last = parts[-1]
    if last.isdigit() or set(last) <= set("IVXLCM"):
        if len(parts) >= 2:
            last = parts[-2]
    return last


def is_handauthored(template: dict) -> bool:
    """True for entries that are not LootBuilder-generated."""
    generated = template.get("generated", 0)
    try:
        return int(generated) == 0
    except (TypeError, ValueError):
        return False


def main() -> int:
    if not os.path.isfile(SRC_PATH):
        print(f"[split_templates] FATAL: source not found: {SRC_PATH}", file=sys.stderr)
        return 1

    with open(SRC_PATH, "r", encoding="utf-8") as handle:
        data = json.load(handle)

    if not isinstance(data, dict):
        print(f"[split_templates] FATAL: expected top-level dict, got {type(data).__name__}", file=sys.stderr)
        return 1

    handauthored: dict[str, dict] = {}
    by_archetype: dict[str, list[tuple[int, str, dict]]] = defaultdict(list)

    for raw_id, template in data.items():
        if not isinstance(template, dict):
            continue

        if is_handauthored(template):
            handauthored[raw_id] = template

        # Build archetype bucket using ONLY generated entries — handauthored
        # entries are one-offs and not useful as roll archetypes.
        if not is_handauthored(template):
            try:
                entry_id = int(template.get("entry") or raw_id)
            except (TypeError, ValueError):
                entry_id = 0
            bucket = archetype_key(template)
            by_archetype[bucket].append((entry_id, raw_id, template))

    # Pick the lowest-entry-id representative per archetype family. Stable,
    # deterministic, and prefers the "base" variant of an archetype.
    seed_pool: dict[str, dict] = {}
    for bucket, entries in by_archetype.items():
        entries.sort(key=lambda triple: triple[0])
        entry_id, raw_id, template = entries[0]
        seed_pool[raw_id] = template

    # Sort outputs by integer entry id for stable diffs.
    def sort_key(item: tuple[str, dict]) -> int:
        try:
            return int(item[1].get("entry") or item[0])
        except (TypeError, ValueError):
            return 0

    handauthored_sorted = dict(sorted(handauthored.items(), key=sort_key))
    seed_pool_sorted = dict(sorted(seed_pool.items(), key=sort_key))

    with open(OUT_HAND, "w", encoding="utf-8") as handle:
        json.dump(handauthored_sorted, handle, ensure_ascii=False, indent=2)
    with open(OUT_SEED, "w", encoding="utf-8") as handle:
        json.dump(seed_pool_sorted, handle, ensure_ascii=False, indent=2)

    src_size = os.path.getsize(SRC_PATH)
    hand_size = os.path.getsize(OUT_HAND)
    seed_size = os.path.getsize(OUT_SEED)

    total_generated = sum(1 for t in data.values() if isinstance(t, dict) and not is_handauthored(t))
    print(f"[split_templates] source: {len(data)} entries, {src_size/1024:.1f} KB")
    print(f"[split_templates] handauthored: {len(handauthored_sorted)} entries -> {OUT_HAND} ({hand_size/1024:.1f} KB)")
    print(f"[split_templates] generated total: {total_generated} entries across {len(by_archetype)} archetype buckets")
    print(f"[split_templates] archetype seed:  {len(seed_pool_sorted)} entries -> {OUT_SEED} ({seed_size/1024:.1f} KB)")
    print("[split_templates] original _templates.json was NOT modified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
