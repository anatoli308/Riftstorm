# 19 — Current State, Open Items & Legacy Cleanup

> Status-Update 2026-06-02: Dieses Dokument bleibt als Detail-Referenz fuer die
> juengsten Ranged-/NPC-/HUD-Aenderungen aktuell. Die uebergreifende Audit-
> Uebersicht liegt jetzt in [21-audit-2026-06-02-spells-combat.md](21-audit-2026-06-02-spells-combat.md).

> Konsolidierter Stand nach Round 7 + 8 (Ranged-Layer-Registration, HUD-Stats,
> NPC-Facing-on-Attack). Dient als Startkontext für eine neue Chat-Session.
> Ergänzt: [15-equipment-system-runtime-swap.md](15-equipment-system-runtime-swap.md).

---

## 1. Aktueller Stand der jüngsten Änderungen

### 1.1 Ranged-Visuals (Round 7 + 8)

- `PlayerEquipmentVisuals.ShowRangedForCast(rangedId)` blendet während eines
  Shoot-Casts MainHand + OffHand aus und Ranged ein.
  `HideRangedAfterCast()` macht das Gegenteil und restored Main/Off aus den
  aktuellen `PlayerCombat`-NetVars.
- Trigger sitzen in `PlayerCombat.BeginCastClientRpc` /
  `EndCastClientRpc` (cross-peer sync), gegated über
  `SpellTemplate.RequiredEquipment == 12L` (Ranged-Code).
- **Round-8-Fix:** `GamePlayerBootstrap.BuildVisualsAsync` registriert jetzt
  drei statt zwei Equipment-Layer (`MainHand`, `OffHand`, **`Ranged`**) via
  `FlareCharacter.RegisterLayer`. Vorher war der Ranged-Layer nicht
  registriert → `SetLayerAtlas("Ranged", …)` lieferte still `false`, der
  Bogen blieb unsichtbar. Sorting-Orders: `bodyCount`, `bodyCount+1`,
  `bodyCount+2`. Layer-Namen kommen aus `PlayerEquipmentVisuals.*LayerName`
  (single source of truth).
- Atlas-JSONs liegen unter
  `Assets/StreamingAssets/player_male/<id>.json` +
  `Assets/StreamingAssets/player_female/<id>.json`. Verifiziert für
  `longbow.json` (beide Genders vorhanden).
- Atlas wird lazy bei `ShowRangedForCast` via `FlareAtlasLoader.LoadAsync`
  geladen — kein Preload nötig, der leere Layer reicht.

### 1.2 HUD-Stats: Spell / Heal (Round 7)

- `CharacterHUD` zeigt jetzt `Spell` und `Heal` als **absolute Werte**, nicht
  als `+Bonus`. Formeln:
  - `Spell = Intelligence`
  - `Heal  = Willpower + Intelligence / 2`
- User-validiert bei `INT=5 / WIL=5` → `Spell 5 / Heal 8`.
- **Caveat:** Diese Formeln sind reine HUD-Anzeige. Die echten Combat-Formeln
  (`CombatFormulas.CalculateSpellDamage` / `CalculateHealing`) sind separat
  und sollten bei Balance-Tuning gegen die HUD-Werte abgeglichen werden,
  sonst lügt das HUD.

### 1.3 NPC schaut zum Target (Round 8)

- `NpcController.UpdateCombat` setzt jetzt jeden Tick
  `m_ServerFacingVec = target.pos − self.pos`, sobald `m_CurrentTarget`
  gültig ist — vor den CC-Gates, vor Melee/Spell-Attempt.
- Wirkung: NPC dreht sich im Melee-Stand zum Spieler, auch wenn der Spieler
  ihn umrundet; Cast-Anims zeigen aufs Ziel. Vorher war die Direction
  Source-parity eingefroren, sobald `MoveTowardsEntity` keinen Schritt mehr
  gemacht hat (`NpcAI.cpp` Z.640–642).
- Hysterese (`m_DirectionHysteresisDeg = 6°`) bleibt aktiv → kein Flicker
  zwischen den 8 FLARE-Richtungen.

### 1.4 Dead-Target-Guard (Round 6)

- `SpellCaster.Validate` lehnt jeden Cast auf ein totes Target ab,
  unabhängig von `SpellAttributes.CanTargetDead`. Verhindert
  Aimed-Shot-Spam auf Leichen.

---

## 2. NPC-AI: wie wird wirklich gecastet?

