# Riftstorm — Architektur- & Setup-Kontext

> **Stand**: Initialer Architektur-Sweep nach Port aus RemakeSoF.
> Dieses Dokument ist der **kondensierte Referenz-Kontext** aus der Setup-Session.
> Zweck: Bei späteren Claude/Copilot-Sessions als Anhang einfügen oder selbst nachlesen.

---

## 1. Vision

**Riftstorm** = Multiplayer Vampire-Survivors-MOBA im League-of-Legends-Stil.

- **Genre**: Top-down PvPvE Action
- **Spielerzahl**: 10 Spieler pro Match
- **Enemy-Scale**: 300–500 Enemies gleichzeitig
- **Performance-Target**: 60 FPS Client
- **Netcode-Tickrate**: 20–30 Hz Server-Authoritative
- **Engine**: Unity 6 + URP 17.3.0

---

## 2. Tech-Stack (Final)

### Aktive Packages (`Packages/manifest.json`)

| Package | Version | Zweck |
|---|---|---|
| `com.unity.feature.ecs` | 1.0.0 | DOTS Feature (Entities, Burst, Collections, Mathematics, Jobs, Transforms, Physics) |
| `com.unity.netcode` | 1.10.0 | **Netcode for Entities** (NfE) — Server-Authoritative Multiplayer mit Ghosts & Prediction |
| `com.unity.addressables` | 2.8.1 | Asset-Loading (Cache-first via PrefabManager) |
| `com.unity.render-pipelines.universal` | 17.3.0 | URP |
| `com.unity.inputsystem` | 1.18.0 | Input System |
| `com.unity.ai.navigation` | 2.0.10 | NavMesh (für MonoBehaviour-Bosses) |
| `com.unity.multiplayer.center` | 1.0.1 | Multiplayer Center UI |
| `com.unity.multiplayer.playmode` | 2.0.2 | **MPPM** — Virtual Players im Editor |
| `com.unity.multiplayer.tools` | 2.2.8 | Network Profiler / Debugger |
| `com.crashkonijn.goap` | 3.1.2 | GOAP für Boss-AI (MonoBehaviour-Welt) |
| `com.unity.services.multiplayer` | 2.2.2 | UGS (drin, aber **erstmal nicht genutzt**) |
| `com.unity.ugui` | 2.0.0 | Legacy UGUI (Pflicht von Unity) |

### Bewusst entfernt / nicht genutzt
- ❌ `com.unity.netcode.gameobjects` (NGO) — durch NfE ersetzt
- ❌ UGS-Services (Lobby, Relay, Matchmaking) — später, erst Direct IP / LAN / Steam P2P

### Noch zu evaluieren
- `com.unity.dedicated-server` für `MultiplayerRolesManager` — wahrscheinlich noch nicht im manifest, muss rein für Build-Time Role-Stripping.

---

## 3. Drei-Schichten-Architektur

### Layer 1: Simulation (DOTS-pur)
- **Wo**: `Runtime/Gameplay/`, `Runtime/AI/`, `Runtime/Networking/`
- **Was**: Movement, Combat, AI für Trash/Elite, Skills, Health/Damage, Netcode-Ghosts
- **Regeln**:
  - Reine `IComponentData` Structs + `ISystem`
  - `[BurstCompile]` Pflicht für jeden Hot-Path-System
  - Keine MonoBehaviour-Referenzen
  - Keine `class`-Komponenten (außer Managed-Components für Sonderfälle)
  - Server-Authoritative via `[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]`
  - Client-Prediction via `PredictedSimulationSystemGroup`

### Layer 2: Presentation (MonoBehaviour)
- **Wo**: `Runtime/UI/`, `Runtime/Core/`, `Runtime/Metagame/`, `Runtime/Game/`
- **Was**: UI Toolkit Screens, Audio, VFX, Camera, Input-Reading, Scene-Management
- **Regeln**:
  - MVC-Pattern (BaseApplication, Model, View, Controller, Element)
  - UI Toolkit für Screens (kein UGUI für neue UI)
  - Keine Gameplay-Logik — nur Darstellung + User-Input-Capture
  - Kommuniziert mit Sim via Bridge

