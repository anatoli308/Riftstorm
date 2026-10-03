"""Verteilung der Sequenz-Felder ueber alle konvertierten Chars (Diagnose)."""
from __future__ import annotations

import collections
import json
import os
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

_HERE = os.path.dirname(os.path.abspath(__file__))
out_root = os.path.join(_HERE, "out")

postypes = collections.Counter()
removetimes = collections.Counter()
prios = collections.Counter()
fx_times = collections.Counter()
body_len = collections.Counter()
total_fx = 0
periodic = 0

for char in sorted(os.listdir(out_root)):
    path = os.path.join(out_root, char, f"{char}.json")
    if not os.path.isfile(path):
        continue
    with open(path, encoding="utf-8") as fh:
        doc = json.load(fh)
    seqs = doc.get("sequences") or []
    nonzero = 0
    for seq in seqs:
        body_len[len(seq.get("anims") or [])] += 1
        for fx in seq.get("effects") or []:
            total_fx += 1
            if fx.get("period"):
                periodic += 1
            postypes[fx.get("postype")] += 1
            removetimes[fx.get("removetime")] += 1
            prios[fx.get("sprpriority")] += 1
            t = fx.get("time") or 0
            fx_times[0 if t == 0 else (1 if t <= 10 else 2)] += 1
            if t:
                nonzero += 1
    print(f"{char:12s} seqs={len(seqs):4d}  fx-mit-tick>0={nonzero}")

print("\npostype   :", dict(postypes))
print("removetime:", dict(removetimes.most_common(8)))
print("sprprio   :", dict(prios.most_common(8)))
print("fx-ticks  : 0er=%d  1-10=%d  >10=%d  (gesamt %d)"
      % (fx_times[0], fx_times[1], fx_times[2], total_fx))
print("body-len  :", dict(sorted(body_len.items())))
print("periodisch:", periodic, "von", total_fx)
