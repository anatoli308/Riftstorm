# Isometrische Sprites & Richtungen

Referenz zur Sprite-Direktionalität in Riftstorm (Flare-/Diablo-Engine-Erbe).
Beantwortet: "Wie viele Rotations-Frames pro Asset, und warum?"

---

## 1. Perspektive ≠ Richtungen

Zwei unabhängige Konzepte, die oft verwechselt werden:

### Perspektive (Kamera-Blickwinkel)
- **Definiert durch die Kamera-Achse**, nicht durch Sprites
- Isometrisch: ~30° Höhenwinkel, 45° Drehung (klassisch Diablo/Flare/SoF)
- Top-Down: 90° von oben
- Side-Scroller: 0°, horizontal
- **Die Perspektive bleibt gleich, egal wie viele Richtungen ein Sprite hat.**

### Richtungen (Rotations-Frames)
- **Wie viele Winkel-Varianten** eines Sprites gezeichnet sind
- Beeinflusst **Bewegungs-Smoothness** und **Asset-Aufwand**
- Hat nichts mit der Perspektive zu tun

**Faustregel:** Sprite-Stil muss zur Kamera-Perspektive passen. Anzahl Richtungen ist davon entkoppelt.

---

## 2. Richtungs-Stufen

| Richtungen | Winkel-Schritt | Look beim Drehen | Typischer Use-Case |
|---|---|---|---|
| 1 (fixed) | — | Sprite dreht sich nie | Pickups, Effekte, Deko, Decals |
| 2 | 180° | Links/Rechts | Symmetrische Objekte, simple NPCs |
| 4 | 90° | Sichtbar grob bei Diagonal-Bewegung | Alte Pixel-RPGs, statische NPCs |
| **8** | **45°** | **Standard für Iso/Action-RPG** | **Spieler, Gegner, Projektile** |
| 16 | 22.5° | Sehr smooth | Cinematic Bosse, Fahrzeuge |
| 32+ | <12° | Fast 3D-like | Vorzeige-Units, Premium-Assets |

---

## 3. Richtungen nach Asset-Typ

### Bewegliche Einheiten (immer 8+)

| Asset | Richtungen | Begründung |
|---|---|---|
| Spielerfigur | 8 | Pflicht, sonst ruckelt Diagonal-Laufen |
| Gegner / NPCs | 8 | Standard für Action-RPG |
| Wichtige Bosse | 8 oder 16 | Smoothness lohnt sich bei Hero-Units |
| Projektile (Pfeile, Speere) | 8 oder 16 | Viele Winkel = glaubwürdige Flugbahn |
| Wurfgranaten (rotieren in Flugkurve) | 1 oder 8 + Frame-Loop | Eigenrotation überlagert |

### Statische Objekte mit Variation (8 möglich, "Flare-Style")

Diese sind **nicht animiert rotierend** — der Map-Editor wählt pro Instanz
einen festen Winkel, damit dasselbe Asset visuell variiert auf der Map liegt.

| Asset | Richtungen | Begründung |
|---|---|---|
| Hero-Bäume (markant, groß) | 8 | Variation ohne neue Assets, bessere Silhouetten |
| Große Felsen | 8 | Wie Bäume — wirkt nicht "kopiert" |
| Hütten, Tore, Brücken | 4–8 | Verschiedene Ausrichtungen auf der Map |
| Wagen, Karren | 8 | Können in jede Richtung stehen |

### Statische Objekte ohne Variation (1–2)

| Asset | Richtungen | Begründung |
|---|---|---|
| Gras-Büschel, kleine Steine | 1 | Zu klein für Variation, spart Speicher |
| Säulen, Brunnen (rund) | 1 | Sehen aus jedem Winkel gleich aus |
| Türen | 2 | Geöffnet / geschlossen ist wichtiger als Winkel |
| Fackeln, Lampen | 1 (+ Frame-Loop für Flamme) | Symmetrisch, Animation > Rotation |
| Loot, Pickups | 1 (+ Bob-Animation) | Rotation egal, Lesbarkeit wichtiger |
| Decals, Blutspuren | 1 | Liegen flach auf dem Boden |

---

## 4. Flare-Engine-Konvention