### Layer 3: Infrastructure & Bridge
- **Wo**: `Runtime/Management/`, `Runtime/ApplicationLifecycle/`, `Runtime/Gameplay/Bridge/`
- **Was**: ServiceLocator, PrefabManager, TextureManager, DataManager, Bootstrap, MB↔DOTS-Bridge
- **Regeln**:
  - Pure Services (keine MonoBehaviours) via ServiceLocator
  - Cache-first für alle Assets
  - Bridge übersetzt MB-Events → DOTS-Components und umgekehrt

---

## 4. Asmdef-Graph (azyklisch)

```
Shared (no deps)
  ↑
Core (Shared + UI Toolkit)
  ↑
Management (Core + Shared + Addressables)
  ↑
Gameplay (Shared + DOTS-Packages, allowUnsafeCode)
  ↑
AI (Shared + Gameplay + DOTS, allowUnsafeCode, + CrashKonijn.Goap)
  ↑
Networking (Shared + Gameplay + NetCode + Transport + DOTS)
  ↑
UI (Core + Shared)
  ↑
Metagame (Core + Shared + Management + UI + InputSystem)
Game     (Core + Shared + Management + Gameplay + AI + Networking + UI + DOTS + InputSystem)
  ↑
ApplicationLifecycle (alle obigen + Addressables + DOTS + Collections + Burst + Mathematics)
```

**Status**: 10 Asmdefs angelegt, Referenzen sauber, zyklusfrei.

---

## 5. Ordnerstruktur

```
Assets/
├── Art/                          Models, Textures, Materials, VFX, Audio
│   └── Animations/               (Animator Controllers + Clips hierhin)
├── Data/                         ScriptableObjects (Heroes, Enemies, Skills, Waves)
├── Prefabs/                      Entity-Prefabs + MonoBehaviour-Prefabs
├── Scenes/
│   ├── Boot.unity                ApplicationEntryPoint
│   ├── Metagame.unity            Login, Hero-Select, Lobby
│   └── Game.unity                Match-Scene mit SubScenes
├── Settings/                     URP, Input Actions, Quality
├── StreamingAssets/
│   └── Data/                     JSON Source-of-Truth (Heroes, Enemies, Skills)
└── Scripts/
    └── Runtime/
        ├── Shared/               Constants, Interfaces, Utilities
        ├── Core/                 MVC Base-Klassen + UI Toolkit Helper
        ├── Management/           ServiceLocator + Pure Services (Prefab/Texture/Data)
        ├── Gameplay/
        │   ├── Authoring/        MonoBehaviour-Authoring + Baker
        │   ├── Bridge/           MB ↔ DOTS Bridge
        │   └── Sim/              IComponentData + ISystem (DOTS-pur)
        ├── AI/
        │   ├── Brains/           GOAP-Brains für Bosse
        │   ├── Bosses/           MonoBehaviour-Boss-Logik
        │   ├── Components/       DOTS AI-Components
        │   └── Systems/          DOTS AI-Systems
        ├── Networking/           NfE Bootstrap + Custom RPCs + Ghost-Setup
        ├── UI/                   UI Toolkit Views/Controls (scene-unabhängig)
        ├── Metagame/             Login, Hero-Select-Scene-Logik
        ├── Game/                 Match-Scene-Logik + MB↔DOTS-Bridge
        └── ApplicationLifecycle/ Bootstrap + ServiceLocator-Init
```

### Cleanup-TODOs
- [ ] `Assets/DefaultNetworkPrefabs.asset` löschen (NGO-Leftover, NfE nutzt es nicht)
- [ ] `Assets/Animator/` → `Assets/Art/Animations/` verschieben
- [ ] UGS-Entscheidung treffen: drin lassen oder raus

