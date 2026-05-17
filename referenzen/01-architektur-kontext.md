# Riftstorm — Architektur- & Setup-Kontext

> **Stand**: Nach ECS-Cut. Riftstorm läuft jetzt **NGO-only** mit klassischen MonoBehaviours.
> Zweck dieses Dokuments: Kondensierter Referenz-Kontext für spätere Copilot/Claude-Sessions.

---

## 1. Vision

**Riftstorm** = Multiplayer Dark-Fantasy Survivor-MOBA/MMO im LoL/WoW-Stil mit ARPG-Elementen — Vampire-Survivors-/Megabonk-Loop für PvE, MOBA-Pacing fürs PvP.

- **Genre**: Top-down PvPvE Action (Survivor-MOBA/MMO-Hybrid)
- **Spielerzahl**: ~15 pro Match (skalierbar nach oben/unten)
- **Enemy-Scale**: 200–400 Enemies gleichzeitig
- **Performance-Target**: 60 FPS Client
- **Netcode-Tickrate**: 20–30 Hz Server-Authoritative
- **Engine**: Unity 6 + URP 17.3.0

---

## 2. Tech-Stack (Final)

### Aktive Packages (`Packages/manifest.json`)

| Package | Version | Zweck |
|---|---|---|
| `com.unity.netcode.gameobjects` | 2.11.2 | **NGO** — Server-Authoritative Multiplayer (NetworkBehaviour, NetworkVariable, RPCs) |
| `com.unity.dedicated-server` | 2.0.2 | `MultiplayerRolesManager` für Build-Time Server/Client-Split |
| `com.unity.addressables` | aktuell | Asset-Loading (Cache-first via `PrefabManager`) |
| `com.unity.render-pipelines.universal` | 17.3.0 | URP |
| `com.unity.inputsystem` | aktuell | Input System |
| `com.unity.ai.navigation` | aktuell | NavMesh (für Bosses/Enemies) |
| `com.unity.multiplayer.center` | aktuell | Multiplayer Center UI |
| `com.unity.multiplayer.playmode` | aktuell | **MPPM** — Virtual Players im Editor |
| `com.unity.multiplayer.tools` | aktuell | Network Profiler / Debugger |

### Bewusst entfernt
- ❌ `com.unity.feature.ecs` (DOTS Meta-Feature) — vollständig gecuttet
- ❌ `com.unity.netcode` (Netcode for Entities / NfE) — gegen NGO ersetzt
- ❌ Burst, Jobs, Collections, Mathematics, Transforms, Physics (ECS-Physik), Entities.Graphics

### Noch offen
- UGS (Lobby/Relay/Matchmaking) — erstmal **nicht** genutzt. Direct IP / LAN / Steam P2P reicht für MVP.
- Anti-Cheat (EAC, BattlEye) — später.

---

## 3. Zwei-Schichten-Architektur (NGO-only)

### Layer 1: Game Simulation (Server-Authoritative MonoBehaviour)
- **Wo**: `Runtime/Game/Networked/`, `Runtime/Game/Characters/`, `Runtime/Game/Controllers/`, `Runtime/AI/`
- **Was**: Player-Movement (Server-Validation), Combat, Enemy-AI, Skills, Health/Damage
- **Regeln**:
  - Authoritative State liegt **immer** auf dem Server
  - `NetworkBehaviour` + `NetworkVariable<T>` + `ServerRpc` / `ClientRpc`
  - Clients senden nur Inputs, Server simuliert, Clients interpolieren
  - Fixed Tick (20–30 Hz) für Simulation, getrennt von Render-FPS
  - Object Pooling für Projektile/Enemies/FX

### Layer 2: Presentation + Bridge (MonoBehaviour + MVC)
- **Wo**: `Runtime/Core/`, `Runtime/Game/Views/`, `Runtime/Game/Models/`, `Runtime/Metagame/`, `Runtime/UI/`
- **Was**: UI Toolkit Screens, Audio, VFX, Camera, Input-Reading, Scene-Management, Lobby
- **Regeln**:
  - MVC-Pattern (`BaseApplication`, `Model`, `View`, `Controller`, `Element`)
  - UI Toolkit für Screens (kein UGUI für neue UI)
  - Keine Gameplay-Logik in Views — nur Darstellung + Input-Capture
  - State-Manager halten Single Source of Truth, Events triggern nur

