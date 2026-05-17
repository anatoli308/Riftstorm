# 05 – Roadmap: Gameplay-Systeme für Riftstorm

Status-Snapshot und Phasenplan für den Aufbau der MMO/MOBA-Gameplay-Systeme (Combat, Stats, Spells, Items, AI, Quests, Persistenz) im Unity-Projekt Riftstorm. Die Vorlagen-Codebase ist ein eigenes vorheriges Multiplayer-Projekt in C++ — hier dient sie nur als Architektur-Referenz, nicht als 1:1-Port-Ziel.

Dient als Erinnerungs-/Kontextdokument für spätere Sessions.

---

## 1. Stand der Basis-Systeme

| System | Vorbild | Riftstorm-Status |
|---|---|---|
| Networking-Fundament | (eigene NetworkManager-Klassen) | ✅ NGO + [ConnectionManager](../Assets/Scripts/Runtime/Management/) StateMachine |
| Auth/Login | `AccountDb`, Login-Pakete | ✅ [AuthenticationManager](../Assets/Scripts/Runtime/Management/) StateMachine |
| Bewegung | (eigener Pred./Reconcile) | ✅ [PlayerMovement](../Assets/Scripts/Runtime/Game/) (CSP + Reconcile) |
| Sprite/Anim | FLARE `.sa`-Scripts | ✅ FlareCharacter |
| Console | Chat-Befehle | ✅ ConsoleManager StateMachine |
| Waffen-Daten (read-only) | `ItemTemplate.h` | ✅ [WeaponDefinition](../Assets/Scripts/Runtime/Gameplay/Combat/) + WeaponCatalog (JSON) |
| Offhand/Shield | `ItemTemplate.h` (subtype) | ✅ OffhandCatalog (JSON) |
| Basis-Attacke | `SpellCaster.cpp` (`SPELL_MELEE`) | ✅ **Phase 3** — Input → Server → Cooldown → Anim |

---

## 2. Abgeschlossene Phasen

### ✅ Phase 1 — Waffen-Daten (read-only)
- `WeaponDefinition` / `WeaponCatalog` / `WeaponCatalogLoader` (Pure Service, JSON aus `StreamingAssets/combat/weapons.json`).
- Registriert via `ServiceLocator` in [ApplicationEntryPoint](../Assets/Scripts/Runtime/ApplicationLifecycle/ApplicationEntryPoint.cs).

### ✅ Phase 2 — Visual-Layer (lokal, nicht-autoritativ)
- `PlayerCombatVisuals` triggert Swing/Shoot/Cast am `FlareCharacter`.
- Binding über `BindCharacter(...)` aus dem Bootstrap.
- Offhand-Catalog parallel angelegt (`OffhandCatalogLoader`, JSON).

### ✅ Phase 3 — Server-autoritative Combat-StateMachine
- **Neu:**
  - [NetworkStateMachine.cs](../Assets/Scripts/Runtime/Core/NetworkStateMachine.cs) — parallele Basisklasse zu `StateMachine<,>`, aber für `NetworkBehaviour`. Gleiche API (`InitializeStates` / `ChangeState` / `EventManager`).
  - [PlayerCombat.cs](../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs) — `NetworkVariable<FixedString64Bytes>` für Weapon/Offhand (server-write), `[ServerRpc] RequestAttackServerRpc`, `[ClientRpc] PlayAttackClientRpc(CombatAnim)`.
  - [PlayerCombatState.cs](../Assets/Scripts/Runtime/Game/Combat/CombatStates/PlayerCombatState.cs) — abstrakte Basis mit `OnAttackRequested` / `OnDeath` / `OnRespawn` No-Op-Defaults.
  - [PlayerCombatIdleState.cs](../Assets/Scripts/Runtime/Game/Combat/CombatStates/PlayerCombatIdleState.cs) — akzeptiert Attack → `Manager.BeginAttack(weapon)`.
  - [PlayerCombatAttackingState.cs](../Assets/Scripts/Runtime/Game/Combat/CombatStates/PlayerCombatAttackingState.cs) — Cooldown via `await Awaitable.WaitForSecondsAsync(cd, token)`. **Kein Polling, keine Coroutine, kein Flag-Check.** `CancellationTokenSource` wird im `Exit` sauber gekillt.
  - [PlayerCombatDeadState.cs](../Assets/Scripts/Runtime/Game/Combat/CombatStates/PlayerCombatDeadState.cs) — absorbierend, verlässt nur über `OnRespawn`.
- **Geändert:**
  - [PlayerInputController.cs](../Assets/Scripts/Runtime/Game/Input/PlayerInputController.cs) — `Attack`-Action eingehängt, `public event Action AttackPressed`. Sauberes Subscribe/Unsubscribe in `OnEnable`/`OnDisable`.
  - [GamePlayerBootstrap.cs](../Assets/Scripts/Runtime/Game/Bootstrap/GamePlayerBootstrap.cs) — bindet zusätzlich `PlayerCombatVisuals.BindCharacter(...)` + `PlayerCombat.BindVisuals/BindInput`.
- **asmdef-Fixes:**
  - `Riftstorm.Core` referenziert jetzt `Unity.Netcode.Runtime` (für `NetworkStateMachine`).
  - `Riftstorm.Game` referenziert jetzt `Unity.Collections` (für `FixedString64Bytes`) und `Riftstorm.ApplicationLifecycle` (für `ServiceLocator`).

