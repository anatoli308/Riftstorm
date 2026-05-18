"""
GenerateSpellsJson.py
=====================

Erzeugt `Assets/StreamingAssets/spells/spells.json` aus der Icon-Liste unter
`Assets/Art/spell_icons/`. Klassifiziert jeden Spell per Name-Heuristik in
School + Default-Effekt, damit die Catalog-Pipeline (SpellCatalogLoader →
SpellExecutor) ein realistisches Set an Spells laden kann. Werte sind
Platzhalter-Balance — Feintuning kommt später aus echten DB-Dumps.

Aufruf:
    python Tools/GenerateSpellsJson.py
"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Dict, Any

REPO_ROOT = Path(__file__).resolve().parents[1]
ICONS_DIR = REPO_ROOT / "Assets" / "Art" / "spell_icons"
OUT_PATH = REPO_ROOT / "Assets" / "StreamingAssets" / "spells" / "spells.json"

# ---- Filter -----------------------------------------------------------------
# Nur Icons mit Großbuchstaben-Anfang sind echte Spell-Namen. Alles in
# snake_case / mit Prefix "cbt_", "live_", "icon_" sind technische Sub-Assets
# (Grade-Varianten, UI-Aktionen) und werden hier ignoriert.
def is_spell_icon(name: str) -> bool:
    if not name or not name[0].isupper():
        return False
    if name.startswith("icon_"):
        return False
    return True


def to_id(name: str) -> str:
    s = name.lower()
    s = s.replace("'", "").replace("’", "")
    s = re.sub(r"[\s\-]+", "_", s)
    s = re.sub(r"[^a-z0-9_]", "", s)
    return s


# ---- Heuristik --------------------------------------------------------------

KW_HEAL = ("heal", "renew", "lay on hands", "flash of light", "holy light",
           "redemption", "desperate prayer")
KW_FIRE = ("fire", "fireball", "scorch", "inferno", "flame", "explosive arrow")
KW_FROST = ("frost", "frostbolt", "icebolt", "frozen", "ice")
KW_HOLY_DMG = ("smite", "holy bolt", "holy vengeance", "hammer of justice",
               "crusader", "divine storm", "hallowed", "mind blast")
KW_SHADOW = ("shadow", "plague")
KW_CC_HARD = ("polymorph", "sleep arrow", "sap", "hammer of justice",
              "kidney shot", "cheap shot", "stunning shot", "intimidating shout",
              "psychic scream", "eye gouge")
KW_BUFF = ("battle shout", "recklessness", "shield block", "shield wall",
           "inner fire", "devotion aura", "retribution aura", "salvation aura",
           "righteous fury", "blessing of", "devotion", "fortify intellect",
           "boon of", "amplify magic", "dampen magic", "fear ward", "fire ward",
           "frost ward", "frozen armor", "pain suppression", "divine shield",
           "blessed shield", "lumiel", "stealth", "sprint", "evasion",
           "focused evasion", "misdirection")
KW_WEAPON = ("heroic strike", "cleave", "overpower", "sunder armor",
             "sinister strike", "rend", "hamstring", "whirlwind",
             "thunder clap", "revenge", "viper sting", "aimed shot",
             "arrow flurry", "auto shot", "auto attack", "multi-shot",
             "entangling shot", "counter attack", "demoralizing shout",
             "disarm", "charge", "taunt", "counter spell")
KW_DISPEL = ("cleanse", "dispel magic", "remove curse")
KW_TELEPORT = ("teleport", "illusion gate")
KW_MANA = ("mana burn",)
KW_RESURRECT = ("resurrection", "reincarnation")


@dataclass
class SpellTemplate:
    school: str = "Physical"
    cast_time_ms: int = 0
    cooldown_ms: int = 0
    gcd_ms: int = 1500
    range_: float = 0.0
    projectile_speed: float = 0.0
    mana_cost: int = 0
    description: str = ""
    effects: List[Dict[str, Any]] = field(default_factory=list)


def has_any(name: str, kws) -> bool:
    n = name.lower()
    return any(k in n for k in kws)


def classify(name: str) -> SpellTemplate:
    n = name.lower()
    t = SpellTemplate()

    # ---- Hard CC (Stuns/Sleep/Poly/Sap) ------------------------------------
    if has_any(name, KW_CC_HARD):
        t.school = "Physical" if "shot" in n or "shout" in n or "gouge" in n else "Arcane"
        t.cast_time_ms = 0
        t.cooldown_ms = 30000
        t.range_ = 5.0 if "gouge" in n or "sap" in n or "kidney" in n else 30.0
        t.mana_cost = 30
        t.description = f"{name}: kontrolliert das Ziel kurzzeitig."
        # CC läuft als Aura — Auras sind als Stub bereits in auras.json (Stun-Type folgt).
        t.effects.append({
            "type": "ApplyAura",
            "target_type": "HostileUnit",
            "positive": False,
            "aura_id": "stun_generic",
        })
        return t

    # ---- Dispel ------------------------------------------------------------
    if has_any(name, KW_DISPEL):
        t.school = "Holy"
        t.cooldown_ms = 8000
        t.range_ = 30.0
        t.mana_cost = 20
        t.description = f"{name}: entfernt einen schädlichen Effekt."
        t.effects.append({
            "type": "Dispel",
            "target_type": "FriendlyUnit",
            "data1": 1,
            "positive": True,
        })
        return t

    # ---- Resurrect (kein eigener Enum-Wert → no-op-Heal-Stub) -------------
    if has_any(name, KW_RESURRECT):
        t.school = "Holy"
        t.cast_time_ms = 10000
        t.cooldown_ms = 0
        t.range_ = 30.0
        t.mana_cost = 100
        t.description = f"{name}: erweckt eine tote Einheit wieder zum Leben (Platzhalter)."
        t.effects.append({
            "type": "Heal",
            "target_type": "FriendlyUnit",
            "data1": 1,
            "positive": True,
            "scale_formula": "1",
        })
        return t

    # ---- Teleport ----------------------------------------------------------
    if has_any(name, KW_TELEPORT):
        t.school = "Arcane"
        t.cast_time_ms = 3000
        t.cooldown_ms = 0
        t.mana_cost = 50
        t.description = f"{name}: teleportiert den Caster."
        t.effects.append({
            "type": "Teleport",
            "target_type": "SelfCaster",
            "positive": True,
        })
        return t

    # ---- Mana Burn ---------------------------------------------------------
    if has_any(name, KW_MANA):
        t.school = "Shadow"
        t.cast_time_ms = 3000
        t.range_ = 30.0
        t.mana_cost = 40
        t.description = f"{name}: verbrennt Mana des Ziels."
        t.effects.append({
            "type": "RestoreMana",
            "target_type": "HostileUnit",
            "data1": -60,
            "positive": False,
        })
        return t

    # ---- Heal --------------------------------------------------------------
    if has_any(name, KW_HEAL):
        t.school = "Holy"
        t.cast_time_ms = 2000 if "greater" in n else (1500 if "flash" in n else 0 if "lay on hands" in n else 1800)
        t.cooldown_ms = 120000 if "lay on hands" in n else 0
        t.range_ = 30.0
        t.mana_cost = 60 if "greater" in n else 40
        amount = 200 if "greater" in n else 120 if "flash" in n else 999 if "lay on hands" in n else 80
        t.description = f"{name}: heilt ein verbündetes Ziel."
        t.effects.append({
            "type": "Heal",
            "target_type": "FriendlyUnit",
            "data1": amount,
            "positive": True,
            "scale_formula": f"sp*0.6+{amount}",
        })
        return t

    # ---- Fire --------------------------------------------------------------
    if has_any(name, KW_FIRE):
        t.school = "Fire"
        t.cast_time_ms = 2500 if "fireball" in n else (0 if "blast" in n else 1500)
        t.cooldown_ms = 8000 if "blast" in n else 0
        t.range_ = 30.0
        t.projectile_speed = 24.0
        t.mana_cost = 35
        dmg = 80 if "fireball" in n else 100 if "inferno" in n else 50
        t.description = f"{name}: verursacht Feuerschaden."
        t.effects.append({
            "type": "SchoolDamage",
            "target_type": "HostileUnit",
            "data1": dmg,
            "positive": False,
            "scale_formula": f"sp*0.5+{dmg}",
        })
        t.effects.append({
            "type": "ApplyAura",
            "target_type": "HostileUnit",
            "positive": False,
            "aura_id": "burn",
        })
        return t

    # ---- Frost -------------------------------------------------------------
    if has_any(name, KW_FROST):
        # Frozen Armor / Frost Ward = Buff
        if "armor" in n or "ward" in n:
            t.school = "Frost"
            t.gcd_ms = 1500
            t.cast_time_ms = 0
            t.mana_cost = 50
            t.description = f"{name}: defensiver Frost-Buff."
            t.effects.append({
                "type": "ApplyAura",
                "target_type": "SelfCaster",
                "positive": True,
                "aura_id": "frozen_armor",
            })
            return t
        if "nova" in n:
            t.school = "Frost"
            t.cast_time_ms = 0
            t.cooldown_ms = 25000
            t.range_ = 0.0
            t.mana_cost = 40
            t.description = f"{name}: friert Gegner im Nahkampfradius ein."
            t.effects.append({
                "type": "SchoolDamage",
                "target_type": "AreaSrcHostile",
                "data1": 40,
                "radius": 8.0,
                "positive": False,
                "scale_formula": "sp*0.3+40",
            })
            t.effects.append({
                "type": "ApplyAura",
                "target_type": "AreaSrcHostile",
                "positive": False,
                "aura_id": "frozen",
            })
            return t
        # Default Frost-Bolt
        t.school = "Frost"
        t.cast_time_ms = 2000
        t.range_ = 30.0
        t.projectile_speed = 22.0
        t.mana_cost = 30
        t.description = f"{name}: verursacht Frostschaden und verlangsamt."
        t.effects.append({
            "type": "SchoolDamage",
            "target_type": "HostileUnit",
            "data1": 60,
            "positive": False,
            "scale_formula": "sp*0.45+60",
        })
        t.effects.append({
            "type": "ApplyAura",
            "target_type": "HostileUnit",
            "positive": False,
            "aura_id": "chilled",
        })
        return t

    # ---- Holy Damage -------------------------------------------------------
    if has_any(name, KW_HOLY_DMG):
        t.school = "Holy"
        t.cast_time_ms = 1500
        t.range_ = 30.0 if "smite" in n or "bolt" in n or "mind blast" in n else 5.0
        t.projectile_speed = 30.0 if t.range_ > 10.0 else 0.0
        t.mana_cost = 25
        t.description = f"{name}: verursacht heiligen Schaden."
        t.effects.append({
            "type": "SchoolDamage",
            "target_type": "HostileUnit",
            "data1": 50,
            "positive": False,
            "scale_formula": "sp*0.4+50",
        })
        return t

    # ---- Shadow ------------------------------------------------------------
    if has_any(name, KW_SHADOW):
        t.school = "Shadow"
        t.cast_time_ms = 2000
        t.range_ = 30.0
        t.projectile_speed = 22.0
        t.mana_cost = 30
        t.description = f"{name}: verursacht Schattenschaden."
        t.effects.append({
            "type": "SchoolDamage",
            "target_type": "HostileUnit",
            "data1": 65,
            "positive": False,
            "scale_formula": "sp*0.45+65",
        })
        return t

    # ---- Buff --------------------------------------------------------------
    if has_any(name, KW_BUFF):
        t.school = "Holy" if "blessing" in n or "devotion" in n or "righteous" in n or "salvation" in n or "retribution" in n else "Arcane"
        t.cast_time_ms = 0
        t.cooldown_ms = 0
        t.range_ = 0.0 if "self" in n or "sprint" in n or "stealth" in n else 30.0
        t.mana_cost = 30
        target = "SelfCaster" if t.range_ == 0.0 else "FriendlyUnit"
        t.description = f"{name}: positiver Effekt für den Empfänger."
        t.effects.append({
            "type": "ApplyAura",
            "target_type": target,
            "positive": True,
            "aura_id": to_id(name),
        })
        return t

    # ---- Weapon-Damage Spec --------------------------------------------------
    if has_any(name, KW_WEAPON):
        t.school = "Physical"
        t.cast_time_ms = 0
        t.cooldown_ms = 6000 if any(k in n for k in ("charge", "thunder clap", "whirlwind", "recklessness")) else 0
        t.range_ = 25.0 if "charge" in n else (30.0 if "shot" in n or "shoot" in n else 5.0)
        t.mana_cost = 0
        t.description = f"{name}: verstärkter Waffenangriff."
        t.effects.append({
            "type": "WeaponDamage",
            "target_type": "HostileUnit",
            "data1": 20,
            "positive": False,
            "scale_formula": "ap*0.3+20",
        })
        return t

    # ---- Default: Single-Target Weapon Damage ------------------------------
    t.school = "Physical"
    t.range_ = 5.0
    t.description = f"{name}: Standard-Angriff (Platzhalter)."
    t.effects.append({
        "type": "WeaponDamage",
        "target_type": "HostileUnit",
        "data1": 15,
        "positive": False,
    })
    return t


def build_spell_entry(icon_file: Path) -> Dict[str, Any]:
    name = icon_file.stem
    tpl = classify(name)
    entry: Dict[str, Any] = {
        "id": to_id(name),
        "name": name,
        "icon": f"spell_icons/{name}",
        "description": tpl.description,
        "school": tpl.school,
        "cast_time_ms": tpl.cast_time_ms,
        "cooldown_ms": tpl.cooldown_ms,
        "gcd_ms": tpl.gcd_ms,
        "range": tpl.range_,
        "projectile_speed": tpl.projectile_speed,
        "max_targets": 1,
        "attributes": "None",
        "mana_cost": tpl.mana_cost,
        "effects": tpl.effects,
    }
    return entry


def main() -> int:
    if not ICONS_DIR.is_dir():
        print(f"[ERROR] Icons-Ordner nicht gefunden: {ICONS_DIR}", file=sys.stderr)
        return 1

    seen_ids = set()
    spells: List[Dict[str, Any]] = []

    for p in sorted(ICONS_DIR.iterdir()):
        if p.suffix.lower() != ".png":
            continue
        if not is_spell_icon(p.stem):
            continue
        entry = build_spell_entry(p)
        sid = entry["id"]
        if sid in seen_ids:
            continue
        seen_ids.add(sid)
        spells.append(entry)

    # ---- Curated class-specific spells (cbt_/live_ icons) ------------------
    # SoF-Klassenkürzel: as=Assassin, el=Elementalist, kn=Knight, ra=Ranger, pr=Priest.
    # Diese Icons folgen dem internen Naming-Schema `<src>_<class>_<ability>_g<rank>`
    # und werden hier explizit ins Catalog gehoben, damit alle 116 Icons abgedeckt sind.
    CURATED_EXTRA: List[Dict[str, Any]] = [
        {"icon": "cbt_as_signetburst_g1", "id": "signet_burst", "name": "Signet Burst",
         "school": "Arcane", "effect": "SchoolDamage", "mana": 35, "cd": 8000,
         "desc": "Detonates a stored arcane signet for instant area damage."},
        {"icon": "cbt_as_tigerfang_g1", "id": "tiger_fang", "name": "Tiger Fang",
         "school": "Physical", "effect": "WeaponDamage", "mana": 20, "cd": 6000,
         "desc": "A swift double-strike that mimics a tiger's claws."},
        {"icon": "cbt_as_tigerfang_g10", "id": "tiger_fang_rank_10", "name": "Tiger Fang X",
         "school": "Physical", "effect": "WeaponDamage", "mana": 40, "cd": 6000,
         "desc": "Mastered Tiger Fang — both strikes hit harder and bypass armor."},
        {"icon": "cbt_el_pet_elementalwraith_g1", "id": "elemental_wraith", "name": "Elemental Wraith",
         "school": "Nature", "effect": "ApplyAura", "mana": 80, "cd": 60000,
         "desc": "Conjures an elemental wraith that fights alongside the caster."},
        {"icon": "cbt_kn_grandprotection_g1", "id": "grand_protection", "name": "Grand Protection",
         "school": "Holy", "effect": "ApplyAura", "mana": 60, "cd": 30000,
         "desc": "A greater ward of protection that reduces all incoming damage."},
        {"icon": "cbt_ra_sabageroar_g1", "id": "savage_roar", "name": "Savage Roar",
         "school": "Physical", "effect": "ApplyAura", "mana": 25, "cd": 20000,
         "desc": "A primal roar that increases attack power."},
        {"icon": "cbt_ra_sabageroar_g10", "id": "savage_roar_rank_10", "name": "Savage Roar X",
         "school": "Physical", "effect": "ApplyAura", "mana": 50, "cd": 20000,
         "desc": "Mastered Savage Roar — longer duration and stronger bonus."},
        {"icon": "live_el_hellpain_g1", "id": "hell_pain", "name": "Hell Pain",
         "school": "Shadow", "effect": "SchoolDamage", "mana": 45, "cd": 6000,
         "desc": "Tortures the target with searing shadow agony."},
        {"icon": "live_kn_brainstorm_g1", "id": "brainstorm", "name": "Brainstorm",
         "school": "Arcane", "effect": "ApplyAura", "mana": 30, "cd": 45000,
         "desc": "Clears the mind and reduces cast times briefly."},
        {"icon": "live_pr_tranquility_g1", "id": "tranquility", "name": "Tranquility",
         "school": "Nature", "effect": "Heal", "mana": 80, "cd": 60000,
         "desc": "A calming wave of nature that heals the target over time."},
    ]
    for extra in CURATED_EXTRA:
        if extra["id"] in seen_ids:
            continue
        eff_type = extra["effect"]
        effect: Dict[str, Any] = {
            "type": eff_type,
            "target_type": "SelfCaster" if eff_type == "ApplyAura" else (
                "FriendlyUnit" if eff_type == "Heal" else "HostileUnit"),
            "data1": 0,
            "radius": 0.0,
            "positive": eff_type in ("ApplyAura", "Heal"),
            "scale_formula": "sp*0.5+120" if eff_type in ("SchoolDamage", "Heal") else (
                "ap*1.2+30" if eff_type == "WeaponDamage" else ""),
            "aura_id": extra["id"] if eff_type == "ApplyAura" else "",
        }
        spells.append({
            "id": extra["id"],
            "name": extra["name"],
            "icon": f"spell_icons/{extra['icon']}",
            "description": extra["desc"],
            "school": extra["school"],
            "cast_time_ms": 0 if eff_type in ("WeaponDamage", "ApplyAura") else 2000,
            "cooldown_ms": extra["cd"],
            "gcd_ms": 1500,
            "range": 5.0 if eff_type == "WeaponDamage" else 30.0,
            "projectile_speed": 0.0,
            "max_targets": 1,
            "attributes": "None",
            "mana_cost": extra["mana"],
            "effects": [effect],
        })
        seen_ids.add(extra["id"])

    catalog = {
        "version": 1,
        "spells": spells,
    }

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUT_PATH.open("w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=2)

    print(f"[OK] {len(spells)} Spells nach {OUT_PATH} geschrieben")
    return 0


if __name__ == "__main__":
    sys.exit(main())