### Layer 3: Infrastructure
- **Wo**: `Runtime/ApplicationLifecycle/`, `Runtime/Management/`, `Runtime/Shared/`
- **Was**: ServiceLocator, PrefabManager, TextureManager, DataManager, ConnectionManager, AuthenticationManager, ConsoleManager
- **Regeln**:
  - Pure Services (keine MonoBehaviours) via `ServiceLocator.Register<T>()` / `Get<T>()`
  - MonoBehaviour-Manager als serialisierte Felder im `ApplicationEntryPoint`
  - Cache-first für alle Assets

---

## 4. Ordnerstruktur

```
Assets/Scripts/
├── Editor/
│   ├── BuildHelpers.cs            # Editor-Build-Utilities
│   ├── BuildProcessor.cs          # IPreprocessBuildWithReport (Server/Client-Stripping)
│   ├── CloudBuildHelpers.cs       # optional, falls Unity Cloud Build
│   └── SceneBootstrapper.cs       # Auto-Load Boot-Scene im Editor
└── Runtime/
    ├── AssemblyInfo.cs            # InternalsVisibleTo etc.
    ├── ApplicationLifecycle/
    │   ├── ApplicationEntryPoint.cs       # Singleton, DontDestroyOnLoad, Bootstrap
    │   ├── ServiceLocator.cs              # Pure-Service Container
    │   ├── ServerCommandListener.cs       # Dedicated-Server stdin Konsole
    │   └── NetworkStatsMonitorInitializer.cs
    ├── Core/
    │   ├── BaseApplication.cs             # Root für Scene-Scripts, EventManager-Owner
    │   ├── Element.cs                     # Gemeinsame MVC-Basis
    │   ├── Model.cs / Model<T>            # Datenhaltung
    │   ├── View.cs / View<T>              # UI-Darstellung (MonoBehaviour, UIToolkit)
    │   ├── Controller.cs / Controller<T>  # MVC-Bridge, EventManager-Listener
    │   ├── EventManager.cs                # Typ-sichere Events
    │   ├── State.cs / State<TManager>     # State-Machine-State
    │   └── StateMachine.cs                # StateMachine<TState, TSelf> (CRTP)
    ├── Management/
    │   ├── ConnectionManager.cs           # NGO NetworkManager Wrapper, StateMachine
    │   ├── AuthenticationManager.cs       # StateMachine, Token-Refresh
    │   ├── ConsoleManager.cs              # In-Game Konsole, StateMachine
    │   └── PlayerSkinManager.cs           # StateMachine + Loader/Applier
    ├── AI/
    │   ├── AIBotController.cs             # Top-Level Bot-Logik
    │   ├── Audio/  EANN/  GOAP/  Personality/  Sensors/
    ├── Game/                              # Match-Scene
    │   ├── GameApplication.cs             # BaseApplication<GameModel, GameView, GameController>
    │   ├── GameEvents.cs                  # Event-Typen
    │   ├── Bootstrap/                     # GamePlayerBootstrap.cs (Layer-Atlases)
    │   ├── Camera/                        # TopdownCameraFollow
    │   ├── Characters/                    # Player + Enemy NetworkBehaviour-Prefabs
    │   ├── Controllers/                   # PlayerInputController, PlayerMovement
    │   ├── Effects/                       # VFX-Spawner (client-side)
    │   ├── Environment/                   # Map-spezifische Logik
    │   ├── MapLoader/                     # Map-Streaming
    │   ├── Models/                        # GameModel + Sub-Models
    │   ├── Networked/                     # NetworkBehaviours, RPCs, NetworkVariables
    │   ├── Projectiles/                   # Server-authoritative Projectile Pool
    │   ├── Sprites/                       # FlareAtlas, FlareLayerAnimator, FlareCharacter
    │   ├── Views/                         # GameView + Sub-Views (HUD, Death-Screen, etc.)
    │   └── WeaponLoader/                  # Weapon-Definition-Loader
    ├── Metagame/                          # Login, Hero-Select, Lobby
    │   ├── MetagameApplication.cs
    │   ├── Models/  Views/  Controllers/
    └── Shared/                            # Cross-Scene DTOs, AvatarActions, Konstanten
```

---

## 5. Asmdef-Graph (azyklisch, NGO-only)

```
Shared (no deps)
  ↑
Core (Shared + UI Toolkit)
  ↑
Management (Core + Shared + Addressables + Unity.Netcode)
  ↑
Gameplay (Shared + URP + InputSystem)           # ggf. mit Game verschmolzen
  ↑
AI (Shared + Gameplay)
  ↑
Networking (Core + Shared + Gameplay + Unity.Netcode)   # ggf. in Game/Networked/ aufgelöst
  ↑
UI (Core + Shared)
  ↑
Metagame (Core + Shared + Management + UI + InputSystem)
Game     (Core + Shared + Management + Gameplay + AI + Networking + UI + InputSystem + Unity.Netcode)
  ↑
ApplicationLifecycle (alle obigen + Addressables + InputSystem)
```

