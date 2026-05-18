# 14 — NPC-AI & Combat-Closure: Audit & Roadmap

> **Stand:** Spell-Pipeline Phase 1/3/4 abgeschlossen (Formel-Evaluator, CombatFormulas+UnitStats, Cast-Lifecycle). Sprint A1 (ThreatManager) ist der nächste Schritt.
> **Referenz-Quelle:** `c:\Users\anato\Downloads\steam-main\source_server\Server\src\AI\`
> **Architektur-Erbe:** WoW-Emu/FLARE-Mischung — siehe [10-spell-pipeline.md](10-spell-pipeline.md) und [13-spell-pipeline-audit.md](13-spell-pipeline-audit.md).

---

## 1. Gesamtbewertung

Der Combat-Loop ist **fast geschlossen, aber nicht ganz**. Spieler kann casten, Mob kann melee-schlagen, Auren ticken, Cooldowns laufen. Was noch fehlt, um den Loop wirklich „rund" zu machen:

1. **Threat-Tracking** — aktuell pickt der NPC immer das *closest hostile target*. Heal-Aggro, Taunt, „Tank pulls" funktionieren nicht.
2. **NPC-Spell-Casting** — Mobs können nur Auto-Attack, keine Templates aus dem Spell-Katalog (Source: `NpcAI::performSpellCast` + `selectSpellToCast`).
3. **Retaliation** — Neutral/Friendly schalten nicht auf Combat, wenn sie geschlagen werden, weil `DamageInfo` keinen Attacker mitführt.
4. **Aggro-Propagation (`callForHelp`)** — nahe Allies eines provozierten Mobs aggroen nicht mit.
5. **Idle-Bewegung (`updatePatrol`/`updateWander`)** — Mobs stehen statisch.

Architektur (State-Machine, Movement, Faction, Damage-Pfad) ist sauber portiert. **Schwächen sind Tiefe, nicht Struktur** — wie schon bei der Spell-Pipeline.

---

## 2. Parität pro Source-Funktion

Source-Header: `Server/src/AI/NpcAI.h` + `ThreatManager.h`.

| Source-Funktion | Riftstorm-Pendant | Status |
|---|---|---|
| `NpcAI::update` | `NpcController.TickServer` | ✅ |
| `NpcAI::updateIdle` | `NpcController.UpdateIdle` | ✅ |
| `NpcAI::updateCombat` | `NpcController.UpdateCombat` | ✅ |
| `NpcAI::updateEvading` | `NpcController.UpdateEvading` | ✅ |
| `NpcAI::findAggroTarget` | `NpcController.FindAggroTarget` (OverlapSphere) | ✅ |
| `NpcAI::isInMeleeRange` | `NpcController.IsInMeleeRange` | ✅ |
| `NpcAI::moveTowardsEntity` | `NpcController.MoveTowardsEntity` | ✅ |
| `NpcAI::returnHome` | `NpcController.MoveTowardsPoint(m_HomePosition,…)` | ✅ |
| `NpcAI::shouldLeash` | `NpcController.ShouldLeash` | ✅ |
| `NpcAI::isValidTarget` | `NpcController.IsValidTarget` | ✅ |
| `NpcAI::performMeleeAttack` | `NpcController.TryMeleeAttack` (→ `CombatFormulas.CalculateMeleeDamage`) | ✅ |
| `NpcAI::performSpellCast` | — | ❌ |
| `NpcAI::canCastSpell` | — | ❌ |
| `NpcAI::selectSpellToCast` | — | ❌ |
| `NpcAI::callForHelp` | — | ❌ |
| `NpcAI::updatePatrol` | — | ❌ |
| `NpcAI::updateWander` | — | ❌ |
| `ThreatManager::addThreat` | — | ❌ |
| `ThreatManager::modifyThreat` | — | ❌ |
| `ThreatManager::removeThreat` | — | ❌ |
| `ThreatManager::getHighestThreat` | — | ❌ |
| `ThreatManager::getThreat` | — | ❌ |
| `ThreatManager::clear` | — | ❌ |
| `ThreatManager::hasThreat` | — | ❌ |

### Bemerkenswert: Retaliation-TODO im Code

`NpcController.UpdateIdle` hat bereits einen TODO-Kommentar:
> *„Neutral/Friendly bleiben Idle, bis sie per Retaliation in `HandleClientDamageReceived` auf Combat geschaltet werden (TODO: Attacker-Identitaet im Damage-Event)."*

Das ist genau der Punkt — der Damage-Event hat keinen Attacker. Das löst Sprint A1 gleich mit, weil ThreatManager den Attacker sowieso braucht.

---

## 3. Periodic-Aura-Tick-Driver — verifiziert ✅

Vorheriger Verdacht ausgeräumt:
- `AuraManager.Update(deltaTimeMs)` existiert und wird von `UnitStats.Update` pro Server-Tick aufgerufen.
- `ProcessPeriodicEffects` / `ApplyPeriodicTick` deckt `PeriodicDamage`, `PeriodicMeleeDamage`, `PeriodicHeal`, `PeriodicHealPct`.
- DoTs/HoTs ticken also schon korrekt — kein Action-Item.

---

## 4. Gesamt-Roadmap (Survivor-MOBA, 15–25 min Matches)

Wie im Plan-Doc besprochen — hier zur Nachlesbarkeit.

### Phase A — Combat-Loop schließen

| Sprint | Inhalt | Status |
|---|---|---|
| A1 | **ThreatManager + Attacker-Plumbing** — Threat-Tabelle pro NPC, Damage = Threat, Retaliation für Neutral/Friendly | ⏳ next |
| A2 | **NPC-Spell-Casting** — `performSpellCast`/`canCastSpell`/`selectSpellToCast`, NPCs nutzen `SpellExecutor` | offen |
| A3 | **`callForHelp`** — Aggro-Propagation an nahe Allies | offen |
| A4 | (optional) Patrol/Wander für Idle-Bewegung | nice-to-have |

### Phase B — Itemization (hoher Gameplay-Wert)

1. `ItemTemplate` + `ItemAffix` Daten-Model (Source `Shared/ItemTemplate.h` + `ItemAffix.h`)
2. `Inventory` (server-authoritativ, `NetworkList<ItemInstance>`)
3. `Equipment` (Slots → Affixe modifizieren `UnitStats`)
4. `LootSystem` (Drop-Tables, NPC-Death-Loot)
5. Loot-Window-UI

### Phase C — Spell-Pipeline Phase 2 (MOBA-Pattern)

Skillshots / Ground-AoE / Cone. Erst nach Itemization, weil Items die Spells skalieren sollen.

### Phase D — Match-Strukturen (eigenes System, NICHT aus Source)

15–25 min Match, ~15 Spieler, Wellenspawn, MOBA-Objectives, Match-Reward-Loop. Source hat dafür nichts Portierbares.

### Bewusst geskippt / sehr spät

- **Quests / Gossip / QuestLog** — MMO-Konzept, passt nicht zu Match-basiertem PvPvE.
- **Vendor / Bank** — nur Hub-Scene, kein Combat-Blocker.
- **Guild / Party / Duel / Trade** — Lobby/Hub. Party kommt sowieso über NGO-Lobbies.
- **ChatSystem** — separate Netcode-Frage.

---

## 5. Sprint A1 — Detailplan (ThreatManager + Attacker-Plumbing)

### 5.1 Ziele

1. NPC pickt nicht mehr „closest hostile", sondern das Target mit dem höchsten Threat-Wert.
2. Damage erzeugt Threat (1:1 mit `FinalDamage`).
3. Neutral/Friendly NPCs schalten bei Schaden auf Combat (Retaliation).
4. Kein Verhalten ändert sich für Hostile-Mobs, solange kein zweiter Player im Aggro-Radius ist — Abwärtskompatibilität.

### 5.2 Architektur-Entscheidungen

- **`ThreatManager` ist eine pure C#-Klasse** (kein MonoBehaviour, kein ServiceLocator) — eine Instanz pro `NpcController`, analog zum `AuraManager` im `UnitStats`. Source kapselt `ThreatManager` als Member auf `Npc*`.
- **Key = `NetworkObjectId` (ulong)**, nicht `UnitStats`-Referenz. Robust gegen Despawn/Destroy. Parallele Weak-Resolution per `NetworkManager.SpawnManager.SpawnedObjects[id]` beim `GetHighestThreat`-Lookup.
- **Damage-Pfad bekommt Attacker.** Neues `UnitStats.ApplyDamage(ICombatUnit attacker, in DamageInfo info)`-Overload; alter Pfad `ApplyDamage(in DamageInfo info)` bleibt als Convenience für Quellen ohne Attacker (z. B. Environment-Damage später). Neues server-only Event `OnServerDamaged(UnitStats attacker, DamageInfo info)` zusätzlich zum bestehenden `ClientDamageReceived`.
- **Threat-Formel (Sprint A1):** `threat += info.FinalDamage`. Heal-Threat, Spell-Threat-Multiplier (Source `SpellAttributes.NoThreat` / `Threat`-Effekt) kommen erst in A2/A3.
- **`NoThreat`-Flag respektieren.** Wenn ein Spell-Schaden mit `SpellAttributes.NoThreat` fliegt, wird kein Threat addiert.

### 5.3 Konkrete Änderungen

| Datei | Änderung |
|---|---|
| `Assets/Scripts/Runtime/Game/Npc/ThreatManager.cs` *(neu)* | Pure-C#-Klasse: `Add/Modify/Remove/GetHighest/GetThreat/Clear/HasThreat` |
| `Assets/Scripts/Runtime/Gameplay/Combat/DamageInfo.cs` | Optionales Flag `NoThreat` (oder durch Attacker=null markiert) — entscheiden bei Impl. |
| `Assets/Scripts/Runtime/Game/Combat/UnitStats.cs` | `ApplyDamage(ICombatUnit attacker, in DamageInfo info)` Overload + `OnServerDamaged`-Event. Bestehende `ApplyDamage(in DamageInfo)` delegiert mit `attacker=null`. |
| `Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs` | Beim Apply von `SchoolDamage`/`WeaponDamage` Attacker durchreichen; `NoThreat`-Flag aus Spell ableiten. |
| `Assets/Scripts/Runtime/Game/Npc/NpcController.cs` | `ThreatManager m_Threat` Field. `UpdateCombat` nutzt `GetHighestThreat` statt `m_CurrentTarget`. `UpdateIdle`: Aggro-Pull addiert Threat=1 statt direkt zuzuweisen. Subscribe auf `OnServerDamaged` → AddThreat. Retaliation für Neutral/Friendly: jeder Schaden mit bekanntem Attacker schaltet auf Combat. |
| `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs` | Beim Auto-Attack Attacker (self) durchreichen. |

### 5.4 Tests (manuell, im Editor)

1. **Aggro-Pull bleibt rückwärtskompatibel:** 1 Player, 1 Hostile-Mob → Mob aggrot wie vorher.
2. **Threat-Pick:** 2 Players nahe dem Hostile-Mob, einer schlägt zuerst → Mob wechselt nicht zum näheren zweiten Player, sondern bleibt am Schläger.
3. **Retaliation:** Neutral-Mob (faction=2) wird angegriffen → schaltet auf Combat statt Idle zu bleiben.
4. **Friendly-No-Aggro:** Friendly-Mob (faction=1) ungeprovoziert → bleibt Idle.
5. **Leash leert Threat:** Mob in Evade → `ThreatManager.Clear()` beim Übergang zu Evading, danach Idle ohne hängengebliebenes Target.
6. **Tot ⇒ aus Threat-Tabelle:** Target stirbt während Combat → wird aus Tabelle entfernt, nächst-höchstes Threat wird Target.

### 5.5 Bewusst NICHT in A1

- Heal-Threat (Source: 50 % der Heilung an alle Mobs, die mit Allies des Heilers engaged sind).
- `SpellAttributes.Threat`-Effekt (= Taunt-Spell).
- Threat-Modifier durch Buffs (Source: `AuraType.ModThreat`).
- NPC vs. NPC Threat (NPCs schaden sich nicht gegenseitig in Phase A).

---

## 6. Offene Architektur-Fragen für später

- **Wie hängt Threat an Items?** Wenn Equipment in Phase B Threat-Modifier hat (Tank-Gear), muss `UnitStats` einen Threat-Multiplier exposen und `ThreatManager.AddThreat` ihn anwenden.
- **Match-State-Reset:** Ein Survivor-MOBA-Match endet → alle ThreatManager der respawnten Mobs müssen geleert werden. Hängt am späteren Match-Lifecycle (Phase D).
- **Performance bei 200–400 Enemies:** `Dictionary<ulong,int>` pro NPC ist sicher, aber 400 Dictionaries × N Player ist kein Problem. Bei >1000 Mobs könnten wir auf gepoolten Array-Storage wechseln. Erst messen.
