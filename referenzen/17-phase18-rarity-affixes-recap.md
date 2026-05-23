# 17 — Phase 18 (Item-Rarities + Random Affixes) — Recap & Pipeline-Audit

Stand: 23.05.2026

Diese Notiz fasst zusammen:
1. Was in Phase 18 konkret umgesetzt wurde.
2. Welche Systeme im Projekt **bereits aus dem Original portiert sind** (Audit).
3. Was im Original noch **nicht** portiert ist (Source-Gap).
4. Empfehlung zur Restruktur von `Assets/StreamingAssets/items/_templates.json`.
5. Mögliche Folge-Phasen.

---

## 1. Phase 18 — Was umgesetzt wurde

Ziel: D2-Style Random-Stats auf Equipment, kompatibel zum Source-D2-Vorbild
(`source_server/Shared/ItemAffix.h`, `ItemDefiner::crunchItemStats`).

### Neue / geänderte Dateien

| Datei | Inhalt |
|---|---|
| `Assets/Scripts/Runtime/Game/Items/ItemRarity.cs` | Enum `Common / Uncommon / Rare / Epic / Legendary`. |
| `Assets/Scripts/Runtime/Game/Items/ItemInstance.cs` | `INetworkSerializable` Struct: `TemplateId`, `Rarity`, `AffixId`, `Score (0–100)`, `Seed`. |
| `Assets/Scripts/Runtime/Game/Items/ItemAffix.cs` | Source-Parity: `Name`, bis zu 4 × `StatType*` + `StatValue*`. |
| `Assets/Scripts/Runtime/Game/Items/AffixCatalogLoader.cs` | Lazy-Static-Cache, lädt `Assets/StreamingAssets/items/_affixes.json` (7145 Affixe, **nicht modifizieren**). Dict-keyed by `entry`. |
| `Assets/Scripts/Runtime/Game/Items/ItemRoller.cs` | Deterministischer Server-Roll: SplitMix64-Seed → Affix-Pick + Score. Formel: `final = round(value * (0.5 + score / 200))`. |
| `Assets/Scripts/Runtime/Game/Items/PlayerEquipment.cs` | Zweite `NetworkList<ItemInstance>` parallel zu Slot-Templates; `SetSlotServer` / `ClearSlotServer` halten beide Listen atomar; `GetEquippedInstance(EquipSlot)`. Default-Loadout rollt **Rare** Mainhand + **Common** Offhand (`m_DefaultMainHandRarity`, `m_DefaultOffhandRarity`). |
| `Assets/Scripts/Runtime/Game/Combat/PlayerStats.cs` | `RecomputeEquipmentSums` iteriert pro Slot und ruft `AccumulateAffixStats(instance, slot)` auf; skaliert jeden Affix-Wert mit `0.5 + score/200` und addiert auf den jeweiligen `StatId`. |
| `Tools/inspect_affixes.py` | Python-Helper zum Audit der Affix-JSON (PowerShell `ConvertFrom-Json` ist auf diesem Datensatz unbrauchbar). |

### Verifiziert im Editor

- Default-Loadout: Rare Vincent's Old Sword zeigt im Tooltip **+2 STR, +10 Health**, Rarity-Label korrekt.
- Default-Loadout: Common Barricade Buckler zeigt korrekte Offhand-Anzeige.
- Damage stieg von 17 → 18 (STR/2 fließt in `CombatFormulas.CalculateMeleeDamage` ein).

### Bewusst noch offen

- **Phase 19 (Inventory-Instance-Awareness)**: `PlayerInventory.TryEquipFromInventoryServer` rollt aktuell als Common-MVP. Inventory hält noch `TemplateId`, nicht `ItemInstance` → Affixe gehen beim Ablegen/Aufnehmen verloren.
- **Gems / Sockets** (`num_sockets` in Templates vorhanden, aber kein Socket-System aktiv).
- **HUD-Rarity-Farbe + Tooltip-Affix-Breakdown** (Tooltip zeigt Rarity, aber Affix-Stats werden noch nicht aufgeschlüsselt dargestellt).
- Diagnostische `Debug.Log`-Zeilen in `PlayerStats.RecomputeEquipmentSums` → können nach finalem Phase-Abschluss raus.

---

## 2. Audit — Was im Projekt aus dem Original schon da ist

