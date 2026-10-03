"""Gibt konvertierte Sequenzen als sortierte Tick-Timeline aus."""
from __future__ import annotations

import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
WANT = int(sys.argv[2]) if len(sys.argv) > 2 else None

_HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(_HERE, "out", CHAR, f"{CHAR}.json"), encoding="utf-8") as fh:
    doc = json.load(fh)

seqs = doc.get("sequences") or []
if WANT is None:
    def spread(seq: dict) -> int:
        ticks = [fx.get("time", 0) for fx in (seq.get("effects") or [])]
        ticks += [an.get("time", 0) for an in (seq.get("anims") or [])]
        return max(ticks) - min(ticks) if ticks else 0

    seqs = sorted(seqs, key=spread, reverse=True)[:4]
else:
    seqs = [s for s in seqs if s.get("state") == WANT]

for seq in seqs:
    print(f"=== state {seq.get('state')}  {seq.get('label')} ===")
    events: list[tuple[int, str]] = []
    for an in seq.get("anims") or []:
        events.append((an.get("time", 0), f"BODY  {an.get('anim')}"))
    for fx in seq.get("effects") or []:
        events.append((
            fx.get("time", 0),
            "FX    {a:<20} pos={p} type={t:<5} prio={r:<4} rm={m:<5} period={e}".format(
                a=fx.get("anim"), p=fx.get("pos"), t=fx.get("postype"),
                r=fx.get("sprpriority"), m=fx.get("removetime"), e=fx.get("period", 0))))
    for tick, line in sorted(events, key=lambda ev: ev[0]):
        print(f"  t={tick:<5} {line}")
    print()