### Daten-Flow (Phase 3)
1. Owner drückt Attack → `PlayerInputController.AttackPressed`
2. → `PlayerCombat.RequestAttackServerRpc()`
3. Server löst Waffe via `ServiceLocator.Get<WeaponCatalogLoader>().GetCached().Get(id)` auf
4. → `m_CurrentState.OnAttackRequested(weapon)` (Idle akzeptiert, Attacking/Dead verwerfen)
5. Idle → `Manager.BeginAttack(weapon)` → `PlayAttackClientRpc(CombatAnim)` an alle Clients → Visuals spielen Swing/Shoot/Cast
6. State wechselt zu Attacking → kickt `Awaitable.WaitForSecondsAsync(AttackCooldown)` → nach Ablauf zurück zu Idle

### Editor-Setup (einmalig pro Prefab)
1. `PlayerCombat`- und `PlayerCombatVisuals`-Komponente auf den `PlayerCharacter`-Prefab-Root (beide am Root, neben `PlayerMovement` und `PlayerInputController`). `RequireComponent` zieht die Visuals automatisch nach.
2. Beim `PlayerCombat`: `m_DefaultWeaponId` auf einen Key setzen, der in `StreamingAssets/combat/weapons.json` existiert (Default: `"longsword"`).

---

## 3. Offene Phasen (geplante Port-Reihenfolge)

Jeweils data-driven via JSON in `StreamingAssets/` + StateMachine, wo passend.

### ⏳ Phase 4 — Hit-Resolution & Damage  *(nächster Schritt)*
- C++-Vorbild: `CombatFormulas.cpp`.
- Hitscan/Overlap im `PlayerCombatAttackingState` bei `HitResolveProgress` (Zeitpunkt im Anim-Verlauf, an dem der Schaden zählt).
- `BaseDamage` aus `WeaponDefinition`.
- Damit ist die Attack-Kette fertig: Input → Server → Anim → **Treffer + Damage** → Cooldown.

### ⏳ Phase 5 — Stats / Attribute
- C++-Vorbild: `MutualUnit.cpp` (HP/MP/STR/...).
- `UnitStats : NetworkBehaviour` mit HP/MP/Level/Regen als `NetworkVariable`.
- `IDamageable`-Interface, damit Damage aus Phase 4 irgendwo landet.

### ⏳ Phase 6 — Spell / Ability + Cooldown-Manager
- C++-Vorbild: `SpellCaster.cpp` (generischer Spell-Pfad jenseits von `SPELL_MELEE`).
- `SpellDefinition` JSON (Cast-Time, Cooldown, Effects, VFX-Anim).
- Generischer Cast-State (vereinheitlicht Melee/Range/Cast).
- `CooldownManager` als per-Player Service.

### ⏳ Phase 7 — Aura / Buff / Debuff
- C++-Vorbild: `AuraSystem.cpp`, `BuffDebuffRenderer.cpp`.
- Zentrale `AuraSystem`-Komponente, `NetworkList<>` aktiver Auras.
- Tick via Awaitable-Loop pro Aura (kein `Update`-Polling).
- Assets (Buff-Icons etc.) sind im Projekt bereits vorhanden, Loader fehlt.

### ⏳ Phase 8 — Inventar + Loot
- C++-Vorbild: `ItemTemplate.h` (geteilte Item-Daten).
- `ItemDefinition` JSON (Erweiterung des bestehenden Weapon/Offhand-Schemas).
- `Inventory : NetworkBehaviour` mit `NetworkList<>`.
- `LootTable` JSON, drops über Server bei NPC-Tod.

### ⏳ Phase 9 — NPC AI + Threat
- C++-Vorbild: NPC-AI-Module + Threat-System.
- `NpcAI : NetworkStateMachine` (Idle / Patrol / Combat / Flee).
- `ThreatManager` pro NPC.

### ⏳ Phase 10 — Quests + Gossip / Vendor
- `QuestDefinition` JSON, `QuestLog : NetworkBehaviour`.
- Gossip/Vendor als UI-Layer auf NPC AI.

### ⏳ Phase 11 — Persistenz
- SQLite (oder bevorzugtes Backend) hinter `ICharacterRepository`.
- Async `SaveJob` (keine Coroutine, kein Polling).

### ⏳ Phase 12 — HUD
- UIToolkit-basiert: CastBar, Minimap, BuffIcons.
- Binden direkt an die bereits vorhandenen `NetworkVariable`s.

---

## 4. Status der Map-/Welt-Systeme

| System | Vorbild | Riftstorm-Status |
|---|---|---|
| Maps | `MapCellClient.cpp`, `MapEditor.cpp` | ⏳ Unity-Scenes vorhanden, kein Cell-System portiert |

Map-Port bleibt explizit außerhalb der Phasen 4–12 — Unity macht Streaming via Scenes/Addressables nativ, ein 1:1-Port der Cell-Logik ist nicht zwingend.

---

## 5. Projektregeln, die im Port gelten

- **No Polling, No Coroutines** → `await Awaitable.WaitForSecondsAsync(seconds, CancellationToken)`.
- **No Timer / Flag Checks** → StateMachine, Events, Callbacks.
- **Single Source of Truth** → Manager hält State, Events sind nur Trigger.
- **Server is authoritative** → Clients senden nur Input-Intent.
- **Data-driven** → Waffen/Spells/Items/Loot als JSON in `StreamingAssets/`.
- **Cache-first** über `PrefabManager` / `TextureManager` via `ServiceLocator`.
- **State-spezifische Interfaces** für State-Inputs; Output-Events kommen vom Manager (nicht vom State) via `EventManager`.