Geprüft gegen `c:\Users\anato\Downloads\steam-main\source_server\`.

| Original (C++) | Riftstorm (C#) | Status |
|---|---|---|
| `Combat/CombatFormulas.cpp` | `Assets/Scripts/Runtime/Gameplay/Combat/CombatFormulas.cs` | ✅ Hit / Dodge / Crit-Roll, Armor- + Resist-Reduktion, Variance, Glancing / Block-Multiplier, Spell-Hit, Heal-Variance, Aura-Modifier-Hook |
| `Combat/AuraSystem.cpp` | `Game/Spells/Runtime/AuraManager.cs` + `Aura.cs` | ✅ Apply / Refresh / Stack / Expire, Periodic-Ticks, `ModifyDamageDealt/ReceivedPct`, MaxBuffs/MaxDebuffs, Server-Snapshot-Broadcast |
| `Combat/SpellCaster.cpp`, `SpellUtils.cpp` | `Game/Spells/SpellTemplate.cs`, `SpellCatalogLoader.cs`, `SpellEnums.cs`, `CastResult.cs` + komplettes `Gameplay/Combat/Spells/Visuals/` | ✅ Effekt-Slots, `ApplyAura` / `ApplyAreaAura`, `scale_formula` |
| `Combat/CooldownManager.cpp` + `Shared/CooldownHolder.h` | `Game/Spells/Runtime/CooldownManager.cs` | ✅ Per-Spell + Category + GCD (1500 ms), Stopwatch-monotonic Clock, von `SpellExecutor` / `PlayerCombat.NotifyCooldownStartedClientRpc` / ActionBar-HUD genutzt |
| `Combat/CombatMessenger.cpp` | `Game/Combat/FloatingCombatText.cs` + `UnitStats.BroadcastDamageClientRpc` | ✅ Strukturierte HitResult-Replikation (Hit/Crit/Miss/Dodge/Parry/Block/Glancing/Resist/Immune/Absorb), Pooled IMGUI-Renderer, allokfrei |
| `AI/NpcAI.cpp` + `ThreatManager.cpp` | `Game/Npc/NpcController.cs` + `ThreatManager.cs` | ✅ Vorhanden |
| `Shared/ItemAffix.h`, `ItemDefiner.h` | `Items/ItemAffix.cs`, `AffixCatalogLoader.cs`, `ItemRoller.cs` | ✅ Phase 18 |
| `Shared/ItemTemplate.h` | `Items/ItemTemplate.cs`, `ItemCatalogLoader.cs`, `ItemInstance.cs` | ✅ |
| `Systems/Equipment.cpp` | `Items/PlayerEquipment.cs` | ✅ |
| `Systems/Inventory.cpp` | `Items/PlayerInventory.cs` + `InventoryItem.cs` | ✅ Phase 19 — `InventoryItem` hält volle `ItemInstance`, `TryAddInstanceServer` für Unequip-Rückläufer, Affixe überleben Pickup/Equip/Unequip |

**Damit ist der Gameplay-Core (Player / NPC / Combat / Items / Equipment / Spells / Auras) im Skelett stehend.** Genau wie du es vermutest hast — das ist ein stabiler Start.

---

## 3. Source-Gap — Was im Original existiert, im Projekt aber noch nicht

Aus `source_server/Server/src/Systems/`:

| C++-System | Im Projekt? | Bemerkung |
|---|---|---|
| `LootSystem.cpp` | ❌ Fehlt | NPC-Drops via LootTables → `ItemRoller` → `ItemInstance`. Direkter Anschluss an Phase 18. |
| `ExperienceSystem.cpp` | ❌ Fehlt | XP-Vergabe + Level-Up. Speist `victim.Level` in `CombatFormulas`. |
| `VendorSystem.cpp` | ❌ Fehlt | Kauf / Verkauf (Tooltip zeigt schon `sell_price`). |
| `BankSystem.cpp` | ❌ Fehlt | Persistent-Storage. |
| `PartySystem.cpp` | ❌ Fehlt | Group-Loot-Voraussetzung. |
| `GuildSystem.cpp` | ❌ Fehlt | |
| `GossipSystem.cpp` | ❌ Fehlt | NPC-Dialoge. |
| `QuestManager.cpp` + `PlayerQuestLog.cpp` | ❌ Fehlt | Quest-Daten. |
| `TradeSystem.cpp` | ❌ Fehlt | Player-zu-Player Trade. |
| `DuelSystem.cpp` | ❌ Fehlt | |
| `ChatSystem.cpp` | ❌ Fehlt | |

Aus `source_server/Server/src/World/`:

| C++-Datei | Im Projekt? |
|---|---|
| `Entity.cpp`, `Map.cpp`, `MapManager.cpp`, `WorldManager.cpp` | 🟡 Teilweise (Unity-Scene-Modell statt eigener WorldManager) |
| `Player.cpp` | ✅ `Game/Player/` |
| `Npc.cpp`, `NpcSpawner.cpp` | ✅ `Game/Npc/` (inkl. `FlareNpcSpawner`, `MugenNpcSpawner`) |

**Honest Take:** Wenn du Riftstorm als Survivor-MOBA mit 15–25 min Match-Length zielst, brauchst du **nicht** alle MMO-Systeme aus dem Source. Trade / Guild / Bank / persistenter Vendor sind ARPG-MMO-Features, kein Match-Based-Gameplay. Was du **schon** brauchst: Loot, Experience, evtl. ein leichtgewichtiges Vendor / Reroll-Interface im Match.

---

## 4. `items/_templates.json` — Redundanz?

Audit per `Tools/inspect_affixes.py`-ähnlichem Python-Check:

```
Total entries:        17.179
quality dist:         {1: 850, 2: 3233, 3: 3335, 4: 3217, 5: 3293, 6: 3219, 0: 32}
generated=1:          16.000   ← auto-generierte Variationen
generated=0:           1.179   ← echte Hand-Templates (Potions, Quest-Items, Unique)
mit inline stat_typeN:    52   ← (alle in generated=0)
```

**Befund:** 93 % der Datei sind vom Original-D2-Build vorgenerierte Items (z. B. 700× „Stave", 500× „Coif" etc., je nach Level/Quality). Die original `ItemDefiner::crunchItemStats(entry, affixId, affixScore, ...)` würde im **Live-System** nur die ~1.179 hand-authored Templates brauchen — die generated-Variationen sind ein SQL-Dump nach Pre-Generation.

### Empfehlung

**Trenne die Datei in zwei Sources:**

```
Assets/StreamingAssets/items/
├── _templates.json        ← nur generated=0  (~1.179 Einträge, hand-authored)
├── _archetypes.json       ← Item-Archetypen (Stave, Coif, Slicer, …) als Klassen,
│                            parametrisiert durch required_level/quality
├── _affixes.json          ← unverändert (Source D2, 7145 Einträge, DO NOT MODIFY)
└── _loot_tables.json      ← (Phase ?): NPC → Archetype-Pool → Drop-Chance
```

**Drop-Pipeline (zukünftig):**

```
NPC dies
  → LootSystem rollt LootTable
  → picks Archetype (z.B. "Stave") + Item-Level + Rarity
  → ItemRoller(seed) wählt Affixe + Score
  → produces ItemInstance (TemplateId aus Archetype-Template, AffixId, Score, Rarity, Seed)
  → drop on ground / direct inventory
