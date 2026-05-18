"""
migrate_game_db.py — Convert steam-source game.db into Riftstorm StreamingAssets JSON.

Source DB:   c:\\Users\\anato\\Downloads\\steam-main\\game.db   (or --db PATH)
Target root: d:\\Riftstorm\\Assets\\StreamingAssets\\               (or --out PATH)

For every table listed in TABLE_PLAN, this script:
  - SELECT * FROM <table>
  - drops NULL / empty-string fields per row (keeps source-faithful snake_case names)
  - emits JSON either as { pk_value: row, ... } (when key is set) or as [row, row, ...]

Add or change tables by editing TABLE_PLAN. No per-table code paths.
"""

from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Iterable

DEFAULT_DB = Path(r"c:\Users\anato\Downloads\steam-main\game.db")
DEFAULT_OUT = Path(r"d:\Riftstorm\Assets\StreamingAssets")


# (table_name, output_relpath, key_columns_or_None)
# key=None  -> emit list
# key=str   -> emit dict keyed by that column (cast to str)
# key=tuple -> emit dict keyed by "_".join(str(col) for col in key)
TABLE_PLAN: list[tuple[str, str, Any]] = [
    # --- NPC ---
    ("npc_template",                 "npc/_templates.json",                  "entry"),
    ("npc_models",                   "npc/_models.json",                     "id"),
    ("npc_sounds",                   "npc/_sounds.json",                     None),
    ("npc",                          "npc/_spawns.json",                     "guid"),
    ("npc_models_junkloot",          "npc/_models_junkloot.json",            None),
    ("npc_announcer",                "npc/_announcer.json",                  None),

    # --- Spells ---
    ("spell_template",               "spells/_templates.json",               "entry"),
    ("spell_visual",                 "spells/_visuals.json",                 "entry"),
    ("spell_visual_kit",             "spells/_visual_kits.json",             "id"),

    # --- Items / Loot ---
    ("item_template",                "items/_templates.json",                "entry"),
    ("item_dictionary",              "items/_dictionary.json",               None),
    ("item_gems",                    "items/_gems.json",                     "gem_id"),
    ("item_orbs",                    "items/_orbs.json",                     "item_entry"),
    ("item_potions_worldloot",       "items/_potions_worldloot.json",        "item_entry"),
    ("affix_template",               "items/_affixes.json",                  "entry"),
    ("loot",                         "items/_loot.json",                     "entry"),
    ("material_chance_armor",        "items/_material_chance_armor.json",    None),
    ("material_chance_weapon",       "items/_material_chance_weapon.json",   None),

    # --- Gameobjects ---
    ("gameobject_template",          "gameobjects/_templates.json",          "entry"),
    ("gameobject_models",            "gameobjects/_models.json",             "id"),
    ("gameobject",                   "gameobjects/_spawns.json",             "guid"),

    # --- World ---
    ("map",                          "world/_maps.json",                     "id"),
    ("zone_template",                "world/_zones.json",                    "id"),
    ("area_template",                "world/_areas.json",                    "id"),
    ("teleport_names",               "world/_teleports.json",                "entry"),
    ("dungeon_template",             "world/_dungeons.json",                 "map"),

    # --- Quests / Dialog ---
    ("quest_template",               "quests/_templates.json",               "entry"),
    ("gossip",                       "quests/_gossip.json",                  "entry"),
    ("gossip_option",                "quests/_gossip_options.json",          "entry"),
    ("scripts",                      "quests/_scripts.json",                 None),
    ("world_texts",                  "quests/_world_texts.json",             "id"),

    # --- Player ---
    ("player_exp_levels",            "player/_exp_levels.json",              None),
    ("player_class_stats",           "player/_class_stats.json",             None),
    ("player_create_item",           "player/_create_items.json",            None),
    ("player_create_spell",          "player/_create_spells.json",           None),
    ("player_create_known_waypoints","player/_create_known_waypoints.json",  "entry"),
    ("player_desirable_armor",       "player/_desirable_armor.json",         None),
    ("player_desirable_stats",       "player/_desirable_stats.json",         None),

    # --- Vendors / Crafting ---
    ("npc_vendor",                   "vendors/_static.json",                 "entry"),
    ("npc_vendor_random",            "vendors/_random.json",                 "entry"),
    ("crafting_recipes",             "vendors/_crafting_recipes.json",       "entry"),
    ("combine_items",                "vendors/_combine_items.json",          None),

    # --- Misc / Sprites ---
    ("model_info",                   "misc/_model_info.json",                None),
    ("sprite_hotspot",               "misc/_sprite_hotspot.json",            "filename"),
    ("sprite_light",                 "misc/_sprite_light.json",              "filename"),
    ("sprite_psi",                   "misc/_sprite_psi.json",                None),
    ("sprite_proximity_sound",       "misc/_sprite_proximity_sound.json",    None),
    ("arena_template",               "misc/_arena_template.json",            None),
]

