# Riftstorm

> **PvPvE Topdown-Action**, in dem die Build-Eskalation von *Vampire Survivors*, die Teamfights von *League of Legends* und die Synergie-Tiefe von *Path of Exile* zu einem chaotischen Survivor-MOBA-Hybrid verschmelzen.

**Kernidee in einem Satz:** *Das PvE erzeugt das PvP.*

---

## Vision

Riftstorm ist kein weiterer MOBA-Klon und kein weiterer Survivor-Klon. Spieler farmen Horden, evolvieren Builds, kontrollieren Objectives — und treffen im Endgame mit voll eskalierten Synergie-Builds in massiven Teamfights aufeinander.

- **Earlygame** → Farmen, Skill-Aufbau, kleine Objectives (wie Vampire Survivors)
- **Midgame** → Map Control, Bosse, erste PvP-Skirmishes (wie LoL)
- **Endgame** → Map schrumpft, Horden eskalieren, 5v5 Chaos mit komplett evolvierten Builds

Match-Länge: **15–25 Minuten**. Zugänglich, streamerfreundlich, build-tief.

---

## Pillars

1. **PvE erzeugt PvP** — Horden sind Ressource, Druckmittel und Zonen-Kontrolle gleichzeitig.
2. **Build-Evolution als Dopamin** — Synergien aus Survivor-Auto-Weapons + aktiven MOBA-Skills.
3. **Readability First** — Klare Silhouetten, lesbare FX, identifizierbare Build-Identitäten trotz Chaos.
4. **Server-authoritative Performance** — 10 Spieler, hunderte Gegner, stabile 60 FPS.
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
| Gameplay-Sim | **ECS / DOTS + Burst + Jobs** | Tausende Entities, cachefreundlich |
| Networking | **Netcode for GameObjects** (Dedicated Server) | Server-authoritative, Unity-nativ |
| Input | **Unity Input System** | bereits eingebunden |
| Assets | **Addressables** | Cache-first Loading via `PrefabManager` |
| Architektur | **MVC + State Machines + EventManager + ServiceLocator** | aus *RemakeSoF* portiert |

> ⚠️ DOTS, NGO und Addressables stehen in der Architektur-Doku, sind aktuell aber **noch nicht** in `Packages/manifest.json`. Das ist der erste konkrete Setup-Schritt.

---

## Architektur (aus RemakeSoF portiert)

```
Assets/Scripts/Runtime/
├── ApplicationLifecycle/   # ApplicationEntryPoint + ServiceLocator
├── Core/                   # MVC Base (Element, Model, View, Controller, BaseApplication)
│                           # State Machine Base (StateMachine<T>, State<T>)
│                           # EventManager (typsichere Events)
├── Management/             # MonoBehaviour Manager (Connection, Auth, Console, ...)
├── AI/                     # Enemy AI, Boss-Logik
├── Game/                   # GameApplication (Match-Scene, Netcode-Integration)
├── Metagame/               # MetagameApplication (Lobby, Hero-Auswahl, Loadout)
└── Shared/                 # Cross-Scene Daten / DTOs
```

**Patterns:**

- **Single Source of Truth** — Manager halten State, Events triggern nur, Views lesen vom Manager.
- **Service Decomposition** — Manager orchestriert, interne Loader laden Daten, Applier wendet an.
- **Cache-First** — alles über `PrefabManager` / `TextureManager` via `ServiceLocator.Get<T>()`.
- **State Machines statt Polling** — keine `Update`-Flag-Checks, keine Coroutines für Ablaufsteuerung.

---

## Performance-Ziele (MVP)

| Metrik | Ziel |
|---|---|
| Spieler pro Match | 10 |
| Gleichzeitige Enemies | 300–500 |
| Frame Rate | >60 FPS stabil |
| Server Tickrate | 20–30 Hz |
| Bandwidth pro Client | < 64 kbit/s |

---

## Bigger Risks

1. **Visual Noise** — Hauptproblem aller Survivor-Multiplayer-Hybriden.
2. **PvP-Balance bei eskalierten Builds** — Synergien dürfen sich nicht One-Shotten.
3. **Server-Sim-Kosten** bei 10 Spielern × hunderten Entities.
4. **Onboarding-Komplexität** — MOBA-Tiefe ohne MOBA-Frust kommunizieren.

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