In Flare hat **jedes** Asset einen `direction`-Slot — auch statische Tiles.
Der gleiche Animation-Slot wie bei Charakteren, nur **ohne Frame-Loop**.

```
ENTITY type=tile
    sprite=tree_oak_large
    direction=0..7      ← Map-Editor wählt aus 8 Varianten
    animation=stand     ← nur 1 Frame, kein Loop
```

**Vorteile:**
- Map-Editor kann beim Platzieren random rotieren
- Ein Sprite-Sheet → 8 visuell unterschiedliche Bäume auf der Map
- Spieler erkennt Tiefe besser, weil Wald nicht "Copy-Paste" wirkt

**Riftstorm folgt dieser Konvention** — daher haben auch Bäume/Felsen
Spritesheets mit 8 Richtungen.

---

## 5. Konsistenz-Regeln

Sprites mit **unterschiedlicher Richtungs-Anzahl dürfen gemischt werden**,
solange sie **dieselbe Perspektive** haben:

✅ **Erlaubt:** 8-Richtungs-Charakter + 1-Richtungs-Pickup auf derselben Map
✅ **Erlaubt:** 8-Richtungs-Baum + 1-Richtungs-Gras
❌ **Nicht erlaubt:** Iso-Charakter (30° Höhenwinkel) + Top-Down-Möbel (90°)
❌ **Nicht erlaubt:** Lichtquelle aus Nordwest beim Charakter, aus Süden beim Baum

### Konsistenz-Checkliste pro Asset

- [ ] **Höhenwinkel der Kamera** identisch (z. B. alle ~30°)
- [ ] **Drehwinkel der Kamera** identisch (z. B. alle 45° aus Nordost)
- [ ] **Lichtquelle** aus derselben Richtung (typisch: oben-links)
- [ ] **Pixel-Density / Auflösung** vergleichbar (kein 32px-Charakter neben 256px-Baum)
- [ ] **Render-Stil** konsistent (alle Pixel-Art, alle gerendertes 3D, oder alle gemalt)

---

## 6. Speicher-Trade-Off

| Variante | Frames pro Asset | Atlas-Größe (relativ) |
|---|---|---|
| 1 Richtung, 1 Frame (Deko) | 1 | 1× |
| 8 Richtungen, 1 Frame (Baum) | 8 | 8× |
| 8 Richtungen, 8 Frames (Walk-Anim) | 64 | 64× |
| 16 Richtungen, 12 Frames (Boss-Attack) | 192 | 192× |

**Konsequenz:** Charaktere dominieren das Sprite-Budget. Deshalb sind
statische Assets oft auf 1–8 Richtungen ohne Anim beschränkt.

---

## 7. Entscheidungs-Workflow für neue Assets

```
Bewegt sich das Asset?
├─ JA → braucht es Animation?
│  ├─ JA → 8 Richtungen × N Frames (Standard für Chars)
│  └─ NEIN → 8 Richtungen × 1 Frame (Projektil-Standstill)
│
└─ NEIN → soll es auf der Map variieren?
   ├─ JA, ist es "groß/markant"? → 8 Richtungen × 1 Frame (Flare-Style)
   ├─ JA, mittelgroß? → 4 Richtungen × 1 Frame
   └─ NEIN, klein oder symmetrisch? → 1 Richtung × 1 Frame
```

---

## 8. Quellen / Inspiration

- **Flare Engine** (`https://github.com/flareteam/flare-engine`) — Open-Source-Iso-Engine,
  Asset-Konvention 1:1 übernommen
- **Diablo II** — 8 Richtungen für Chars, 1–4 für Objekte

---

## 9. Relevant für Riftstorm

- Spieler-Sprites erwarten 8 Richtungen (oder 1/4 je nach Kategorie aus Abschnitt 7)
- `PrefabManager` lädt Sprite-Atlas-Prefabs via Addressables —
  Asset-Layout pro Atlas folgt Flare-Konvention
- `Assets/Art/` strukturiert nach Asset-Typ; pro Asset eigenes Spritesheet mit
  Richtungs-Variante als horizontaler Streifen, Frames vertikal

> Wenn du beim Importieren eines neuen Assets unsicher bist, verwende den
> Entscheidungs-Workflow aus Abschnitt 7.
