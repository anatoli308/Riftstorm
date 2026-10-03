"""Zaehlt exportierte Physikwechsel je Charakter."""
from __future__ import annotations

import json
from pathlib import Path

OUT = Path(__file__).resolve().parent / "out"

total_seq = 0
total_entries = 0
for char_dir in sorted(OUT.iterdir()):
    atlas = char_dir / f"{char_dir.name}.json"
    if not atlas.is_file():
        continue
    sequences = json.loads(atlas.read_text("utf-8")).get("sequences") or []
    seqs = [s for s in sequences if s.get("physicschanges")]
    entries = sum(len(s["physicschanges"]) for s in seqs)
    total_seq += len(seqs)
    total_entries += entries
    if seqs:
        print(f"{char_dir.name:16s} {len(seqs):3d} Sequenzen  {entries:3d} Wechsel")
print(f"\ngesamt {total_seq} Sequenzen, {total_entries} Wechsel")