> Update 2026-06-02: Slots sind nicht mehr auf 4 fix verdrahtet, und es gibt
> jetzt einen separaten **Notfall-/Primary-Spell** (`spell_primary`). Siehe
> auch [21-audit-2026-06-02-spells-combat.md](21-audit-2026-06-02-spells-combat.md).

`NpcController.SelectSpellSlotToCast` (Server-only, jeden Tick im `Combat`-
State) entscheidet in **drei Stufen**:

1. **Notfall-Primary (höchste Priorität):** Ist ein `spell_primary` gesetzt,
   sein eigener Cooldown abgelaufen (`m_PrimaryNextReadyAt`), der Spell
   castbar **und** die NPC-HP `<= k_EmergencyHealthPct` (30 %), wird sofort
   der Primary gewählt — vor allen regulären Slots. Rückgabe
   `k_PrimarySlotSentinel` (= `-2`).
2. **Reguläre Slots:** Iteration über `m_SpellSlots` **in Template-Reihenfolge**
   (dynamisch N Slots, nicht mehr fix 4). Erster Slot, der **alle** Gates
   passiert, gewinnt.
3. **Fallback-Primary:** Zog kein regulärer Slot in diesem Tick, wird der
   Primary nochmals geprüft (ohne HP-Bedingung) und gewählt, falls bereit.

| Gate            | Quelle (JSON)        | Wirkung                                              |
|-----------------|----------------------|------------------------------------------------------|
| Interval        | `intervalN` (ms)     | Minimaler Abstand zwischen Cast-**Versuchen**.       |
| Cooldown        | `cooldownN` (ms)     | Minimaler Abstand zwischen **erfolgreichen** Casts.  |
| Chance          | `chanceN` (0..100)   | `Random.Range(0,100) > chance` → skip.               |
| `CanCastSpell`  | `SpellCaster.Validate` | Range, Mana, CC, Equipment, Dead-Target.            |

Wichtige Konsequenzen:

- `interval` wird **erst bei erfolgreicher Slot-Wahl verbraucht**, nicht bei
  bloßen Fehlversuchen — sonst würden niedrige Chance-Werte die Frequenz
  fälschlich runtertakten.
- `chance=100 + interval=0 + cooldown=0` ⇒ NPC castet quasi jeden Tick.
  Praxiswerte: `chance=30..60`, `interval=2000..4000`.
- Slot-Priorität ist linear, kein Scoring. Slot 1 dominiert die Folge-Slots →
  der „interessanteste" Spell gehört nach **vorne**.
- **Primary** ist die einzige Ausnahme von der linearen Priorität: er sticht
  bei Notfall-HP alles, und dient sonst als letzter Fallback. Eigener
  Cooldown-Gate (`m_PrimaryNextReadyAt`), unabhängig von den Slot-Timern.
- `k_EmergencyHealthPct = 30f` (HP-Schwelle in Prozent),
  `k_PrimarySlotSentinel = -2` (Sentinel-Slotindex für den Primary).
- Beim Despawn/Death/Evade-Reset werden `NextAttemptAt` / `NextReadyAt` **und**
  `m_PrimaryNextReadyAt` per `ResetSpellRuntimeTimers` genullt.
- Unbekannte Primary-IDs werden beim Spawn (`SpellCatalogLoader.TryGetTemplate`)
  verworfen und mit Warnung deaktiviert (`m_PrimarySpellId = 0`).

Datenstruktur in `npc_templates.json` (Slots dynamisch, `spell_primary` optional):

```jsonc
{
  "entry": 70001,
  "name": "Goblin Shaman",
  "spell_primary": 20030,                                   // Notfall-Heilung bei <=30% HP
  "spell1": 20015, "chance1": 50, "interval1": 3000, "cooldown1": 8000,
  "spell2": 20007, "chance2": 35, "interval2": 4500, "cooldown2": 0,
  "spell3": 0,     "chance3": 0,  "interval3": 0,    "cooldown3": 0,
  "spell4": 0,     "chance4": 0,  "interval4": 0,    "cooldown4": 0
}
```

- `cooldown=0` ⇒ Fallback auf `spell_template.cooldown` aus dem Spell selbst.
- `spell_primary` fehlt/`0` ⇒ kein Notfall-/Fallback-Spell, reines Slot-Verhalten
  wie zuvor (rückwärtskompatibel).
- Aktive Slot-Anzahl wird beim Spawn als `m_ActiveSpellSlotCount` gezählt;
  leere Trailing-Slots (`spellN = 0`) kosten zur Laufzeit nichts.

---

## 3. Offene Punkte / Was noch fehlt

### 3.1 Equipment-Visuals