> **NGO-Assembly heißt `Unity.Netcode`** (lowercase „c"), **nicht** `Unity.NetCode` (das wäre NfE).
> Diese Referenz muss in `Riftstorm.Management.asmdef`, `Riftstorm.Networking.asmdef` und `Riftstorm.Game.asmdef`, sobald `NetworkBehaviour`-Code geschrieben wird.

---

## 6. Drei Multiplayer-Systeme (NICHT verwechseln!)

| System | Layer | Steuert |
|---|---|---|
| **`MultiplayerRolesManager`** (`com.unity.dedicated-server`) | **Build-Time** | Was wird in den Build gepackt? Server-Build vs. Client-Build (Code/Asset-Stripping) |
| **MPPM (Multiplayer Play Mode)** | **Editor-PlayMode** | Virtuelle Player-Instanzen im Editor, Player-Tags, Rollen-Dropdown |
| **NGO `NetworkManager`** | **Runtime** | StartHost / StartServer / StartClient, Connection Approval, Scene Sync |

### MPPM Player Tags
- Werden **NICHT** im `TagManager.asset` angelegt
- Müssen im MPPM-Window manuell pro Virtual Player getippt werden
- Abrufbar via `CurrentPlayer.ReadOnlyTags().Contains("Server")` (Namespace `Unity.Multiplayer.Playmode`)

### MPPM Rolle (Server/Client/ClientAndServer)
- Im MPPM-Window pro Virtual Player im **Role-Dropdown** wählbar
- `ApplicationEntryPoint` liest die Rolle und ruft `StartServer()` oder `StartClient()` am `NetworkManager`

---

## 7. Dedicated-Server-Build (Server-Only / Client-Only)

### Konfiguration
- Build Profile: **"Windows Server"** (`Assets/Settings/Build Profiles/Windows Server.asset`)
- Subtarget: `Server` (Dedicated Server, Headless, kein Rendering)
- Scripting Define: `UNITY_SERVER` (automatisch gesetzt)

### Code-Pattern für Role-Stripping
```csharp
#if UNITY_SERVER
    networkManager.StartServer();
    SceneManager.LoadScene("Game");
#else
    networkManager.StartClient();
    SceneManager.LoadScene("Metagame");
#endif
```

Oder runtime-basiert über `MultiplayerRolesManager.ServerRoleEnabled`.

### CommandLineArgumentsParser
- Liest `--port <int>` (Default 7777)
- Liest `--target-framerate <int>` (Default 30 für Server)
- Triggert beim Server-Start: `Application.targetFrameRate`, `QualitySettings.vSyncCount = 0`, `NetworkManager.StartServer()`

---

## 8. Datenfluss-Pipeline

```
StreamingAssets/Data/*.json        (Source of Truth, editierbar)
        │
        ▼
DataManager (Pure Service) liest JSON beim Bootstrap
        │
        ▼
ScriptableObject / DTO im Memory-Cache
        │
        ▼
Prefab / NetworkBehaviour referenziert Daten via ServiceLocator
        │
        ▼
Server-Logic liest Stats, Client interpoliert + rendert
```

JSON wird **NIE** in Gameplay-Hot-Path geparst. Immer beim Bootstrap einmal laden.

---

## 9. Build-Order (Bottom-Up)

### Phase 0: Fundament ✅ (teils erledigt)
1. `ApplicationEntryPoint` mit MultiplayerRoles-Switch + ServiceLocator + Scene-Load
2. 3 leere Scenes: Boot, Metagame, Game
3. Build Settings: Scenes in Reihenfolge

### Phase 1: NGO-Bootstrap
1. `ConnectionManager` (StateMachine: Disconnected / Connecting / Connected / Failed)
2. `NetworkManager` Prefab mit Unity Transport
3. `StartServer()` auf Dedicated-Build, `StartClient()` mit Direct-IP-Connect auf Client

### Phase 2: Player Spawning + Server-Authoritative Movement
1. `Player.prefab` mit `NetworkObject` + `PlayerNetworked : NetworkBehaviour`
2. Client sendet Input via `ServerRpc(InputCommand input)`
3. Server simuliert Movement, sendet Position via `NetworkVariable<Vector3>` oder ClientRpc
4. Client-Prediction + Reconciliation (manuell, da NGO das nicht out-of-the-box hat)
5. `TopdownCameraFollow` client-side am lokalen Player

### Phase 3: Combat + Skills
1. `WeaponNetworked` mit Server-authoritativem Fire-Cooldown
2. Hit-Detection server-side (Raycast/Overlap)
3. Damage über `NetworkVariable<int> Health` mit OnValueChanged → Client-VFX
4. Skills als Composition (`DamageEffect`, `KnockbackEffect`, `SlowEffect`)

### Phase 4: Enemies + AI
1. `EnemyNetworked` Object Pool (Server spawnt, Clients sehen via NetworkObject)
2. Server-side AI mit NavMeshAgent + State Machine
3. Boss-AI optional via GOAP (`com.crashkonijn.goap` falls eingebunden)

### Phase 5: Daten-getriebene Content-Pipeline
1. `HeroDefinition`, `EnemyDefinition`, `WeaponDefinition` als ScriptableObject + JSON
2. `DataManager` lädt JSON → SO im Bootstrap
3. Prefabs referenzieren SOs, keine Hardcoded-Stats

---

## 10. AAA-Standard-Abgleich

### Was AAA-Standard ist (und du machst)
- ✅ Bottom-up Vertical Slice
- ✅ Server-Authoritative von Tag 1
- ✅ Strikte Trennung Sim / Presentation (MVC + NetworkBehaviour-Layer)
- ✅ Build-Time Role-Stripping (Dedicated Server Subtarget)
- ✅ Fixed Tick Simulation getrennt von Render-FPS
- ✅ Data-Driven (JSON + ScriptableObjects, keine Hardcoded-Balance)

### Was AAA hat (und du erstmal nicht brauchst)
- ⚠️ Custom Engine — du nutzt Unity (good enough für Indie/AA)
- ⚠️ Server-Rewind Lag-Compensation — NGO macht's nicht out-of-the-box, nachrüstbar
- ⚠️ Anti-Cheat (BattlEye, EAC, Vanguard) — später
- ⚠️ Backend-Microservices (Matchmaking, Ranked, Telemetrie) — UGS optional später
- ⚠️ Audio-Middleware (Wwise/FMOD) — Unity Audio reicht für MVP
- ⚠️ Deterministic Lockstep für Esports-Replays — nicht für Survivor-MOBA-Tier nötig

### Realistische Zielsetzung
**Indie/AA-Tier mit AAA-Patterns** — wie Hades, Risk of Rain 2, Deep Rock Galactic, MegaBonk.
Nicht Riot-Tier Esport-Titel.

---

## 11. Anti-Patterns (NICHT machen)

- ❌ Coroutines für Gameplay-Flow (stattdessen State Machines)
- ❌ `Update()` mit Polling-Checks (stattdessen Events)
- ❌ Singleton-Zugriffe über `ApplicationEntryPoint.Singleton` für Services (stattdessen `ServiceLocator.Get<T>()`)
- ❌ Magic Numbers / Strings (stattdessen Konstanten/ScriptableObjects)
- ❌ Monolithische Skill-Klassen (stattdessen Effect-Composition)
- ❌ Client-Side Damage/Hit-Detection (immer Server-Authoritative)
- ❌ LINQ in Gameplay-Hot-Paths
- ❌ Per-Frame Heap-Allocations in Gameplay-Loops (Object Pools!)
- ❌ Reflection in Runtime-Gameplay-Systems
- ❌ Jede Kugel / jedes Partikel als `NetworkObject` synchronisieren — nur Events/Seeds senden
- ❌ NGO-RPCs für hochfrequente Streams (NetworkVariables oder Custom Snapshot-System nutzen)

---

## 12. Offene TODOs (nach ECS-Cut)

- [x] ECS-Code gelöscht (RiftstormBootstrap, MovementAuthoring, Move*Component, MovementSystem)
- [x] ECS-Refs aus 5 Asmdefs entfernt
- [x] `com.unity.feature.ecs` aus manifest.json entfernt
- [ ] `DefaultNetworkPrefabs.asset` Inhalt prüfen (NGO nutzt das tatsächlich)
- [ ] `ConnectionManager` schreiben (NGO `NetworkManager` Wrapper als StateMachine)
- [ ] `ApplicationEntryPoint`-Skelett (ServiceLocator + Dedicated-Server-Switch)
- [ ] `Player.prefab` mit `NetworkObject` + `PlayerNetworked`
- [ ] Server-Authoritative Movement implementieren
- [ ] Erste Vertical Slice: 1 Hero, 1 Map, 1 Boss, 4 Spieler, server-authoritativ
- [ ] `backup_old/` Ordner aufräumen
- [ ] Leere `Game/Ecs/` Ordner-Reste löschen
