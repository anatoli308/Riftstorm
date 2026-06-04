"""Fuellt alle NPC-Templates mit player-vergleichbaren Stat-Werten.

Hintergrund: Die migrierte npc/_templates.json hat fast ueberall leere
Attribute (0) und Sentinels (-1). Dadurch konnten Caster kein Mana ableiten
(intellect=0) und alle Melee-NPCs machten identischen Schaden (weapon_value
defaultet auf 10, strength=0).

Dieses Skript setzt fuer alle 328 NPCs konsistente Werte anhand eines
Archetyps (Caster / Ranged / Melee), abgeleitet aus den Spieler-Klassenstats
(player_class_stats): Attribute sind - wie beim Spieler - flach pro Archetyp,
HP/Mana skalieren pro Level. Elite/Boss bekommen den im Spawner ueblichen
HP-Multiplikator (x3 / x10) direkt eingebacken sowie hoeheren Waffenwert.

Reines Daten-Skript, einmalig auszufuehren.
"""
import json

PATH = r"d:/Riftstorm/Assets/StreamingAssets/npc/_templates.json"

# Archetyp-Profile, abgeleitet aus player_class_stats (Level-1-Werte sind
# flach ueber alle Level; nur HP/Mana wachsen pro Level).
#   melee  -> Krieger  (Class 1): HP 75/Lvl, Mana 30/Lvl
#   caster -> Magier   (Class 2): HP 40/Lvl, Mana 70/Lvl
#   ranged -> Jaeger   (Class 3): HP 60/Lvl, Mana 45/Lvl
PROFILES = {
    "melee":  {"hp": 75, "mana": 30, "strength": 15, "agility": 10,
               "willpower": 5,  "intellect": 5,  "courage": 15,
               "armor_per": 4, "wv_base": 8, "wv_per": 2, "skill_per": 5},
    "caster": {"hp": 40, "mana": 70, "strength": 5,  "agility": 5,
               "willpower": 15, "intellect": 20, "courage": 10,
               "armor_per": 3, "wv_base": 4, "wv_per": 1, "skill_per": 3},
    "ranged": {"hp": 60, "mana": 45, "strength": 10, "agility": 15,
               "willpower": 10, "intellect": 10, "courage": 10,
               "armor_per": 3, "wv_base": 6, "wv_per": 2, "skill_per": 5},
}


def is_dedicated_caster(entry):
    """True nur fuer dedizierte Zauberer.

    Massgeblich ist das spieleigene Flag ``spell_primary`` (ersetzt den
    Auto-Angriff durch einen Zauber). Mobs, die lediglich eine Slot-
    Faehigkeit (z.B. ein Heulen) besitzen, bleiben Nahkaempfer.
    """
    return (entry.get("spell_primary") or 0) > 0


def has_any_spell(entry):
    """True, wenn der NPC irgendeinen Zauber wirken kann (Primaer oder Slot)."""
    if (entry.get("spell_primary") or 0) > 0:
        return True
    for key, val in entry.items():
        if key.startswith("spell_") and key.endswith("_id") and isinstance(val, int) and val > 0:
            return True
    return False


def is_ranged(entry):
    """True fuer Fernkaempfer ohne Zauber (Bogen/Wurf)."""
    return (entry.get("ranged_weapon_value") or 0) > 0 or (entry.get("ranged_skill") or 0) > 0


def archetype_of(entry):
    """Bestimmt den Archetyp: Caster vor Ranged vor Melee."""
    if is_dedicated_caster(entry):
        return "caster"
    if is_ranged(entry):
        return "ranged"
    return "melee"


def representative_level(entry):
    """Repraesentatives Level fuer HP/Mana (Attribute sind levelunabhaengig)."""
    lo = entry.get("min_level") or 1
    hi = entry.get("max_level") or lo
    return max(1, (lo + hi + 1) // 2)


def is_truthy(entry, key):
    """Robuster Wahrheitstest fuer bool_elite / bool_boss (kann None/0/1 sein)."""
    return bool(entry.get(key))


def fill_entry(entry):
    """Setzt alle Stat-Felder eines NPC anhand seines Archetyps neu."""
    arch = archetype_of(entry)
    profile = PROFILES[arch]
    level = representative_level(entry)
    boss = is_truthy(entry, "bool_boss")
    elite = is_truthy(entry, "bool_elite")

    hp_mult = 10 if boss else (3 if elite else 1)
    combat_mult = 2.0 if boss else (1.5 if elite else 1.0)

    # Attribute: flach wie beim Spieler (kein Elite/Boss-Aufschlag).
    entry["strength"] = profile["strength"]
    entry["agility"] = profile["agility"]
    entry["intellect"] = profile["intellect"]
    entry["willpower"] = profile["willpower"]
    entry["courage"] = profile["courage"]

    # HP: player-gleiche Basis pro Level, Elite/Boss-Multiplikator eingebacken.
    entry["health"] = int(round(profile["hp"] * level)) * hp_mult

    # Mana: player-gleiche Basis pro Level. Nahkaempfer mit Utility-Zaubern
    # erhalten zusaetzlich einen Mindestpool, damit ihre Faehigkeiten wirken.
    mana = int(round(profile["mana"] * level))
    if has_any_spell(entry):
        mana = max(mana, 50 + level * 5)
    entry["mana"] = mana

    # Ruestung: level-skaliert, Elite/Boss zaeher.
    entry["armor"] = int(round(profile["armor_per"] * level * combat_mult))

    # Waffenwert: treibt den Nahkampfschaden; level- und archetyp-abhaengig
    # plus Elite/Boss-Aufschlag -> sorgt fuer Schadensvariation.
    weapon_value = int(round((profile["wv_base"] + profile["wv_per"] * level) * combat_mult))
    entry["weapon_value"] = weapon_value
    if "ranged_weapon_value" in entry:
        entry["ranged_weapon_value"] = weapon_value if arch == "ranged" else 0

    # Waffenfertigkeiten (aktuell vom C#-Combat nicht genutzt, aber
    # konsistent zum Spieler gesetzt).
    entry["melee_skill"] = int(round(profile["skill_per"] * level))
    entry["ranged_skill"] = int(round(profile["skill_per"] * level)) if arch == "ranged" else 0
    if "shield_skill" in entry:
        entry["shield_skill"] = 5 * level


def main():
    with open(PATH, encoding="utf-8") as handle:
        data = json.load(handle)

    counts = {"melee": 0, "caster": 0, "ranged": 0}
    for entry in data.values():
        counts[archetype_of(entry)] += 1
        fill_entry(entry)

    with open(PATH, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    print("Aktualisiert:", len(data), "NPCs")
    print("Archetypen:", counts)
    for entry_id in ("15", "237", "1"):
        if entry_id in data:
            v = data[entry_id]
            print(f"  [{entry_id}] {v.get('name')}: lvl~{representative_level(v)} "
                  f"str={v['strength']} int={v['intellect']} hp={v['health']} "
                  f"mana={v['mana']} armor={v['armor']} wv={v['weapon_value']}")


if __name__ == "__main__":
    main()