- [ ] **Weitere Weapon-Layer registrieren** sobald Klassen wie 2H, Focus,
      Throwable, Off-Ranged etc. kommen. Aktuell sind nur `MainHand`,
      `OffHand`, `Ranged` registriert. Refactor-Vorschlag: in
      `PlayerEquipmentVisuals` ein `static readonly string[] LayerSlotNames`
      pflegen und `GamePlayerBootstrap` darüber iterieren — verhindert das
      „silent SetLayerAtlas=false"-Problem für künftige Slots.
- [ ] **NPC-Equipment-Visuals**: NPCs haben aktuell keine MainHand/OffHand-
      Layer, nur Body. Wenn NPC-Waffen sichtbar werden sollen, müssen
      `FlareNpcSpawner` / `MugenNpcSpawner` parallel zum Bootstrap-Code
      Layer registrieren.
- [ ] **Female-Atlas-Verifikation für alle Equipment-IDs**: `longbow` ist OK,
      aber jede neue Ranged-Waffe braucht beide Genders.

### 3.2 NPC-AI

- [ ] **Auren-Clear beim Evade-Reset** (`UpdateEvading`): aktuell wird HP +
      Mana zurückgesetzt, aktive Buffs/Debuffs bleiben.
      Source: `NpcAI::updateEvading` clearst Auren — Pendant fehlt, weil das
      Buff/Debuff-System noch nicht durchgezogen ist.
- [ ] **Pet-/NPC-vs-NPC-Friendly-Fire** ist hart aus (`IsHostileTo` filtert
      andere NpcController). Sobald Pets/Charmed-Units kommen, muss das
      revisited werden.
- [ ] **Move-Speed-Override**: `WalkSpeed` ist read-only; der Template-
      Override läuft in `FlareNpcSpawner.ApplyStatsToUnitStats` **vor** dem
      Netcode-Spawn. Nach Spawn kann `move_speed` nicht mehr verändert
      werden — relevant für Haste/Slow-Auren auf NPCs.
- [ ] **Spell-Slot-Scoring** (optional): aktuell linear, kein Priority/
      Threat-Bonus. Falls AI smarter werden soll, hier ansetzen.

### 3.3 HUD / Stats

- [ ] **HUD-Formeln ↔ Combat-Formeln synchron halten**: `CharacterHUD`
      zeigt `INT` / `WIL + INT/2`. `CombatFormulas` muss bei jedem Balance-
      Pass dagegen geprüft werden. Idealfall: zentrale Formel in
      `CombatFormulas` + `CharacterHUD` ruft die.
- [ ] **Ranged-Damage im HUD**: aktuell nicht angezeigt; `UnitStats
      .BaseRangedWeaponDamage` ist nur für Validate/Damage lesbar.

### 3.4 Spell-Pipeline

- [ ] **Crossbow / Gun** als Ranged-Typen sind im Code vorbereitet
      (`RequiredEquipment=12` ist Sammeltyp), brauchen aber eigene Atlas-IDs
      + Cast-Anims. Bow-Pose ist Default.
- [ ] **Channeled / Charge-Spells** (Hold-to-Aim): aktuell nicht abgebildet.
      Nur Instant + Timed-Cast.

---

## 4. Legacy / zu entfernen