# Tables we deliberately skip (all currently empty in source).
SKIPPED_TABLES = {
    "dialog", "graveyard", "npc_ai", "npc_groups", "npc_waypoints",
    "reserved_names", "spell_conditions", "spell_conditions_target",
}


def is_empty(value: Any) -> bool:
    """Treat NULL and empty string as 'not present'. Keep 0 and 0.0 as real data."""
    return value is None or (isinstance(value, str) and value == "")


def clean_row(row: sqlite3.Row) -> dict[str, Any]:
    out: dict[str, Any] = {}
    for key in row.keys():
        v = row[key]
        if is_empty(v):
            continue
        if isinstance(v, bytes):
            # Source-faithful: emit length marker; no blob columns expected in priority tables.
            out[key] = f"<bytes len={len(v)}>"
        else:
            out[key] = v
    return out


def make_key(row: dict[str, Any], key_spec: Any) -> str:
    if isinstance(key_spec, tuple):
        return "_".join(str(row.get(col, "")) for col in key_spec)
    return str(row.get(key_spec, ""))


def migrate_table(
    conn: sqlite3.Connection,
    table: str,
    out_path: Path,
    key_spec: Any,
) -> tuple[int, str]:
    cursor = conn.execute(f"SELECT * FROM {table}")
    rows = [clean_row(r) for r in cursor.fetchall()]

    out_path.parent.mkdir(parents=True, exist_ok=True)

    if key_spec is None:
        payload: Iterable[Any] = rows
        mode = "list"
    else:
        keyed: dict[str, dict[str, Any]] = {}
        for row in rows:
            key = make_key(row, key_spec)
            if key == "" or key == "None":
                # row missing PK — fall back to deterministic index key
                key = f"__row_{len(keyed)}"
            if key in keyed:
                # composite duplicates: append suffix
                i = 2
                while f"{key}#{i}" in keyed:
                    i += 1
                key = f"{key}#{i}"
            keyed[key] = row
        payload = keyed
        mode = "dict"

    with out_path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
        f.write("\n")

    return len(rows), mode


def list_actual_tables(conn: sqlite3.Connection) -> set[str]:
    return {
        r[0] for r in conn.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
        )
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", type=Path, default=DEFAULT_DB,
                    help=f"Path to source game.db (default: {DEFAULT_DB})")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT,
                    help=f"Output root, typically StreamingAssets (default: {DEFAULT_OUT})")
    ap.add_argument("--dry-run", action="store_true",
                    help="Inspect counts only, do not write files.")
    args = ap.parse_args()

    db_path: Path = args.db
    out_root: Path = args.out

    if not db_path.exists():
        print(f"ERROR: database not found: {db_path}", file=sys.stderr)
        return 2

    print(f"DB:  {db_path}")
    print(f"OUT: {out_root}")
    if args.dry_run:
        print("(dry-run — no files will be written)")
    print()

    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row

    actual = list_actual_tables(conn)
    planned = {name for (name, _, _) in TABLE_PLAN}
    unplanned = actual - planned - SKIPPED_TABLES
    missing = planned - actual

    if missing:
        print(f"WARN: planned tables not found in DB: {sorted(missing)}")
    if unplanned:
        print(f"INFO: tables present in DB but not in TABLE_PLAN (left as-is): "
              f"{sorted(unplanned)}")
    print()

    total_rows = 0
    written = 0
    for table, relpath, key_spec in TABLE_PLAN:
        if table not in actual:
            print(f"  SKIP  {table:32s}  (not in DB)")
            continue
        out_path = out_root / Path(relpath)
        if args.dry_run:
            count = conn.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
            mode = "list" if key_spec is None else "dict"
            print(f"  PLAN  {table:32s}  rows={count:>6d}  mode={mode:4s}  -> {relpath}")
            total_rows += count
            continue
        count, mode = migrate_table(conn, table, out_path, key_spec)
        print(f"  OK    {table:32s}  rows={count:>6d}  mode={mode:4s}  -> {relpath}")
        total_rows += count
        written += 1

    conn.close()
    print()
    print(f"Done. Tables processed: {len(TABLE_PLAN)}  written: {written}  total rows: {total_rows}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
