"""Read-only-Inspektion fuer das Stat-Fill-Modell.

Ermittelt Level-Ranges, Archetyp-Signale (Caster/Ranged/Melee) und die
exakten Feldnamen, damit das Fill-Skript korrekt schreibt.
"""
import collections
import json

PATH = r"d:/Riftstorm/Assets/StreamingAssets/npc/_templates.json"

with open(PATH, encoding="utf-8") as handle:
    data = json.load(handle)

print("NPCs:", len(data))

# Level-Range-Verteilung
diff = sum(1 for v in data.values() if v.get("min_level") != v.get("max_level"))
print("min_level != max_level:", diff)
print("min_level range:", min(v.get("min_level", 0) for v in data.values()),
      max(v.get("min_level", 0) for v in data.values()))
print("max_level range:", min(v.get("max_level", 0) for v in data.values()),
      max(v.get("max_level", 0) for v in data.values()))


def has_spells(entry):
    if entry.get("spell_primary", 0) and entry.get("spell_primary", 0) > 0:
        return True
    for key, val in entry.items():
        if key.startswith("spell_") and key.endswith("_id") and isinstance(val, int) and val > 0:
            return True
    return False


def is_ranged(entry):
    return (entry.get("ranged_weapon_value", 0) or 0) > 0 or (entry.get("ranged_skill", 0) or 0) > 0


casters = [v for v in data.values() if has_spells(v)]
ranged = [v for v in data.values() if is_ranged(v) and not has_spells(v)]
melee = [v for v in data.values() if not has_spells(v) and not is_ranged(v)]
print("Archetyp -> Caster:", len(casters), "Ranged:", len(ranged), "Melee:", len(melee))

# Welche Spell-Slot-Keys existieren ueberhaupt?
spell_keys = collections.Counter()
for v in data.values():
    for k in v:
        if k.startswith("spell_"):
            spell_keys[k] += 1
print("Spell-Keys:", dict(spell_keys.most_common()))

# Alle vorkommenden Felder + Haeufigkeit
keys = collections.Counter()
for v in data.values():
    keys.update(v.keys())
print("\nFelder (alle):")
for key, count in keys.most_common():
    print(f"  {key}: {count}")

# Beispiel: Battle Mage (15) und Tasloi Mage (237)
for entry_id in ("15", "237"):
    if entry_id in data:
        v = data[entry_id]
        print(f"\n--- entry {entry_id} {v.get('name')} ---")
        for k in ("min_level", "max_level", "type", "bool_elite", "bool_boss",
                  "strength", "agility", "intellect", "willpower", "courage",
                  "armor", "health", "mana", "weapon_value", "melee_skill",
                  "ranged_skill", "ranged_weapon_value", "melee_speed",
                  "spell_primary"):
            print(f"  {k}: {v.get(k)}")
