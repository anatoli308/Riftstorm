"""
Inspect steam-main game.db files: list all tables, schemas, row counts, and
sample rows. Used to plan the Riftstorm JSON migration (npc_template,
spell_template, item_template, quest_template, gameobject_template, etc.).

Run: python inspect_game_db.py [--samples N] [--full]
"""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import sys
from typing import Iterable

CANDIDATE_DBS = [
    r"c:\Users\anato\Downloads\steam-main\game.db",
    r"c:\Users\anato\Downloads\steam-main\source_server\game\game.db",
    r"c:\Users\anato\Downloads\steam-main\source_server\Server\data\server.db",
]

# Tables the user is interested in (printed first, sampled deeper).
PRIORITY_TABLES = [
    "npc_template", "npc_models", "npc_sounds",
    "spell_template", "spell_visual", "spell_visual_kit",
    "item_template", "item_gems", "affix_template",
    "gameobject_template", "gameobject_models", "gameobject",
    "quest_template",
    "gossip", "gossip_option",
    "zone_template", "area_template", "map",
    "player_exp_levels",
    "scripts", "loot",
    "npc_vendor", "npc_vendor_random",
    "teleport_names",
]


def _safe(v):
    if isinstance(v, bytes):
        return f"<bytes len={len(v)}>"
    return v


def dump_table(cur: sqlite3.Cursor, table: str, samples: int) -> None:
    try:
        cnt = cur.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
    except sqlite3.Error as exc:
        print(f"  ! cannot count {table}: {exc}")
        return

    print(f"\n  === {table}  rows={cnt} ===")

    cols = list(cur.execute(f'PRAGMA table_info("{table}")'))
    for c in cols:
        cid, name, ctype, notnull, dflt, pk = c
        flags = []
        if pk:
            flags.append("PK")
        if notnull:
            flags.append("NOT NULL")
        flag_s = f" [{', '.join(flags)}]" if flags else ""
        dflt_s = f" default={dflt}" if dflt is not None else ""
        print(f"    {cid:>3} {name:<28} {ctype:<14}{dflt_s}{flag_s}")

    if cnt == 0 or samples <= 0:
        return

    cur.execute(f'SELECT * FROM "{table}" LIMIT ?', (samples,))
    rows = cur.fetchall()
    col_names = [d[0] for d in cur.description]
    print(f"  --- sample ({len(rows)}) ---")
    for r in rows:
        obj = {n: _safe(v) for n, v in zip(col_names, r)}
        print("    " + json.dumps(obj, default=str, ensure_ascii=False))


def inspect_db(path: str, samples: int, full: bool) -> None:
    print("=" * 78)
    print(f"DB: {path}")
    if not os.path.isfile(path):
        print("  (file not found, skipping)")
        return
    print(f"  size: {os.path.getsize(path):,} bytes")

    db = sqlite3.connect(path)
    try:
        cur = db.cursor()
        all_tables = [
            r[0]
            for r in cur.execute(
                "SELECT name FROM sqlite_master "
                "WHERE type='table' AND name NOT LIKE 'sqlite_%' "
                "ORDER BY name"
            )
        ]
        print(f"  total tables: {len(all_tables)}")
        print("  tables: " + ", ".join(all_tables))

        priority_present = [t for t in PRIORITY_TABLES if t in all_tables]
        other = [t for t in all_tables if t not in PRIORITY_TABLES]

        print("\n[PRIORITY TABLES]")
        for t in priority_present:
            dump_table(cur, t, samples)

        if full:
            print("\n[OTHER TABLES]")
            for t in other:
                dump_table(cur, t, max(1, samples // 2))
        else:
            print("\n[OTHER TABLES — row counts only]")
            for t in other:
                try:
                    cnt = cur.execute(f'SELECT COUNT(*) FROM "{t}"').fetchone()[0]
                    print(f"  {t:<32} rows={cnt}")
                except sqlite3.Error as exc:
                    print(f"  {t:<32} ! {exc}")
    finally:
        db.close()


def main(argv: Iterable[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--samples", type=int, default=3,
                   help="rows to sample per priority table (default 3)")
    p.add_argument("--full", action="store_true",
                   help="also dump schema + samples for non-priority tables")
    p.add_argument("--dbs", nargs="*", default=None,
                   help="override candidate DB paths")
    args = p.parse_args(list(argv))

    targets = args.dbs if args.dbs else CANDIDATE_DBS
    for path in targets:
        inspect_db(path, args.samples, args.full)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
