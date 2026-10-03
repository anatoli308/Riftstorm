"""Audit: prueft, welche MUGEN-Merkmale im FLARE-Export ankommen und welche fehlen."""
from __future__ import annotations

import collections
import json
import os
import sys

CHAR = sys.argv[1] if len(sys.argv) > 1 else "Truffe"
_HERE = os.path.dirname(os.path.abspath(__file__))


def load(name: str) -> dict:
    with open(os.path.join(_HERE, "out", CHAR, name), encoding="utf-8") as fh:
        return json.load(fh)


flare = load(f"{CHAR}.json")
anims = load("animations.json")
states = load("states.json")

print(f"===== {CHAR} =====")

# --- Animationen ---------------------------------------------------------
air_actions = anims.get("actions") or []
loop_starts = sum(1 for a in air_actions if a.get("loopStart"))
print(f"Actions: {len(air_actions)}, davon mit LoopStart: {loop_starts}")
types = collections.Counter(v.get("type") for v in flare["animations"].values())
print("Exportierte Typen:", dict(types))

# --- Effekte -------------------------------------------------------------
effects = [e for s in flare["sequences"] for e in s["effects"]]
helpers = [h for s in flare["sequences"] for h in (s.get("helpers") or [])]
print(f"\nMoves: {len(flare['sequences'])}")
print(f"  Helfer-Akteure  : {len(helpers)}")
print(f"  Helfer mit Kette: {sum(1 for h in helpers if len(h['anims']) > 1)}")
print(f"  Helfer mit Hit  : {sum(1 for h in helpers if h['hits'])}")
print(f"Effekte: {len(effects)} (Root) + {sum(len(h['effects']) for h in helpers)} (Helfer)")
print("  mit Geschwindigkeit:", sum(1 for e in effects if e["vel"] != [0.0, 0.0]))
print("  mit Beschleunigung :", sum(1 for e in effects if e["accel"] != [0.0, 0.0]))
print("  postype            :", dict(collections.Counter(e["postype"] for e in effects)))

# --- Projectiles ---------------------------------------------------------
proj = [p for s in states["states"] for c in (s.get("controllers") or [])
        for p in [c.get("params") or {}]
        if (p.get("type") or "").lower() == "projectile"]
print(f"\nProjectile-Controller im CNS: {len(proj)}")
for p in proj[:4]:
    print("   ", {k: v for k, v in p.items()
                 if k.lower() in ("projanim", "velocity", "accel", "offset", "postype", "projid")})

# Controller, deren Wirkung im FLARE-Export ankommt.
handled = {
    "changeanim", "playsnd", "explod", "projectile", "helper",
    "palfx", "allpalfx", "superpause", "pause", "velset", "veladd", "velmul",
    "posset", "posadd", "hitdef", "afterimage", "afterimagetime", "modifyexplod",
    "removeexplod", "changestate", "selfstate", "trans",
    "changeanim2", "sprpriority", "angledraw", "angleset", "angleadd",
}
# Controller ohne sichtbare Wirkung im Showcase (Logik, Ressourcen, KI).
invisible = {
    "destroyself", "null", "varset", "varadd", "varrandom", "parentvarset",
    "parentvaradd", "assertspecial", "nothitby", "hitby", "ctrlset",
    "statetypeset", "poweradd", "powerset", "lifeadd", "targetstate",
    "hitoverride", "attackdist", "attackmulset", "screenbound", "bindtoroot",
    "bindtoparent", "turn", "forcefeedback", "playerpush", "hitvelset",
    "hitadd", "targetlifeadd", "reversaldef", "defencemulset", "posfreeze",
    "targetbind", "makedust", "envcolor", "sndpan", "stopsnd", "gamemakeanim",
}
missing: collections.Counter[str] = collections.Counter()
for state in states["states"]:
    for ctrl in state.get("controllers") or []:
        ctype = ((ctrl.get("params") or {}).get("type") or "").lower()
        if ctype and ctype not in handled and ctype not in invisible:
            missing[ctype] += 1
print("\nSichtbar wirksam, aber noch NICHT portiert:", dict(missing.most_common(12)))

used = collections.Counter()
for state in states["states"]:
    for ctrl in state.get("controllers") or []:
        ctype = ((ctrl.get("params") or {}).get("type") or "").lower()
        if ctype in handled:
            used[ctype] += 1
print("Portiert und im Char benutzt:", dict(used.most_common(14)))
