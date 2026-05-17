# 00 – Vision & Game Design

> **Stand**: Lebendes Dokument. Quelle der Wahrheit für Pitch-/Scope-Fragen.
> Ergänzt [`01-architektur-kontext.md`](01-architektur-kontext.md) und [`05-roadmap-mmo-port.md`](05-roadmap-mmo-port.md).

---

## 1. Elevator Pitch

**Riftstorm** ist ein **Multiplayer Dark-Fantasy PvPvE Topdown Survivor-MOBA/MMO**.

Auto-Combat-Loop & Build-Evolution aus **Vampire Survivors** / **Megabonk** treffen auf
**LoL**-/**WoW**-Teamfights und Objektive plus **ARPG**-Synergie-Tiefe. ~15 Spieler pro
Match (skalierbar), 15–25 Minuten Sessions, dedizierter Server, vollständig
server-authoritativ.

---

## 2. Genre & Inspirationen

| Quelle | Was wir übernehmen |
|---|---|
| **Vampire Survivors / Megabonk** | Auto-Attack-Kerncloop, Pickups, In-Match-Level-Ups, Build-Evolutionen, Horde-Pacing |
| **League of Legends** | Match-Struktur (15–25 min), Objektive, Map-Pressure, Team-Rollen, Sichtbarkeit/Fog |
| **World of Warcraft** | Visuelles Framing, klare Klassenfantasie, Talent-Optik, Endgame-Loop-Inspiration |
| **ARPG (Path of Exile / Diablo / Last Epoch)** | Synergie-/Modifier-Tiefe, Loot-Tier-System, Build-Diversität, Theorycrafting |
| **FLARE Engine** | Iso-Sprite-Konvention, 8-Richtungs-Asset-Layout |

**Genre-Position**: Niche zwischen Survivor-Bullet-Hell und MOBA. Stand 2025 hat
niemand die MOBA-PvP-Qualität in einem Survivor-Loop sauber gelöst — das ist der
Wedge.

### 2.1 Genre-Fusion-Intent (bewusst gewollt)

Riftstorm ist **explizit ein Hybrid aus allen vier Genres**, kein „Survivor mit
MOBA-Anstrich". Jedes Genre liefert eine tragende Säule:

- **Survivor** = der moment-to-moment Combat-Loop und das Build-Up-Gefühl.
- **MOBA** = Match-Pacing, Objektive, Teamfights, Map-Pressure, klare Sessions.
- **MMO** = soziale Schicht außerhalb des Matches: Hub-Welt, Klassen-Progression,
  Gilden/Parties, Endgame-Pillars, Persistenz (Cosmetics, Unlocks, Mastery).
- **ARPG** = Loot-/Modifier-/Synergie-Tiefe, Theorycrafting, Build-Diversität.

**Ziel-Empfindung**: „Vampire Survivors moment-to-moment, LoL match-to-match,
WoW between-matches, Path of Exile build-to-build."

Die Out-of-Scope-Liste in §9 schließt **nicht** MMO-Elemente aus, sondern grenzt
nur ab, **welche** MMO-Konventionen wir nicht 1:1 übernehmen (kein Seamless-Open-World,
kein Quest-Grinding-Endgame).

---

## 3. Core Game Loop

### 3.1 In-Match-Loop (15–25 min)

```
1. Spawn-Phase (0–2 min)      ──> Pick Klasse, erste Waffen-/Skill-Auswahl
2. Build-Phase (2–10 min)     ──> PvE-Wellen, Level-Ups, erste Evolutionen
3. Skirmish-Phase (10–18 min) ──> PvP-Encounters, Map-Objektive, Powerspike-Fenster
4. Endgame (18–25 min)        ──> Final-Objektiv / Boss-Showdown / Last-Stand
```

Schlüsselprinzipien:

- **Power-Ramp ist schnell.** In Minute 5 fühlt sich der Char bereits stark an — kein 20-min-Farming wie in klassischen MOBAs.
- **PvE und PvP überlagern sich** statt strikt getrennt: Enemies sind sowohl Bedrohung als auch XP-Quelle bei PvP-Engagements.
- **Builds sind im Match temporär.** Persistenz nur auf Meta-Ebene (Klassen-Unlocks, kosmetisch, kein Power-Creep).

### 3.2 Meta-Loop (MMO-Schicht)

```
Hub-Welt ──> Match ──> XP / Loot / Mastery ──> Unlocks ──> Hub-Welt
   │                                                          │
   └── soziale Layer: Gilden, Parties, Trading (TBD) ─────────┘
```

Persistente MMO-Elemente außerhalb des Matches:

- **Hub-Welt** (instanziiert, kein Seamless-Open-World) als sozialer Treffpunkt, Vendor-/Crafting-Zone, Party-Building.
- **Klassen-Mastery** und Talent-Unlocks pro Klasse — langlebige Progression.
- **Loot-Persistenz** für Cosmetics und Build-Bausteine (kein Power-Creep über Match-Builds hinaus).
- **Gilden / Parties** für Pre-Made-Matches und soziale Bindung.
- **Endgame-Pillars** (TBD): Mastery-Tiers, saisonale Modi, Leaderboards.

**Kein Pay-to-Win.** Monetarisierung nur kosmetisch / Battle-Pass-Stil (TBD).

---

## 4. Match-Struktur

- **~15 Spieler** Default (kann je nach Mode 5v5, 3-Team-FFA, oder PvE-Coop sein).
- **15–25 Minuten** Hard-Cap (Anti-Stalling, ähnlich LoL Surrender-Window).
- **Dedicated Server**, **server-authoritativ**.
- **Reconnect-Support** erwünscht (NGO unterstützt das nativ via Persistent-Session-Pattern).

---

## 5. Performance-Targets

| Metrik | Ziel | Begründung |
|---|---|---|
| Spieler / Match | ~15 (skalierbar) | Sweet-Spot zwischen MOBA-Lesbarkeit und MMO-Feel |
| Enemies / Match | 200–400 gleichzeitig | Survivor-Horde, kein Tausender-Bullet-Hell |
| Server-Tick | 20–30 Hz | NGO-Komfortzone, fixed-step Simulation |
| Client-FPS | 60 stabil | Topdown-Action ist responsivitätssensitiv |
| RPC-Budget | minimal | Events statt vollständiger Object-Sync wo möglich |

---

## 6. Netcode- & Authority-Modell

- **Server ist Single Source of Truth** für alle gameplay-relevanten Entscheidungen
  (Damage, Cooldowns, Hit-Validation, Movement-Validation, Loot-Drops).
- **Clients senden nur Inputs / Intentions** (Move-Targets, Cast-Requests).
- **Client-Prediction** für eigene Bewegung; **Reconciliation** bei Server-Korrektur.
- **Enemies werden nicht als individuelle NetworkObjects repliziert.** Snapshot-Sync
  + AOI-Culling (Area-of-Interest), ähnlich wie LoL für Minions.
- **Visuelle Effekte** laufen client-seitig (kein Sync für jeden Partikel).

---

## 7. Art Direction

- **Dark Fantasy**, gothic-leaning, gedeckte Palette mit gezielten Akzentfarben.
- **WoW-inspiriertes Visual Framing**: lesbare Silhouetten, klare Klassenfantasie, leicht stilisiert (nicht hyperrealistisch).
- **Topdown Iso-Sprites**, 8 Richtungen, FLARE-Konvention.
- **Lesbarkeit > Fidelity.** In Horde-Combat müssen Enemy-Silhouetten und Telegraphs sofort erkennbar bleiben.
- **Color-Coding**:
  - Fraktion / Team → harte, konsistente Farben
  - Damage-Type → konsistente Tönungen
  - Danger-Zones → reservierte Warn-Farben (rot/orange)

---

## 8. Inhalts-Pillars

1. **Class Fantasy** — jede Klasse hat eine ausdrucksstarke Grundidentität (z. B. Necromancer, Stormcaller, Bloodknight).
2. **Build Diversity** — pro Klasse mindestens 3–5 sinnvolle Build-Pfade über Waffen-Slots + Talente.
3. **Evolution Surprise** — Survivor-Style verkettete Upgrades, die im Match zu „Aha"-Momenten führen.
4. **Tactical Map** — wenige, prägnante Objektive statt MOBA-Lane-Sprawl. Karte muss in 5 Sekunden lesbar sein.
5. **Endgame Spike** — die letzten Minuten müssen sich anders anfühlen als die Mitte (Boss, finales Objektiv, Sudden-Death).

---

## 9. Out-of-Scope (bewusst NICHT)

Das Spiel **bedient alle vier Genre-Elemente** (Survivor + MOBA + MMO + ARPG).
Diese Liste schränkt nur ein, **welche konkreten Konventionen** wir nicht übernehmen:

- Kein FPS / Gunplay-Fokus (es bleibt Topdown-Action).
- Keine 100+ Spieler / Battle-Royale-Scale pro Match (Ziel ist ~15, skalierbar).
- **MMO-Schicht ja, aber als Hub-/Meta-Layer** — kein Seamless-Open-World wie WoW,
  kein Quest-Grinding als Hauptloop. Persistenz lebt in Hub, Klassen, Mastery, Gilden.
- Kein DOTS/ECS in V1 — klassische MonoBehaviours + Pooling + Spatial-Hashing
  reichen für die Ziel-Scale (Re-Evaluation falls Enemy-Count >500 ansteht).
- **ARPG-Tiefe ja, aber klassengebunden** — keine Cross-Klassen-Builds wie
  Path of Exile (Klassen-Identität bleibt scharf).
- Kein P2W. Keine Lootboxen mit Gameplay-Effekt.
- Kein vollwertiges Trading-/Auction-House in V1 (mögliches Endgame-Feature später).

---

## 10. Risiko-Register (Tech & Design)

| Risiko | Mitigation |
|---|---|
| Visuelle Überladung in Horde + PvP gleichzeitig | Strenge VFX-Disziplin, Telegraph-Pflicht, Color-Coding |
| Server-Tick-Budget bei 200–400 Enemies + 15 Players | Spatial-Hashing, AOI, kein NetworkObject pro Enemy, evtl. Job-System ohne Burst |
| Balance Survivor-Build vs. PvP-Counterplay | Build-Tier-Banding, klare Counter-Beziehungen, In-Match-Rebalancing-Hooks |
| Content-Treadmill für ARPG-Tiefe | Modulare Affix-/Modifier-Pipeline, daten­getrieben via StreamingAssets/JSON |
| Scope-Creep | Diese Datei + 05-Roadmap = Scope-Anker. Alles außerhalb braucht explizite Begründung |

---

## 11. Glossar

- **PvPvE**: Player-vs-Player-vs-Environment — Spieler kämpfen gegen sich und gegen Enemies gleichzeitig.
- **AOI** (Area of Interest): Netzwerk-Optimierung, nur relevante Entities pro Client syncen.
- **Power-Spike**: Build-Phase in der der Char einen Sprung in Effektivität macht (typischerweise nach einer Evolution).
- **Survivor-Loop**: Auto-Attack + Pickup + Level-Up-Pick-Cycle aus Vampire Survivors.
