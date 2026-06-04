"""Einmalige Analyse von npc/_templates.json: Verteilung von Leveln, Typen,
Caster-Anteil und vorhandenen Stat-Werten. Reine Read-only-Diagnose."""
import collections
import json

PATH = r"d:/Riftstorm/Assets/StreamingAssets/npc/_templates.json"

with open(PATH, encoding="utf-8") as handle:
    data = json.load(handle)

print("NPCs gesamt:", len(data))

levels = [v.get("min_level", 0) for v in data.values()]
print("min_level min/max:", min(levels), max(levels))

types = collections.Counter(v.get("type") for v in data.values())
print("type-Verteilung:", dict(types))

elite = sum(1 for v in data.values() if v.get("bool_elite", 0))
boss = sum(1 for v in data.values() if v.get("bool_boss", 0))
print("elite:", elite, "boss:", boss)


def has_spells(entry):
    if entry.get("spell_primary", 0) > 0:
        return True
    for key, val in entry.items():
        if key.startswith("spell_") and key.endswith("_id") and isinstance(val, int) and val > 0:
            return True
    return False


casters = [k for k, v in data.items() if has_spells(v)]
print("NPCs mit Spells:", len(casters))

# Wie viele Caster haben intellect <= 0 (koennen also kein Mana ableiten)?
caster_no_int = [k for k in casters if data[k].get("intellect", 0) <= 0]
print("davon intellect<=0:", len(caster_no_int))

# Stat-Befund: wie viele haben ueberhaupt Attribute > 0?
attrs = ("strength", "agility", "intellect", "willpower", "courage")
any_attr = sum(1 for v in data.values() if any(v.get(a, 0) > 0 for a in attrs))
print("NPCs mit irgendeinem Attribut>0:", any_attr)

wv = sum(1 for v in data.values() if v.get("weapon_value", -1) > 0)
print("NPCs mit weapon_value>0:", wv)

# Felder-Inventar (welche Keys kommen vor)
keys = collections.Counter()
for v in data.values():
    keys.update(v.keys())
print("\nFeld-Haeufigkeit (Top):")
for key, count in keys.most_common():
    print(f"  {key}: {count}")