| Symbol / Datei                                          | Status                                                                                                                  | Empfehlung                                                                                                                                |
|---------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------|
| `MugenNpcSpawner.cs`                                    | „läuft nur noch für Visuals; AI nutzt FLARE-Defaults" (Eigenkommentar Z.205). Wird parallel zum `FlareNpcSpawner` gehalten. | Sobald alle NPC-Prefabs auf `FlareNpcSpawner` migriert sind: löschen. Vorher: prüfen welche NPC-Prefabs noch `MugenNpcSpawner` referenzieren. |
| `MugenAnimationShowcase.cs`                             | Test/Tool-Komponente, hängt am `MugenNpcSpawner`.                                                                       | Mit `MugenNpcSpawner` zusammen entfernen oder auf `FlareNpcSpawner` portieren, falls Showcase-Tool im Editor weiter gewollt.              |
| `MugenHitboxRuntime.cs`                                 | Wird laut Doc-String vom `MugenNpcSpawner` getriggert.                                                                  | Funktional in FLARE-Pipeline ersetzbar — Audit ob noch produktiv benutzt.                                                                 |
| HUD-Label `Spell+` / `Heal+`                            | In Round 7 entfernt. Keine Restvorkommen im Code.                                                                       | Doku-Restbestände in `referenzen/16-equipment-stats.md` o. ä. ggf. nachziehen.                                                            |
| "Stance"-Variante (Main/Off ausblenden bei equipptem Bow) | In Round 5 kurz aktiv, in Round 6 entfernt. Cast-getriggerte Sichtbarkeit ist Standard.                                | Falls in alten Branches noch `EquipmentStanceController` o. ä. herumliegt: löschen.                                                       |
| `Resources/`-Ordner                                     | Per Copilot-Instructions verboten. Aktuell keiner unter `Assets/` — gut so.                                             | Bei zukünftigen Prefab/Material-Bedarfen: `[SerializeField]` auf `ApplicationEntryPoint` oder Addressables, nie `Resources.Load`.         |
| Legacy `UnityEngine.Input` API                          | Per Copilot-Instructions verboten. Spot-Check empfehlenswert (Editor-Tools).                                            | `grep` auf `Input.Get*` + Migration auf `InputSystem` (`InputAction`-Pattern aus `PlayerInputController.cs`).                              |
| `ScriptableObject` für Daten                            | Per Copilot-Instructions verboten. Konfiguration läuft über JSON in `Assets/StreamingAssets/`.                          | Falls noch SOs für Daten existieren: nach `StreamingAssets/<bereich>/<id>.json` portieren, Loader nach `HudConfigLoader`-Vorbild.         |

---

## 5. Architektur-Quick-Refs (für neue Session)

- **MVC-Root**: `BaseApplication` → `MetagameApplication` / `GameApplication`.
- **Pure Services** via `ServiceLocator`: `PrefabManager`, `TextureManager`.
  Registrierung in `ApplicationEntryPoint.Awake`, Teardown in `OnDestroy`
  via `ServiceLocator.ClearAll()`.
- **State Machines**: `ConnectionManager`, `AuthenticationManager`,
  `ConsoleManager` — Manager hält State + Events, States nur Transitionen.
- **Spell-Pipeline**: `RequestCastSpellServerRpc` →
  `SpellCaster.Validate` → `BeginCast` → `BeginCastClientRpc` →
  `ServerCompleteCast` → `SpellExecutor.Execute` → `EndCastClientRpc`.
  Visuals hängen sich an `BeginCastClientRpc` / `EndCastClientRpc`.
- **FLARE-Layer-Registration** in `GamePlayerBootstrap.BuildVisualsAsync`:
  Body-Layer aus Prefab-Daten (sort 0..N-1) +
  `MainHand` (N) + `OffHand` (N+1) + `Ranged` (N+2).
- **NPC-Tick**: 20–30 Hz server-authoritative; `NpcController.TickServer`
  → `UpdateIdle` / `UpdateCombat` / `UpdateEvading`. Direction-Replikation
  über `m_ServerDirection` mit 6° Hysterese.
- **Server is authority**: kein Client-Trust für Damage/Cooldown/Hit.
  ClientRpcs nur für Visuals/Sound, nie für Gameplay-Resultate.

---

## 6. Wichtige Dateien / Einstiegspunkte

| Datei                                                                       | Verantwortung                                                  |
|-----------------------------------------------------------------------------|----------------------------------------------------------------|
| `Assets/Scripts/Runtime/Game/Bootstrap/GamePlayerBootstrap.cs`              | Player-Spawn: Body- + Equipment-Layer registrieren, Bindings.  |
| `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs`                        | Server-Source-of-Truth für equippte Waffen + Cast-RPCs.        |
| `Assets/Scripts/Runtime/Game/Combat/PlayerEquipmentVisuals.cs`              | Client-Bridge NetVar → FLARE-Layer (inkl. Ranged-Cast-Toggle). |
| `Assets/Scripts/Runtime/Game/Sprites/FlareCharacter.cs`                     | Layer-Registry + `SetLayerAtlas` (silent fail wenn nicht reg). |
| `Assets/Scripts/Runtime/Game/Npc/NpcController.cs`                          | Server-AI: Idle/Combat/Evade, Facing, Spell-Slot-Picking.      |
| `Assets/Scripts/Runtime/Game/Npc/FlareNpcSpawner.cs`                        | NPC-Spawn auf FLARE-Basis (Nachfolger von `MugenNpcSpawner`).  |
| `Assets/StreamingAssets/npc_templates.json`                                 | NPC-Daten: Stats, Spell-Slots, Ranges, Faction.                |
| `Assets/StreamingAssets/player_{male,female}/<id>.json`                     | FLARE-Atlas-Definitionen für Body + Equipment-Sprites.         |

