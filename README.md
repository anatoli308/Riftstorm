# Riftstorm

> **Multiplayer Dark-Fantasy PvPvE Topdown-Action**, in dem die Build-Eskalation von *Vampire Survivors* / *Megabonk*, die Teamfights & Pacing von *League of Legends* und die Synergie-Tiefe eines ARPGs (à la *Path of Exile* / *Diablo*) zu einem Survivor-MOBA/MMO-Hybriden im WoW-Look verschmelzen.

**Kernidee in einem Satz:** *Das PvE erzeugt das PvP.*

---

## Vision

Riftstorm ist kein weiterer MOBA-Klon und kein weiterer Survivor-Klon. Spieler farmen Horden, evolvieren Builds, kontrollieren Objectives — und treffen im Endgame mit voll eskalierten Synergie-Builds in chaotischen Teamfights aufeinander. Das Setting ist **Dark Fantasy** (Untote, Dämonen, Ritualmagie), die Welt fokussiert, die Sessions kurz und streamerfreundlich.

- **Earlygame** → Farmen, Skill-Aufbau, kleine Objectives (Vampire-Survivors / Megabonk-Auto-Combat)
- **Midgame** → Map Control, Bosse, erste PvP-Skirmishes (LoL-Pacing)
- **Endgame** → Map schrumpft, Horden eskalieren, voll evolvierte Builds clashen (ARPG-Synergie-Tiefe)

Match-Länge: **15–25 Minuten**. Zugänglich, streamerfreundlich, build-tief.

---

## Pillars

1. **PvE erzeugt PvP** — Horden sind Ressource, Druckmittel und Zonen-Kontrolle gleichzeitig.
2. **Build-Evolution als Dopamin** — Synergien aus Survivor-Auto-Weapons + aktiven MOBA-Skills.
3. **Readability First** — Klare Silhouetten, lesbare FX, identifizierbare Build-Identitäten trotz Chaos.
4. **Server-authoritative Performance** — 10-15 Spieler, hunderte Gegner, stabile 60 FPS.
5. **Teamrollen mit Identität** — Tank, DPS, Support, Controller, Summoner, Assassin.

---

## Core Gameplay Loop

```
Spawn → Farm Horden → Skill leveln → Evolution freischalten
      → Objective contesten → Boss + PvP Skirmish
      → Map Shrink → Voll eskalierter Endgame-Teamfight → Loot / Meta Progression
```

---

## Tech Stack

| Bereich | Wahl | Warum |
|---|---|---|
| Engine | **Unity 6 + URP** | Topdown-tauglich, große Toolchain, performant |
| Gameplay | **Klassische MonoBehaviours** + Object Pooling | Schnelle Iteration, ausreichend für ~15 Spieler / hunderte Enemies |
| Networking | **Netcode for GameObjects (NGO)** + Dedicated Server | Server-authoritative, Unity-nativ, Server-/Client-Build-Split |
| Input | **Unity Input System** | bereits eingebunden |
| Assets | **Addressables** | Cache-first Loading via `PrefabManager` |
| Architektur | **MVC + State Machines + EventManager + ServiceLocator** | Modulare, datengetriebene In-House-Architektur |

> Aktuell installiert: `com.unity.netcode.gameobjects 2.11.2`, `com.unity.dedicated-server 2.0.2`, `com.unity.addressables`, `com.unity.inputsystem`, URP 17.3.0. ECS/DOTS wurde bewusst entfernt.

---

**Patterns:**

- **Single Source of Truth** — Manager halten State, Events triggern nur, Views lesen vom Manager.
- **Service Decomposition** — Manager orchestriert, interne Loader laden Daten, Applier wendet an.
- **Cache-First** — alles über `PrefabManager` / `TextureManager` via `ServiceLocator.Get<T>()`.
- **State Machines statt Polling** — keine `Update`-Flag-Checks, keine Coroutines für Ablaufsteuerung.

---

## Performance-Ziele (MVP)

| Metrik | Ziel |
|---|---|
| Spieler pro Match | ~15 (Default-Target, skalierbar nach oben/unten) |
| Gleichzeitige Enemies | 200–400 |
| Frame Rate | >60 FPS stabil |
| Server Tickrate | 20–30 Hz |
| Bandwidth pro Client | < 64 kbit/s |

---

## Bigger Risks

1. **Visual Noise** — Hauptproblem aller Survivor-Multiplayer-Hybriden.
2. **PvP-Balance bei eskalierten Builds** — Synergien dürfen sich nicht One-Shotten.
3. **Server-Sim-Kosten** bei 10-15 Spielern × hunderten Entities.
4. **Onboarding-Komplexität** — MOBA-Tiefe ohne MOBA-Frust kommunizieren.

---

## Scene-Flow (Boot → Metagame → Game)

```
Boot.unity                    Metagame.unity                    Game.unity
─────────────                 ───────────────                   ──────────
NetworkManager        ─┐      MetagameApplication               GameApplication
 + UnityTransport      │       + MetagameModel                    + GameModel
ApplicationEntryPoint  │       + MetagameView                     + GameView
 + ConnectionManager   │       + MetagameController               + GameController
DontDestroyOnLoad ─────┘             │                                 ▲
                                     ▼                                 │
                          ConnectionManager.StartClient(ip,port)       │
                                     │                                 │
                                     ▼                                 │
                            NGO StartClient → Approval ───────────────►│
                                                  (NGO SceneManager
                                                   syncs Client into Game)
```

- **Server-Build** (`UNITY_SERVER`) → `ApplicationEntryPoint` ruft `ConnectionManager.StartServer(0.0.0.0, --port)`, lädt nach `OnServerStarted` via `NetworkManager.Singleton.SceneManager.LoadScene("Game")`.
- **Client-Build** → lädt `Metagame`, User triggert Connect, `ConnectionManager.StartClient(...)`, NGO synced Client automatisch in `Game`.
- **Disconnect / Server-Down** → Client kehrt automatisch zurück nach `Metagame`.

Einrichtung im Editor: siehe [`referenzen/02-scene-setup.md`](referenzen/02-scene-setup.md).

---

## Repo-Konventionen

Siehe [`.github/copilot-instructions.md`](.github/copilot-instructions.md) für vollständige Coding-, Architektur- und Networking-Standards.

**Kurz:**
- Immer `new()` Syntax statt `new TypeName()`
- XML-Doku auf öffentliche APIs
- Kein Polling, keine Coroutines für Ablaufsteuerung → State Machines / Events
- Kein `Time.deltaTime` in autoritativer Gameplay-Sim → Fixed Tick
- Server ist immer authoritativ, Clients schicken nur Inputs

---

## License

TBD