---

## 6. Drei Multiplayer-Systeme (NICHT verwechseln!)

| System | Layer | Steuert |
|---|---|---|
| **`MultiplayerRolesManager`** | **Build-Time** | Was wird in den Build gepackt? Server-Build vs. Client-Build (Code/Asset-Stripping) |
| **MPPM (Multiplayer Play Mode)** | **Editor-PlayMode** | Virtuelle Player-Instanzen im Editor, Player-Tags, Rollen-Dropdown |
| **NfE `ClientServerBootstrap`** | **Runtime** | Welche World wird erstellt? `ClientWorld`, `ServerWorld`, `ClientAndServer`, `ThinClient` |

### MPPM Player Tags
- Werden **NICHT** im `TagManager.asset` angelegt
- Müssen im MPPM-Window manuell pro Virtual Player getippt werden
- Abrufbar via `CurrentPlayer.ReadOnlyTags().Contains("Server")` (Namespace `Unity.Multiplayer.Playmode`)

### MPPM Rolle (Server/Client/ClientAndServer)
- Im MPPM-Window pro Virtual Player im **Role-Dropdown** wählbar
- NfE liest das automatisch via `MultiplayerPlayModePreferences.RequestedPlayType`

---

## 7. Datenfluss-Pipeline

```
StreamingAssets/Data/heroes/hero_01.json   (Source of Truth, editierbar)
        │
        ▼
DataManager liest JSON beim Bootstrap
        │
        ▼
ScriptableObject (HeroDefinition) im Memory-Cache
        │
        ▼
Authoring-MonoBehaviour referenziert ScriptableObject
        │
        ▼
Baker übersetzt → IComponentData (Burst-kompatibel, BlobAsset wenn nötig)
        │
        ▼
DOTS-Systems lesen Components im Hot-Path
```

**Wichtig**: JSON wird **NIE** in DOTS-Hot-Path geparst.

---

## 8. Build-Order (Bottom-Up)

### Phase 0: Fundament
1. `ApplicationEntryPoint` minimal (MultiplayerRolesManager-Switch + ServiceLocator + Scene-Load)
2. 3 leere Scenes: Boot, Metagame, Game
3. Build Settings: Scenes in Reihenfolge

### Phase 1: DOTS + NfE-Host gleichzeitig ← **OPTIMIERTER WEG**
1. `RiftstormBootstrap : ClientServerBootstrap` mit `ClientAndServer` Default
2. SubScene in Game.unity mit Test-Cube
3. `MoveSpeedComponent` + `LocalTransform` + `[GhostField]`
4. `MovementSystem` mit `[BurstCompile]` + `[WorldSystemFilter(ServerSimulation)]` + ProfilerMarker
5. Stresstest-Scene mit 1000 Dummy-Entities → FPS-Baseline

### Phase 2: Player Input + Prediction
1. `PlayerInputCommand : ICommandData`
2. `PredictedSimulationSystemGroup` für Player-Movement
3. `[GhostComponent(PrefabType = AllPredicted)]` am Player

### Phase 3: Daten-getriebenes Authoring
1. `HeroDefinition` ScriptableObject mit allen Stats
2. `HeroAuthoring : MonoBehaviour` + Baker liest aus Definition
3. JSON→ScriptableObject Pipeline via DataManager

### Phase 4: Enemies + AI
1. `EnemyDefinition` ScriptableObject
2. `EnemySpawnSystem` Server-only, liest aus `WaveDefinition`
3. AI als DOTS-State-Machine (Enum-Component + System-pro-State)
4. Boss-AI: MonoBehaviour + GOAP via Bridge