```

Damit:
- `_templates.json` schrumpft von 7 MB → ~600 KB.
- Items werden zur Laufzeit generiert (echt D2-Style), nicht aus einem 16k-Lookup-Table.
- Die existierenden 16k generated-Entries bleiben als Migrations-Hilfe erhalten (in einem Backup), aber das Live-System braucht sie nicht.

**Vorgehen ohne Risiko:**

1. Python-Script: `Tools/split_templates.py` — schreibt `_templates_handauthored.json` (generated=0) und `_templates_archetypes_seed.json` (typische Beispiele pro Archetyp) ohne das Original zu überschreiben.
2. `ItemCatalogLoader` bleibt erstmal kompatibel zu allen 17k Einträgen.
3. Sobald `LootSystem` mit Archetypen funktioniert, kann `_templates.json` durch die schlankere Version ersetzt werden.

Wenn du das willst, kann ich `Tools/split_templates.py` als nächsten kleinen Schritt schreiben — das ist null Risiko, weil es nur Read-Only ist und neue Files produziert.

---

## 5. Folge-Phasen (priorisierte Optionen)

| # | Phase | Aufwand | Hebel |
|---|---|---|---|
| 19 | Inventory-Instance-Awareness + Gems | klein | Affixe überleben Pickup/Drop, Gems werden aktiv. **Logische direkte Fortsetzung von Phase 18.** |
| 20 | LootSystem (NPC drops) + Templates-Split | mittel | Aktiviert den vollen D2-Item-Loop: kill → drop → pickup → equip. |
| 21 | ExperienceSystem | klein–mittel | Level-Differenz fließt schon in `CombatFormulas` ein — fehlt nur der Level-Up-Loop. |
| 22 | Cooldown-Manager + CombatMessenger | mittel | Spell-Cooldowns zentral, strukturierte Combat-Messages (Miss/Crit/Block/Resist-Labels). |
| 23 | QuestManager + Gossip (optional) | groß | MMO-Feature, ggf. zurückstellen wenn Match-Based-Fokus. |

**Empfehlung:** 19 → 20 → 21 in dieser Reihenfolge, weil sie sich gegenseitig verstärken (Inventar trägt Instanzen → Loot liefert Instanzen → XP macht Item-Level relevant).

---

## Wichtige Konventionen (zur Erinnerung für die nächsten Phasen)

- **`Assets/StreamingAssets/items/_affixes.json`**: Source-D2-Daten, **niemals** modifizieren (7145 Einträge).
- **PowerShell `ConvertFrom-Json`** ist auf großen Affix/Template-Files unzuverlässig → immer `python -c "import json; ..."` oder ein Skript unter `Tools/`.
- **Keine `Resources/`-Ordner**, keine `ScriptableObjects` für Daten — alles JSON unter `StreamingAssets/` mit Lazy-Static-Cache via Newtonsoft.
- **NEW Input System only** — kein `UnityEngine.Input.*`.
- Affix-Score-Formel: `final = round(value * (0.5 + score / 200))` (Score 0..100). Bei Score=50 → ×0.75, Score=100 → ×1.0, Score=0 → ×0.5. Source-Parity, in `ItemRoller` und `PlayerStats.AccumulateAffixStats` identisch.
- Default-Loadout-Rarities sind über `m_DefaultMainHandRarity` / `m_DefaultOffhandRarity` auf `PlayerEquipment` per `[SerializeField]` editierbar.
