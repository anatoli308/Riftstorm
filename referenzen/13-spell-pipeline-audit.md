# 13 — Spell-Pipeline: Audit & Roadmap zu Source-Parität

> **Stand:** Sprint A abgeschlossen (Sound bei Cast + Multi-Target-AoE).
> **Referenz-Quelle:** `c:\Users\anato\Downloads\steam-main\source_server\Server\src\Combat\`
> **Architektur-Erbe:** WoW-Emu-artig (Tab-Target, `SpellTemplate` mit 3 Effekt-Slots, `effectN_targetType`), **nicht** Flare. Aus Flare bleibt nur PPM = 64 px/m.

---

## 1. Gesamtbewertung

Die Pipeline ist **End-to-End korrekt verdrahtet** und entspricht in Struktur 1:1 dem `source_server`. Mana wird konsumiert, Cooldowns gestartet, Targets resolved, Schaden/Heal/Auras appliziert. **Schwächen liegen nicht in der Architektur, sondern in der Tiefe einzelner Berechnungs-Stufen** — vor allem Formel-Evaluation und Combat-Math.

### Aktueller End-to-End-Pfad

1. `PlayerCombat.TryRequestCastSpell(entry)` *(Owner-Client, [PlayerCombat.cs#L771](../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs#L771))*
2. `RequestCastSpellServerRpc(entry, targetNetId)` → Server resolved `SpellTemplate` + `primaryTarget` via `NetworkObject`
3. `CombatState.OnCastRequested` (Idle akzeptiert, Attacking/Casting/Dead lehnen ab)
4. `BeginCast` → bei `CastTime > 0` Wechsel in `CastingState`, sonst direkt `ServerCompleteCast`
5. **Server-Authoritative:** `SpellExecutor.Execute(caster, spell, primary)`
   - `Validate` — `SpellCaster.Validate` *(Caster-State → Resources → Target → Range)*
   - `ConsumeResources` — Mana + HP-Kosten
   - `StartCooldowns` — Spell-CD, optional GCD
   - 3× Effect-Slot-Loop: `ResolveTargets` → `ApplyEffect`
6. `PlaySpellCastClientRpc` broadcastet Visual/Sound

---

## 2. Was bereits korrekt funktioniert ✅

| Bereich | Datei | Status |
|---|---|---|
| Cast-Validierung (Order) | `SpellCaster.cs` | ✅ identisch zur Source-Reihenfolge |
| Caster-State (Dead/Stunned/Silenced + `IgnoreStun`) | `SpellCaster.CheckCasterState` | ✅ |
| Mana-Abzug | `SpellExecutor.ConsumeResources` | ✅ |
| HP-Kosten (`healthCost` + `healthPctCost`) | `SpellExecutor.ConsumeResources` | ✅ |
| Cooldown + Category + GCD | `SpellExecutor.StartCooldowns` | ✅ (nur Player, Mobs überspringen CDs — wie Source) |
| `Triggered`-Attribute überspringt GCD | `StartCooldowns` | ✅ |
| Faction friendly/hostile | `SpellCaster.CheckTarget` | ✅ |
| `CantTargetSelf`, `CanTargetDead` Attributes | `SpellCaster.CheckTarget` | ✅ |
| Range max + min (Pixel→Meter) | `SpellCaster.CheckRange` | ✅ |
| AoE `UnitAreaSrc*` (Sphere am Caster) | `SpellExecutor.ResolveTargets` | ✅ |
| AoE `UnitAreaDst*` (Sphere am Primärziel) | `SpellExecutor.ResolveTargets` | ✅ |
| `MaxTargets`-Cap distance-sorted | `SpellExecutor.ResolveTargets` | ✅ |
| Tote Targets ausfiltern (außer `CanTargetDead`) | `SpellCaster` + `ResolveTargets` | ✅ |
| `SchoolDamage` / `WeaponDamage` → `TakeDamage` | `SpellExecutor.ApplyEffect` | ✅ |
| `Heal` / `HealPct` → `Heal` | `SpellExecutor.ApplyEffect` | ✅ |
| `RestoreMana` / `RestoreManaPct` | `SpellExecutor.ApplyEffect` | ✅ |
| `ApplyAura` / `ApplyAreaAura` | `SpellExecutor.ApplyEffect` | ✅ |
| `TriggerSpell` (rekursiver Cast) | `SpellExecutor.ApplyEffect` | ✅ |
| Caster-Sound bei Cast | `PlayerCombat.TryTriggerCasterSound` | ✅ |
| Caster-Particles bei Cast | `PlayerCombat.TryTriggerCasterParticles` | ✅ |

---

## 3. Lücken vs. `source_server`

### Lücke 1 — Formel-Evaluator fehlt komplett

**Source:** `SpellUtils.cpp:19 evaluateFormula` — Shunting-Yard-Parser für `+ - * / ()` mit Variable `clvl`. Verwendet für:
- `manaFormula` (z. B. `"2+((clvl*20)/20)"`)
- `effectScaleFormula[3]` (Schaden/Heal pro Level)
- `durationFormula` (im Schema vorhanden, im Source `(void)`)

**Wir aktuell:**
- `SpellUtils.CalculateManaCost`: nur `int.TryParse` → jede Formel mit `clvl` ergibt **0 Mana**
- `ApplyEffect`: nutzt `eff.Data1` direkt → kein Level-Scaling

**Auswirkung:** Alle Spells mit `manaFormula != Plain-Int` sind kostenlos, alle Spells skalieren nicht mit Caster-Level.

**Aufwand:** klein (~130 Zeilen Port).

---

### Lücke 2 — MOBA-Cast-Pattern (Skillshot/Ground/Cone)

**Source hat das nicht** — ist reines Tab-Target. Für Riftstorm aber gameplay-kritisch.

| Pattern | Beispiel | Benötigt |
|---|---|---|
| Skillshot | Ezreal Q | Projectile-Sim, Travel-Time, erstes-Ziel-trifft, NetworkObject pro Projektil oder Server-Tick-Simulation |
| Ground-Target AoE | Lux R, Annie Tibbers | `castPoint: Vector3` als Cast-Param, Client-Preview-Indikator |
| Frontal-Cone | Annie W | `Vector3.Angle(facing, toTarget)` Filter zusätzlich zu `OverlapSphere` |
| Line-AoE | Morgana Q | `OverlapBox` / `OverlapCapsule` entlang Cast-Direction |
| Smart-Cast | LoL-Quickcast | Input-Layer: Cursor-Pos → direkter Cast ohne Target-Selection |

**Schemata-Erweiterung nötig:**
```csharp
RequestCastSpellServerRpc(
    int spellEntry,
    ulong targetNetId,        // Tab-Target (bestehend)
    Vector3 castPoint,        // NEU: Ground-Target
    Vector3 castDirection     // NEU: Skillshot/Cone
)
```

**Neue `SpellTargetType`-Werte (außerhalb Source-Enum):**
- `DestGround` — Ground-AoE-Zentrum
- `DestCone` — Cone mit Winkel aus `eff.Data2`
- `DestLine` — Line/Capsule
- `Projectile` — Skillshot

**Aufwand:** mittel-groß, am besten als **eigene Sprint-Phase NACH** #1, #3, #4.

---

### Lücke 3 — `CombatFormulas` fehlt komplett

**Source:** `CombatFormulas.cpp:520 calculateDamage` → 8 Schritte. **Wir:** `eff.Data1` → `TakeDamage` (1 Schritt).

| Source-Schritt | Source-Konstante | Wir |
|---|---|---|
| `rollToHit` | BASE_HIT_CHANCE 95, MISS_PER_LEVEL 5 | ❌ |
| `getBaseDamage` | Formula + STR/10 (physisch) bzw. INT/20 (magisch) + Weapon | ⚠️ nur `Data1` |
| `applyDamageModifiers` (Buffs/Debuffs) | Stub in Source | ❌ |
| Armor-Reduktion | `armor / (armor + 400*20)`, cap 75 % | ❌ |
| Resist-Reduktion (Frost/Fire/Shadow/Holy) | `r / (r + 100)`, cap 75 % | ❌ |
| Crit ×2, Glancing ×0.7, Block ×0.7, Resist ×0.5 | CRIT_MULTIPLIER 2.0, GLANCING 0.7 | ❌ |
| Variance ±10 % | DAMAGE_VARIANCE 0.10 | ❌ |
| Minimum-Damage 1 | MIN_DAMAGE 1 | ⚠️ implizit |

**Heilung analog:** Crit-Heal, ±5 % Variance, Overheal-Kappung. Aktuell flat.

**Voraussetzung `UnitStats`-Erweiterung:**
- `Strength`, `Intelligence`, `Willpower` (für Scaling)
- `Armor`, `ResistFrost`, `ResistFire`, `ResistShadow`, `ResistHoly`
- `CritChance`, `DodgeChance`, `ParryChance`, `BlockChance`
- `WeaponDamage` (für physische Spells)

**Aufwand:** groß. Eigener Service `CombatFormulas` (Pure, ohne MonoBehaviour) + Felder + Tests.

---

### Lücke 4 — Channeled-Cast Movement-Cancel fehlt

**Source:** `CastResult::CasterMoving 4` existiert + `CasterCasting 5`. Bewegung während Cast-Time bricht ab.

**Wir:** `CastingState` hat aktuell keinen Movement-Listener. Spieler kann während Cast laufen → Cast vollendet trotzdem.

**Aufwand:** klein. `CastingState.Enter` abonniert `IMoveController.OnMoved` → bei Bewegung > Threshold `InterruptCast`. Optional `SpellAttributes.CanMoveWhileCasting`.

---

### Lücke 5 — Line of Sight (optional)

Source hat einen `hasLineOfSight`-Stub der immer `true` liefert. Bis Maps Wände bekommen: **nicht relevant**.

---

### Lücke 6 — Reagent / Equipment-Check (optional)

Beide sind in Source auskommentiert. Erst relevant wenn Inventar steht.

---

## 4. Prioritäten-Roadmap

| Phase | Lücke | Aufwand | Blockt was? |
|---|---|---|---|
| **Phase 1** | Formel-Evaluator | klein | freischaltet alle existierenden `manaFormula`/`effectScaleFormula` aus den JSON-Templates |
| **Phase 3** | CombatFormulas + UnitStats-Stats | groß | echte Schaden-Skalierung mit Caster/Target-Stats; Voraussetzung für Itemization |
| **Phase 4** | Channeled-Cast Move-Cancel | klein | macht Cast-Time-Spells in Bewegung „ehrlich" |
| **Phase 2** *(später)* | MOBA-Cast-Pattern | mittel-groß | Skillshot / Ground-AoE / Cone — extension/swap auf bestehende Pipeline |

User-Entscheidung: **1 → 3 → 4 → 2.** MOBA-Pattern wird als Erweiterung gelayert, sobald die Math-Stack steht.

---

## 5. Architektur-Constraints (für alle folgenden Phasen)

- Server-authoritative (alle Berechnungen auf `IsServer`)
- Pure Services über `ServiceLocator` (keine MonoBehaviours für Formulas)
- Asmdef: `Riftstorm.Gameplay` hat keine `Management`-Ref → CombatFormulas gehört zu `Riftstorm.Gameplay` oder `Riftstorm.Game`
- JSON-only Daten, keine ScriptableObjects
- Files < 800 Zeilen, immutable Patterns, KISS/DRY/YAGNI
- Allokationsfrei in Hot Paths (statische Buffers wie in `SpellExecutor.ResolveTargets`)
- NEW Input System für alle Eingaben
- Tests: AAA-Pattern, mind. 80 % Coverage für Formulas

---

## 6. Quick-Reference: relevante Dateien

### Unity (Port)
- [SpellExecutor.cs](../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs) — Pipeline-Kern
- [SpellCaster.cs](../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellCaster.cs) — Validation
- [SpellUtils.cs](../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellUtils.cs) — Helpers + (zukünftig) Formula-Evaluator
- [SpellTemplate.cs](../Assets/Scripts/Runtime/Game/Spells/SpellTemplate.cs) — Daten-Model
- [SpellEnums.cs](../Assets/Scripts/Runtime/Game/Spells/SpellEnums.cs) — `SpellTargetType`, `SpellEffect`, `CastResult`, `SpellAttributes`
- [PlayerCombat.cs](../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs) — Cast-Entry, ServerRpc, ClientRpc
- [UnitStats.cs](../Assets/Scripts/Runtime/Game/Combat/UnitStats.cs) — Combat-State + (zukünftig) Stats-Erweiterung

### C++ Referenz
- `source_server/Server/src/Combat/SpellCaster.cpp` — Validierungs-Reihenfolge
- `source_server/Server/src/Combat/SpellUtils.cpp` — `evaluateFormula` (Port-Quelle Phase 1)
- `source_server/Server/src/Combat/CombatFormulas.cpp` — `calculateDamage` / `calculateHeal` (Port-Quelle Phase 3)
- `source_server/Shared/SpellDefines.h` — School-Enum, Attributes