### Phase 5: Skills via Composition
1. `AbilityComponent`, `CooldownComponent`
2. Skill-Definition = Liste von Effects (`DamageEffect`, `KnockbackEffect`, `SlowEffect`, …)
3. `AbilitySystem` instanziiert Effects als Entity-Components
4. **Kein** Monolithic-Skill-Klassen-Pattern

---

## 9. AAA-Standard-Abgleich

### Was AAA-Standard ist (und du machst)
- ✅ Bottom-up Vertical Slice
- ✅ Server-Authoritative von Tag 1
- ✅ Data-Oriented Design (DOTS/ECS) — wie Overwatch, Diablo IV, Destiny 2
- ✅ Strikte Trennung Sim / Presentation
- ✅ Authoring + Baking Pipeline
- ✅ Build-Time Role-Stripping
- ✅ Fixed Tick Simulation getrennt von Render-FPS

### Was AAA hat (und du erstmal nicht brauchst)
- ⚠️ Custom Engine (Riot, Frostbite, Source 2) — du: Unity DOTS (best-in-class für Indie)
- ⚠️ Server-Rewind Lag-Compensation — NfE macht's nicht out-of-the-box
- ⚠️ Anti-Cheat (BattlEye, EAC, Vanguard)
- ⚠️ Backend-Services (Matchmaking, Ranked, Telemetrie) als Microservices
- ⚠️ Audio-Middleware (Wwise/FMOD)
- ⚠️ Deterministic Lockstep für Esports-Replays

### Realistische Zielsetzung
**Indie/AA-Tier mit AAA-Patterns** — wie Hades, Risk of Rain 2, Deep Rock Galactic.
Nicht: Riot-Tier Esport-Titel. Solo/Small-Team mit Unity erreicht das nicht.

---

## 10. Anti-Patterns (NICHT machen)

- ❌ MonoBehaviour-Components für DOTS-Entities mischen (außer explizit als Hybrid mit Begründung)
- ❌ JSON in DOTS-Hot-Path parsen
- ❌ Coroutines für Gameplay-Flow (stattdessen State Machines)
- ❌ `Update()` mit Polling-Checks (stattdessen Events)
- ❌ Singleton-Zugriffe über `ApplicationEntryPoint.Singleton` (stattdessen ServiceLocator)
- ❌ Magic Numbers / Strings (stattdessen Konstanten/ScriptableObjects)
- ❌ Monolithische Skill-Klassen (stattdessen Effect-Composition)
- ❌ Client-Side Damage/Hit-Detection (immer Server-Authoritative)
- ❌ LINQ in Gameplay-Hot-Paths
- ❌ Per-Frame Heap-Allocations in Gameplay-Loops
- ❌ Reflection in Runtime-Systems

---

## 11. Burst Compiler — Erinnerung

- **Im Editor abschaltbar** via `Jobs` → `Burst` → `Enable Compilation` (Toggle, **per Session**, nicht im Projekt gespeichert)
- **Empfehlung**: An lassen. Burst-Disable nur für temporäre Debug-Sessions mit Breakpoints in BurstCompile-Code
- **In Builds**: Über `BurstAotSettings` per Platform konfigurierbar (Project Settings → Burst AOT Settings)

---

## 12. Offene Entscheidungen / TODOs

- [ ] `com.unity.dedicated-server` ins manifest aufnehmen (für `MultiplayerRolesManager`)
- [ ] UGS entfernen oder behalten (aktuell drin, ungenutzt)
- [ ] `DefaultNetworkPrefabs.asset` löschen
- [ ] `Animator/` → `Art/Animations/` verschieben
- [ ] Erste SubScene in `Game.unity` anlegen
- [ ] `RiftstormBootstrap` schreiben
- [ ] Erste DOTS Components + System (Movement)
- [ ] Stresstest-Scene mit 1000 Dummy-Entities
- [ ] JSON→ScriptableObject Baking Pipeline
- [ ] Vertical Slice: 1 Hero, 1 Map, 1 Boss, server-authoritativ
