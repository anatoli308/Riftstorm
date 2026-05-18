"""
GenerateSpellVisuals.py
=======================

Erzeugt `Assets/StreamingAssets/spells/spell_visuals.json` aus dem bereits
generierten `spells.json`. Mappt jeden Spell auf ein 3-Phasen-Visual-Kit
(Casting → Travel → Impact), das vom Runtime-Player (WorldSpellAnimation)
abgespielt wird. Mirror der C++-Original-Tabelle `spell_visual`.

Lookup-Reihenfolge pro Spell:
  1) Curated-Override (per spell_id in `CURATED_OVERRIDES`).
  2) Effect-spezifischer Default (Heal/ApplyAura/Dispel/Teleport/...).
  3) School-Default (Fire/Frost/Arcane/Nature/Shadow/Holy/Physical).
  4) Generischer Fallback (Arcane-Optik).

Animations-Namen MÜSSEN als Datei unter
`Assets/StreamingAssets/spells/animations/<name>.json` existieren, sonst
ignoriert die Runtime das Visual.

Aufruf:
    python Tools/GenerateSpellVisuals.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

REPO_ROOT = Path(__file__).resolve().parents[1]
SPELLS_PATH = REPO_ROOT / "Assets" / "StreamingAssets" / "spells" / "spells.json"
ANIMS_DIR = REPO_ROOT / "Assets" / "StreamingAssets" / "spells" / "animations"
OUT_PATH = REPO_ROOT / "Assets" / "StreamingAssets" / "spells" / "spell_visuals.json"

# -----------------------------------------------------------------------------
# Default-Mapping: Effect-Type → Visual-Kit
# -----------------------------------------------------------------------------
# Effect-Defaults haben Vorrang vor School-Defaults, weil "Heal" auf Holy ein
# komplett anderes Visual ist als "Smite" auf Holy.
EFFECT_DEFAULTS: Dict[str, Dict[str, Any]] = {
    "Heal": {
        "casting_anim": "cast_001",
        "impact_anim": "heal_001",
    },
    "Dispel": {
        "casting_anim": "cast_007",
        "impact_anim": "light_002",
    },
    "Teleport": {
        "casting_anim": "cast_002",
        "impact_anim": "magic_005",
    },
    "RestoreMana": {
        "casting_anim": "cast_002",
        "impact_anim": "magic_007",
    },
    "WeaponDamage": {
        # Nahkampf: kein Cast, kein Travel, nur ein Slash-Pop am Ziel.
        "impact_anim": "slash_001",
    },
}

# ApplyAura ist polarisiert (positive vs. negative); deshalb hier separat:
APPLY_AURA_POSITIVE: Dict[str, Any] = {
    "casting_anim": "cast_001",
    "impact_anim": "aura_001",
    "aura_loop_anim": "aura_001",
}
APPLY_AURA_NEGATIVE: Dict[str, Any] = {
    "casting_anim": "cast_006",
    "impact_anim": "darkness_001",
}

# -----------------------------------------------------------------------------
# Default-Mapping: School → Visual-Kit (Fallback, wenn Effect kein Mapping hat)
# -----------------------------------------------------------------------------
SCHOOL_DEFAULTS: Dict[str, Dict[str, Any]] = {
    "Fire": {
        "casting_anim": "cast_004",
        "travel_anim": "fire_002",
        "impact_anim": "fire_001",
        "travel_speed": 22.0,
    },
    "Frost": {
        "casting_anim": "cast_005",
        "travel_anim": "ice_001",
        "impact_anim": "ice_002",
        "travel_speed": 20.0,
    },
    "Arcane": {
        "casting_anim": "cast_002",
        "travel_anim": "magic_005",
        "impact_anim": "magic_007",
        "travel_speed": 24.0,
    },
    "Nature": {
        "casting_anim": "cast_003",
        "travel_anim": "wind_001",
        "impact_anim": "wind_002",
        "travel_speed": 20.0,
    },
    "Shadow": {
        "casting_anim": "cast_006",
        "travel_anim": "dark_effect_001",
        "impact_anim": "darkness_001",
        "travel_speed": 18.0,
    },
    "Holy": {
        # Instant-Lichtblitz am Ziel — keine Projektil-Phase.
        "casting_anim": "cast_007",
        "impact_anim": "light_001",
    },
    "Physical": {
        # Nahkampf-Fallback (Range > 5 wird via WeaponDamage-Default unten ohnehin
        # auf Slash zurückgesetzt).
        "impact_anim": "slash_001",
    },
}

GENERIC_FALLBACK: Dict[str, Any] = {
    "casting_anim": "cast_002",
    "impact_anim": "magic_007",
}

# -----------------------------------------------------------------------------
# Curated-Overrides: einzelne Spells, die per Name eine eigene Optik bekommen.
# -----------------------------------------------------------------------------
CURATED_OVERRIDES: Dict[str, Dict[str, Any]] = {
    # Klassischer Fireball: längeres Cast, sichtbarer Trail.
    "fireball": {
        "casting_anim": "cast_004_full",
        "travel_anim": "fire_002",
        "impact_anim": "fire_001",
        "travel_speed": 22.0,
    },
    # Lay on Hands: massives Holy-Burst.
    "lay_on_hands": {
        "casting_anim": "cast_007",
        "impact_anim": "light_003",
    },
    # Frostbolt: klassisches Ice-Projektil.
    "frostbolt": {
        "casting_anim": "cast_005",
        "travel_anim": "ice_001",
        "impact_anim": "ice_002",
        "travel_speed": 18.0,
    },
    # Smite / Holy Bolt: Lichtprojektil.
    "smite": {
        "casting_anim": "cast_007",
        "travel_anim": "light_004",
        "impact_anim": "light_001",
        "travel_speed": 26.0,
    },
    # Lightning-Bolt-artige Sprüche → Light-FX, schnelles Projektil.
    "lightning_bolt": {
        "casting_anim": "cast_003",
        "travel_anim": "lightning_001",
        "impact_anim": "lightning_001a",
        "travel_speed": 35.0,
    },
    # Mana Burn: Shadow-Optik.
    "mana_burn": {
        "casting_anim": "cast_006",
        "travel_anim": "dark_effect_001",
        "impact_anim": "darkness_002",
        "travel_speed": 18.0,
    },
}


# -----------------------------------------------------------------------------
# Helpers
# -----------------------------------------------------------------------------
def list_available_anims() -> set:
    if not ANIMS_DIR.exists():
        return set()
    return {p.stem for p in ANIMS_DIR.glob("*.json")}


def _merge_kit(*layers: Optional[Dict[str, Any]]) -> Dict[str, Any]:
    """Erste-Schicht-gewinnt-Merge: Curated → Effect → School → Fallback."""
    merged: Dict[str, Any] = {}
    for layer in layers:
        if not layer:
            continue
        for k, v in layer.items():
            if k not in merged:
                merged[k] = v
    return merged


def resolve_visual(spell: Dict[str, Any]) -> Dict[str, Any]:
    spell_id: str = spell.get("id", "")
    school: str = spell.get("school", "Physical")
    effects: List[Dict[str, Any]] = spell.get("effects") or []
    first_effect = effects[0] if effects else {}
    effect_type: str = first_effect.get("type", "")
    is_positive: bool = bool(first_effect.get("positive", False))

    curated = CURATED_OVERRIDES.get(spell_id, {})

    # Effect-Default
    if effect_type == "ApplyAura":
        effect_layer = APPLY_AURA_POSITIVE if is_positive else APPLY_AURA_NEGATIVE
    else:
        effect_layer = EFFECT_DEFAULTS.get(effect_type, {})

    # WeaponDamage ist instant + Nahkampf: kein Cast, kein Travel, nur Slash.
    # Kein School-Merge, kein Fallback — sonst kriegt jeder Melee-Skill ein
    # bogus "cast_002" vom GENERIC_FALLBACK.
    if effect_type == "WeaponDamage":
        return _merge_kit(curated, effect_layer)

    school_layer = SCHOOL_DEFAULTS.get(school, {})
    merged = _merge_kit(curated, effect_layer, school_layer)

    # GENERIC_FALLBACK greift nur, wenn die ersten drei Layer GAR NICHTS
    # produziert haben (z. B. unbekannte School + unbekannter Effect-Type).
    if not merged:
        merged = dict(GENERIC_FALLBACK)
    return merged


def make_visual_entry(spell: Dict[str, Any], available_anims: set) -> Optional[Dict[str, Any]]:
    spell_id = spell.get("id")
    if not spell_id:
        return None

    kit = resolve_visual(spell)

    # Animationen, die nicht im animations/-Ordner existieren, werden gefiltert,
    # damit die Runtime keinen "missing animation"-Spam loggt.
    for key in ("casting_anim", "travel_anim", "impact_anim", "aura_loop_anim"):
        name = kit.get(key)
        if name and name not in available_anims:
            print(f"  [warn] {spell_id}: animation '{name}' ({key}) fehlt — entfernt")
            kit.pop(key, None)

    if not any(kit.get(k) for k in ("casting_anim", "travel_anim", "impact_anim", "aura_loop_anim")):
        return None

    entry: Dict[str, Any] = {"spell_id": spell_id}
    # Stabile Feldreihenfolge im Output (lesbar/diff-stabil).
    for key in ("casting_anim", "travel_anim", "impact_anim", "aura_loop_anim", "travel_speed"):
        if key in kit:
            entry[key] = kit[key]
    return entry


# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
def main() -> int:
    if not SPELLS_PATH.exists():
        print(f"[ERROR] spells.json nicht gefunden: {SPELLS_PATH}")
        return 1

    with SPELLS_PATH.open("r", encoding="utf-8") as f:
        spells_doc = json.load(f)

    spells = spells_doc.get("spells") or []
    available = list_available_anims()
    if not available:
        print(f"[ERROR] Keine Animationen unter {ANIMS_DIR} gefunden")
        return 1

    visuals: List[Dict[str, Any]] = []
    skipped = 0
    for spell in spells:
        entry = make_visual_entry(spell, available)
        if entry is None:
            skipped += 1
            continue
        visuals.append(entry)

    visuals.sort(key=lambda e: e["spell_id"])

    out_doc = {
        "version": 1,
        "visuals": visuals,
    }

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUT_PATH.open("w", encoding="utf-8") as f:
        json.dump(out_doc, f, ensure_ascii=False, indent=2)

    print(f"[OK] {len(visuals)} Spell-Visuals nach {OUT_PATH} geschrieben "
          f"(skipped: {skipped})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
