# Combat System — Status & Roadmap

> Stand: 2026-05-18
> Referenz-Quelle (Parity-Ziel): `c:\Users\anato\Downloads\steam-main\` (C++ MMO Server/Client)
> Owner: Riftstorm Combat Vertical

---

## 1. Zweck dieses Dokuments

Dieses Dokument hält fest:
1. **Was im Combat-System bereits live und server-autoritativ wired ist**,
2. **welche Lücken gegenüber der C++-Referenz (`source_server`/`source_client`) offen sind**,
3. **welche Reihenfolge wir abarbeiten** (5-Punkte-Plan),
4. **welche Änderungen pro Vertical schon umgesetzt wurden** (Changelog am Ende).

Pflege: Jede neue Vertical-Lieferung trägt einen Eintrag im Changelog ein, mit Datum, Subsystem, betroffenen Dateien und ggf. JSON-Schema-Erweiterungen.

---

## 2. Ist-Zustand (verifiziert aus Code, nicht vermutet)

### 2.1 Pipeline-Fundament — steht ✅

Server-autoritative Pipeline matcht das Source-Pattern aus
`source_server/Server/src/Combat/SpellCaster.cpp`:

```
Validate → ConsumeResources → StartCooldowns → ResolveTargets → ApplyEffect (pro Slot 1..3)
```

| Phase | Riftstorm | Source-Pendant |
|---|---|---|
| Validate (Stun/Silence/Range/Mana/CD/Faction/Alive) | `SpellCaster.Validate` | `SpellCaster::CheckCast` |
| Resource-Abzug (Mana, HP, HP%) | `SpellExecutor.ConsumeResources` | `SpellCaster::ApplyCosts` |
| Cooldown + GCD + Cooldown-Category | `CooldownManager.StartCooldown / StartGcd` | `CooldownManager` |
| Target-Resolve (Single/AreaSrc/AreaDst, Faction-Filter, MaxTargets) | `SpellExecutor.ResolveTargets` | `SpellUtils::ResolveTargets` |
| Effect-Loop über Slots 1..3 | `SpellExecutor.ApplyEffect` | `SpellCaster::HandleEffects` |
| Aura-Tick (DoT/HoT/Mana-Drain) | `AuraManager.ApplyPeriodicTick` | `AuraSystem::Update` |
| Aura-Replikation Server→Client | `UnitStats.BroadcastAurasClientRpc` (Parallel-Arrays) | `AuraSystem::SendAuraUpdate` |
| Aura-HUD (Self + Target) | `UnitAuraBarUI` | `BuffDebuffRenderer` |
| Cast-Failure Owner-Toast | `PlayerCombat.NotifyCastFailedClientRpc` → `OwnerCastFailed` → `CastFailedToastHUD` | `CombatMessenger::SendCastResult` |
| Damage-Floating-Text | `FloatingCombatText` | `CombatMessage` |

### 2.2 Implementierte `SpellEffect`-Handler

In [`SpellExecutor.ApplyEffect`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs) sind diese Cases live:

- ✅ `SchoolDamage` (#1)
- ✅ `WeaponDamage` (#14)
- ✅ `Heal` (#6)
- ✅ `HealPct` (#27)
- ✅ `RestoreMana` (#11)
- ✅ `RestoreManaPct` (#28)
- ✅ `ApplyAura` (#3)
- ✅ `ApplyAreaAura` (#4)
- ✅ `TriggerSpell` (#24)

### 2.3 Cast-Validation — vollständig

In [`SpellCaster.Validate`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellCaster.cs) werden alle relevanten `CastResult`-Codes erzeugt:
Stunned, Silenced, OnCooldown, NotEnoughMana, OutOfRange, InvalidTarget,
TargetAllied, CasterDead, TargetDead, GcdActive.

Deutsche UI-Strings → [`CastResultStrings.Get`](../../Assets/Scripts/Runtime/Game/Spells/CastResult.cs).

### 2.4 Aura-System — Periodics laufen

[`AuraManager.ApplyPeriodicTick`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/AuraManager.cs) tickt server-seitig:
PeriodicDamage, PeriodicMeleeDamage, PeriodicHeal, PeriodicHealPct,
PeriodicRestoreMana, PeriodicRestoreManaPct, PeriodicBurnMana.

CC-Flags exposed auf `ICombatUnit`: `IsStunned`, `IsSilenced`, `IsRooted`.

---

## 3. Offene Lücken (gegen Source-Parity)

### 3.1 Cast-Failure-Sichtbarkeit ✅ (2026-05-18)

- HUD-GameObject in `Assets/Scenes/Game.unity` angelegt (`CastFailedToastHUD`,
  UIDocument mit gemeinsamem PanelSettings, SortingOrder 100).
- Wiring vollständig: `NotifyCastFailedClientRpc` → `OwnerCastFailed` →
  `CastFailedToastHUD.OnOwnerCastFailed` → `CastResultStrings.Get`.
- Auto-Attack-Failures (RMB out-of-range etc.) feuern den Toast **nicht** —
  by design (LoL-Style: Pet walkt in Range). Bewusst gelassen.

### 3.2 CC-Gates teilweise lose ✅ (2026-05-18)

| Aura-Flag | Cast-Gate (Validate) | Movement-Gate (`PlayerMovement`) |
|---|---|---|
| `IsStunned` | ✅ | ✅ — Owner-Prediction + Server-Authority verwerfen Input |
| `IsSilenced` | ✅ | n/a |
| `IsRooted` | ✅ — via `IsImmobilized` (Stun ODER Root) im Movement-Gate | ✅ |
| Speed-Modifier (Snare/Chill/Haste) | n/a | ✅ — `ModifyMoveSpeedPct` additiv aggregiert, repliziert, in `Simulate` multiplikativ |

- `AuraManager.IsImmobilized` + `MoveSpeedMultiplier` aggregieren CC/Speed live auf dem Server.
- `UnitStats` repliziert beides über zwei kompakte `NetworkVariable`s
  (`m_CcFlags` Bitmaske, `m_MoveSpeedMultiplierMilli` als Fixed-Point ×1000).
- `PlayerMovement` konsultiert `UnitStats.IsImmobilized` sowohl im Owner-Tick
  (Prediction-Konsistenz) als auch in `SubmitCommandServerRpc` (Anti-Cheat)
  und multipliziert `m_Speed` mit `UnitStats.MoveSpeedMultiplier` an allen
  drei `Simulate`-Call-Sites inkl. Reconciliation-Replay.

**Noch offen (Polish, nicht Blocker):**
- Stun-Gate für Auto-Attacks (`PlayerCombat.AttackingState`) — Stuns brechen
  aktuell nur Casts, nicht laufende Auto-Attack-Loops.
- `NpcController.Speed` × `MoveSpeedMultiplier` — NPCs ignorieren Snares noch.

### 3.3 Nicht implementierte `SpellEffect`-Cases ⚠️ (Movement-Vertical ✅ 2026-05-19)

Fallen in den `default`-Branch von `SpellExecutor.ApplyEffect` und werden stumm verworfen.
Cast inkl. Mana-Cost + Cooldown + Cast-VFX läuft trotzdem durch — daher
"ich seh die Anim aber es passiert nichts".

**Movement-FX-Vertical (Step 3) — implementiert:**
- `ICombatUnit` erweitert um `Forward`, `ServerTeleportTo(Vector3)`,
  `ServerApplyImpulse(direction, meters, durationSec)`.
- `UnitStats` cached `PlayerMovement`/`NpcController` und delegiert die
  Interface-Aufrufe an die passende Sibling-Component.
- `PlayerMovement`: Impulse-State (Velocity + RestDauer) wird pro Frame in
  `Update` via `AdvanceImpulse` ueber alle Peers gestepped; Server pumpt
  `m_ServerPosition`, Owner invalidiert in-flight Predictions. TickOwner /
  SubmitCommandServerRpc / TickRemoteClient sind durch `IsImpulseActive`
  gegated &#8212; keine Eigenbewegung waehrend Knockback.
- `NpcController`: gleiche Impulse-Semantik in `TickServer` vor dem
  State-Switch; AI-Logik (Combat-Approach, Pathing) pausiert solange.
- `SpellExecutor.ApplyEffect` deckt jetzt:

| Effect | # | Datenmapping | Status |
|---|---|---|---|
| `Teleport` | 2 | Caster &#8594; `target.Position` | ✅ |
| `TeleportForward` | 29 | `Data1`px in `caster.Forward` | ✅ |
| `KnockBack` | 25 | `Data1`px / `Data2`ms vom Caster weg | ✅ |
| `Charge` | 37 | Caster &#8594; Target, max `Data1`px / `Data2`ms | ✅ |
| `PullTo` | 43 | Target &#8594; Caster, `Data1`px / `Data2`ms | ✅ |
| `SlideFrom` | 39 | Caster in Blickrichtung, `Data1`px / `Data2`ms | ✅ |

**Noch offen (kein Movement-Vertical):**

| Effect | # | Was dir fehlt |
|---|---|---|
| `InterruptCast` | 22 | Kick, Counterspell-Anteil |
| `Dispel` | 13 | Purge, Cleanse, Decurse |
| `ManaDrain` | 5 | Drain Mana |
| `ManaBurn` | 17 | Mana-Burn |
| `HealthDrain` | 8 | Drain Life |
| `Threat` | 18 | Taunt, Threat-Mods |
| `SummonNpc` | 10 | Pets, Totems |
| `SummonObject` | 23 | Wards, Traps |
| `ScriptEffect` | 26 | Quest/Specialcase |

### 3.4 Projektil-System existiert nicht ❌

`projectileRange` ist nur ein Datenfeld auf `UnitStats`/`MugenCharacterStats`.
Es gibt **keinen** `ServerProjectile`-NetworkBehaviour, keine Travel-Time,
keine Missile-Speed-Auflösung.

→ Fireball / Frostbolt / Pyroblast sind aktuell **hitscan**: Damage trifft
beim Cast-Ende sofort das Target, das Visual ist nur Cast-VFX am Caster
(`PlaySpellCastClientRpc`). Es fliegt nichts. Größte sichtbare Lücke
gegenüber `source_server/.../SpellCaster.cpp` + `source_client/WorldSpellAnimation`.

---

## 4. 5-Punkte-Plan (Reihenfolge ist Pflicht)

Reihenfolge maximiert "Spielgefühl pro Stunde Arbeit" — sichtbare Bugs zuerst,
dann fühlbare CC, dann Effect-Breitenarbeit, dann das große Projektil-Stück.

### Punkt 1 — Cast-Failure-Toast in Szene verifizieren ✅ (2026-05-18)

**Ziel:** Spieler sieht "Spell auf Cooldown.", "Ziel ist zu weit weg.",
"Ziel ist verbündet.", "Nicht genug Mana." etc. wieder.

**Arbeit:**
- GameScene: GameObject `CastFailedToastHUD` mit `UIDocument` +
  `CastFailedToastHUD`-Component anlegen (oder bestehendes verifizieren).
- Optional: kurzes `Debug.Log` im HUD beim Empfang, um den RPC-Pfad zu beweisen.
- Akzeptanzkriterium: Cast auf eigene Fraktion → "Ziel ist verbündet." sichtbar.

**Betroffene Dateien:** GameScene (.unity), evtl. [`CastFailedToastHUD.cs`](../../Assets/Scripts/Runtime/Game/UI/CastFailedToastHUD.cs) (nur falls Wiring-Hilfe nötig).

### Punkt 2 — CC-Enforcement schließen ✅ (2026-05-18)

**Ziel:** Stuns, Roots, Snares **fühlen sich an** wie CC.

**Geliefert:**
- `AuraManager.IsImmobilized` (Stun ODER Root) + `MoveSpeedMultiplier`
  (additiv aus allen `ModifyMoveSpeedPct`-Effekten, clamped `[0, 5]`).
- `UnitStats` repliziert CC-Flags + Speed-Multiplier über `NetworkVariable<byte>`
  / `NetworkVariable<short>` (Fixed-Point ×1000) — kompakt + Everyone-Read.
- `PlayerMovement` verwirft Move-Input bei `IsImmobilized` sowohl im
  Owner-Prediction-Tick als auch im Server-Authority-Pfad. Multiplier
  greift an allen drei `Simulate`-Aufrufen (Owner-Predict, Server-Authority,
  Reconciliation-Replay).
- Cast-Gate für `IsRooted` nicht zusätzlich notwendig — Casts laufen
  positionsunabhängig durch.

**Akzeptanz erfüllt:** Stun → kein Move; Root → kein Move; Snare −50% → halbe Speed.

**Polish-Offen (nicht Blocker):** — _erledigt 2026-05-18_
- ✅ `PlayerCombat.BeginAttack` + `ServerIsTargetStillValid` zusätzlich an
  `IsStunned` gekoppelt (Stun blockt AA-Start, mid-Windup-Stun cancelt
  Hit-Resolve). Roots erlauben AA weiterhin (FLARE-Parität).
- ✅ `NpcController.UpdateCombat` / `UpdateEvading` respektieren jetzt
  `IsStunned` (kein Move/Attack/Cast), `IsImmobilized` (kein Move, aber AA+Cast),
  und multiplizieren `WalkSpeed` mit `MoveSpeedMultiplier`.
- Server-only `UnitStats.IsStunned` Accessor ergänzt (Client → `false`,
  Server → live aus AuraManager).

**Betroffene Dateien:**
- [`AuraManager.cs`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/AuraManager.cs)
- [`UnitStats.cs`](../../Assets/Scripts/Runtime/Game/Combat/UnitStats.cs)
- [`PlayerMovement.cs`](../../Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs)
- [`PlayerCombat.cs`](../../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs)
- [`NpcController.cs`](../../Assets/Scripts/Runtime/Game/Npc/NpcController.cs)

### Punkt 3 — Movement-Effects Vertical: Teleport / Charge / KnockBack / PullTo

**Ziel:** Blink, Charge, Frost-Nova-KB, Death-Grip funktionieren.

**Arbeit:**
- Server-Helfer: `PlayerMovement.ServerTeleportTo(Vector3)` existiert bereits;
  ergänzen: `ServerApplyImpulse(Vector3 direction, float meters, float durationSec)`
  für KnockBack/PullTo/Charge/SlideFrom mit Lerp-Bewegung über N Server-Ticks.
- Neue Effect-Handler in `SpellExecutor.ApplyEffect`:
  - `Teleport` (#2): Ziel-Pos aus `eff.Data1`/Target-Pos → `ServerTeleportTo`
  - `TeleportForward` (#29): Caster.Forward * `eff.BasePoints` → `ServerTeleportTo` (mit NavMesh-Clamp)
  - `KnockBack` (#25): (Target.Pos − Caster.Pos).normalized * `eff.BasePoints` → `ServerApplyImpulse`
  - `PullTo` (#43): (Caster.Pos − Target.Pos).normalized * Distance → `ServerApplyImpulse`
  - `Charge` (#37): TeleportForward zu Target − meleeRange
  - `SlideFrom` (#39): wie KB, aber am Caster
- ClientRpc-Fanout für den Bewegungsimpuls (Visual-Reconciliation).

**Betroffene Dateien:**
- [`PlayerMovement.cs`](../../Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs)
- [`SpellExecutor.cs`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs)
- ggf. neuer `MovementImpulse.cs`-Service

### Punkt 4 — Projektil-System

**Ziel:** Fireball, Frostbolt, Pyroblast, Hammer of Wrath fliegen sichtbar
und haben Travel-Time.

**Arbeit:**
- Neuer `ServerProjectile` NetworkBehaviour: spawn am Caster-Mündungspunkt,
  Server-Tick-Movement Richtung Target (Homing) oder Richtung (Skillshot),
  bei Hit → Callback der das ursprüngliche `SpellTemplate.Effects` auf den
  getroffenen `ICombatUnit` anwendet (delegiert an `SpellExecutor.ApplyEffectsOnHit`).
- Visuals-Prefab pro Schule (Fire/Frost/Holy/...).
- `SpellExecutor` erkennt "hat Missile-Speed > 0" → statt direktem
  `ApplyEffect(primary)` einen Projektil-Spawn auslösen.
- `MaxRange` aus `SpellTemplate.MaxRange`; bei Ablauf despawn ohne Hit.

**Betroffene Dateien:**
- Neu: `Assets/Scripts/Runtime/Game/Spells/Runtime/ServerProjectile.cs`
- Neu: `Assets/Prefabs/Projectiles/*.prefab` (Addressable)
- [`SpellExecutor.cs`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs)
- [`SpellTemplate`](../../Assets/Scripts/Runtime/Game/Spells/SpellTemplate.cs) — Missile-Speed-Feld prüfen/ergänzen
- JSON-Schema-Erweiterung `_templates.json`: `missile_speed`, `missile_prefab`

### Punkt 5 — Restliche Effect-Handler: Interrupt / Dispel / Threat / Drains

**Ziel:** PvP-Toolkit + Tank-Identity vollständig.

**Arbeit:**
- `InterruptCast` (#22): `PlayerCombat.ServerInterruptCast(int lockoutMs)`
  → State-Transition aus `CastingState` raus + Lockout pro Schule via Aura.
- `Dispel` (#13): `AuraManager.ServerDispelAuras(count, dispelType)` —
  filtert auf `Positive`/`Negative` und Dispel-Type.
- `Threat` (#18): hängt von NPC-Threat-Table ab (Source: `Creature::ThreatManager`).
  Riftstorm-NPCs brauchen das nur, wenn Tanks Pflicht sind — sonst Stub.
- `ManaDrain` / `ManaBurn` / `HealthDrain` (#5/#17/#8): trivial,
  `target.SetMana/TakeDamage` + ggf. `caster.Heal/SetMana`.
- `SummonNpc` (#10) / `SummonObject` (#23): braucht
  `Addressables`-Prefab-Load + Server-Spawn — kann später, ist eigenes Vertical.

**Betroffene Dateien:**
- [`SpellExecutor.cs`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs)
- [`AuraManager.cs`](../../Assets/Scripts/Runtime/Game/Spells/Runtime/AuraManager.cs)
- [`PlayerCombat.cs`](../../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs)

---

## 5. Akzeptanzkriterien pro Vertical

| Vertical | Verifikation |
|---|---|
| 1. Toast | Cast auf Allied → "Ziel ist verbündet." sichtbar im HUD |
| 2. CC-Gates | Gestunnt → kein Move; Gerootet → kein Move; Snare 50 % → halbe Speed |
| 3. Movement-FX | Blink-Spell teleportiert; KB-Spell schiebt sichtbar zurück |
| 4. Projektile | Fireball fliegt sichtbar 1–2 s und kann verfehlen wenn Target bewegt |
| 5. Interrupt | Kick → unterbricht Cast + sperrt Schule für N Sekunden |

---

## 6. Out-of-Scope (bewusst nicht in diesem Plan)

- Pet-System (`SummonNpc`) — eigenes größeres Vertical, später.
- Threat-Table für NPCs — nur wenn Tank-Identity gewollt wird.
- `ScriptEffect` — pro Spell case-by-case, nicht generisch.
- CastBar Channel/Interrupt-State-Animationen — Polish nach Vertical 5.
- Source-Parity-Sweep gegen `source_client/BuffDebuffRenderer` Animationen
  (Aura-Pulse, Stack-Bounce) — Polish.

---

## 7. Changelog

Format: `YYYY-MM-DD | Vertical | Subsystem | Dateien | Notizen`

### 2026-05-18

- **CC-Enforcement (Stun / Root / Snare / Haste)** | Combat / Movement
  → `AuraManager`: neue Properties `IsImmobilized` und `MoveSpeedMultiplier`
  (additiv über alle `ModifyMoveSpeedPct`-Effekte, Stack-skaliert, clamped
  auf `[0, 5]`).
  → `UnitStats`: zwei neue `NetworkVariable`s (`m_CcFlags` Bitmaske,
  `m_MoveSpeedMultiplierMilli` Fixed-Point ×1000). Werden in
  `ServerOnAurasChanged` parallel zum Aura-Broadcast aus dem AuraManager
  gespiegelt. Public Accessors `IsImmobilized` / `MoveSpeedMultiplier`
  liefern auf Server live aus AuraManager, auf Clients aus NetworkVariables.
  → `PlayerMovement`: neues `[SerializeField] m_Stats`-Feld (Fallback per
  `GetComponent<UnitStats>()`). Owner-Tick verwirft Input bei
  `IsImmobilized`. `SubmitCommandServerRpc` verwirft Move-Input
  server-autoritativ. Alle drei `Simulate`-Call-Sites (Owner-Predict,
  Server-Authority, Reconciliation-Replay) multiplizieren `m_Speed`
  mit `MoveSpeedMultiplier` → Snares wirken jetzt fühlbar.
- **CastFailedToastHUD in GameScene** | UI / Scene
  → `Assets/Scenes/Game.unity`: neues Root-GameObject `CastFailedToastHUD`
  mit `UIDocument` (SortingOrder 100, gleiches PanelSettings wie CastBarHUD)
  + `CastFailedToastHUD`-Component. Cast-Failure-Toast jetzt sichtbar.
- **Aura-Replikation Server→Client** | UnitStats / AuraManager
  → `AuraManager.OnChanged`-Event, `UnitStats.BroadcastAurasClientRpc` mit
  Parallel-Primitive-Arrays, `ClientAuras` + `ClientAurasChanged`.
- **Aura-Bar HUD (Self + Target)** | UI
  → Neuer programmatischer UIToolkit-HUD `UnitAuraBarUI` mit Icon-Pool,
  Cooldown-Sweep, Stack-Label.
- **Spell-Icon-Pfad-Normalisierung** | UI
  → `NormalizeSpellIconKey` in `UnitAuraBarUI` — strippt Extension,
  forced `spell_icons/`-Prefix, matcht TextureManager-Key-Schema
  (`Art/<rel>` ohne Extension, Forward-Slashes).
- **Status-Doc erstellt** | docs
  → `docs/combat/STATUS_AND_ROADMAP.md` (dieses File).

### Vor 2026-05-18 (Auswahl)

- Cast-Failure-Toast (Server-RPC + Client-HUD + deutsche Strings).
- LoL-Style Targeting (`MobaCommandController.TryRequestAutoAttack`).
- Move-cancels-Cast mit `SpellAttributes.CanMoveWhileCasting`-Exception.
- Server-Respawn-Pfad (`m_RespawnCts`, `m_RespawnDelaySeconds`, `ServerTeleportTo`).
- Damage-Floating-Text (`FloatingCombatText` + `ClientDamageReceived`).
- Cooldown-Manager + GCD + Cooldown-Category.
- Periodic-Aura-Ticks (DoT/HoT/Mana-Drain).
- AoE-Resolve (Src/Dst, Faction-Filter, MaxTargets, OverlapSphere alloc-frei).

---

## 8. Pflege-Regel

- Jede Vertical-Lieferung trägt **vor dem Merge** ihren Changelog-Eintrag hier ein.
- Wenn ein Effect-Handler implementiert wird, in Abschnitt **2.2** verschieben.
- Wenn eine Lücke aus Abschnitt **3** geschlossen wird, dort streichen.
- Akzeptanzkriterium aus Abschnitt **5** muss vor "done" durchlaufen.
