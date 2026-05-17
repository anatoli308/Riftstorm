# MUGEN → Unity Import Pipeline

Referenz für den MUGEN-Character-Importer in Riftstorm. Beantwortet:
"Wie kommen MUGEN-Charaktere mit voller Combat-Daten-Treue ins Unity-Projekt?"

Verwandte Dokumente:
- `04-animationen-combat.md` — wie Animationen/Combat im Game-Code laufen
- `07-ui-fonts-streamingassets.md` — Loader-Pattern (HudConfigLoader) als Vorbild
- `08-isometric-sprites-richtungen.md` — warum 8 Richtungen + warum hier "fake"

---

## 1. Ziel & Scope

**Was die Pipeline löst:** MUGEN-Charaktere (binäre/textuelle Eigenformate aus
der Fighting-Game-Engine) in ein Unity-freundliches JSON+PNG+WAV-Bundle
konvertieren, **ohne Datenverlust**.

**Datentreue: 100% auf Extraktions-Seite.** Alles, was MUGEN selbst hat
(Sprites, Animationen, Hitboxen, Hurtboxen, State Machine, Commands, Sounds,
Palettes, Konstanten), ist in der Ausgabe verfügbar.

**Nicht im Scope:**
- Echte 8-Richtungs-Sprites (MUGEN ist 2D-Sidescroller → siehe §6)
- C#-Runtime im Unity-Projekt (siehe §8 "Next Steps")
- MUGEN-Trigger-Sprache evaluieren (eigene Mini-Sprache, ~100 Controller-Typen)

---

## 2. Tool-Standort

```
Tools/Scripts/mugen_import/
├── mugen_import.py        # CLI Entry-Point
├── requirements.txt       # Pillow >= 10
├── mugen/                 # Parser-Module (klein, SRP)
│   ├── __init__.py
│   ├── def_parser.py      # .def → CharDef (Metadaten + File-Refs)
│   ├── sff_v1.py          # .sff v1 (PCX 8-bit indexed RLE) → Sprites
│   ├── air.py             # .air → Actions + Frames + Clsn-Boxen
│   ├── cns_parser.py      # .cns Konstanten ([Data]/[Size]/[Velocity]/…)
│   ├── cns_states.py      # Statedef/State Controller (shared CNS/ST/Common)
│   ├── cmd_parser.py      # .cmd Commands + State -1 Listener
│   ├── snd_parser.py      # .snd → WAV-Subfiles + Index
│   ├── palette.py         # .act (768 B Adobe Color Table)
│   ├── atlas.py           # Sprite-Packing (Shelf-Algorithmus)
│   └── eight_dir.py       # 8-Richtungs-Layout (Mirror-basiert)
└── mugen_data/            # (gitignored) Original-Charaktere
    ├── Mudpenis/          # alter MUGEN, shift_jis, multi-file CNS
    └── Gallon/            # MUGEN 1.1, utf-8-sig, LocalCoord 320×240
```

---

## 3. Verwendung

```bash
cd Tools/Scripts/mugen_import
python -m pip install -r requirements.txt

# Voll-Import (alle Daten):
python mugen_import.py mugen_data/Mudpenis --out out/Mudpenis

# Nur Sprites + Animationen (schnell, kein Combat-Data):
python mugen_import.py mugen_data/Gallon --out out/Gallon \
    --no-palettes --no-states --no-sounds

# Atlas-Only (für Vorschau):
python mugen_import.py mugen_data/Mudpenis --out out/Mudpenis \
    --no-individual-pngs --no-states --no-sounds
```

### CLI-Flags

| Flag | Wirkung |
|---|---|
| `--out <dir>` | Output-Verzeichnis (Default: `./out/<char>`) |
| `--max-width <px>` | Atlas-Breite (Default 4096) |
| `--padding <px>` | Atlas-Padding (Default 2) |
| `--transparent-index <i>` | Palette-Index = transparent (MUGEN: 0) |
| `--no-individual-pngs` | Skip per-Sprite PNGs (nur Atlas) |
| `--no-palettes` | Skip Pal1..Pal12 |
| `--no-states` | Skip CNS/CMD State Machine |
| `--no-sounds` | Skip .snd |
| `--def-file <name>` | Explizite .def (sonst Auto-Detect) |

---

## 4. MUGEN-Format-Reconnaissance

Format-Wissen aus echter Datenanalyse (Mudpenis = altes MUGEN, Gallon = 1.1).

### .def — Character-Manifest (INI-like)
- `[Info]` → name, displayname, versiondate, mugenversion, author, localcoord
- `[Files]` → sprite, anim, cmd, snd, cns, stcommon, st, st1..st9, pal1..pal12
- **Encoding-Fallback:** `utf-8-sig → shift_jis → cp1252 → latin-1`
  (alle Parser nutzen `air._read_text`)

### .sff v1 — Sprite Pack
- Binär-Header mit Group/Image-Indizes
- Frames sind **PCX 8-bit indexed RLE**
- **Shared Palettes** (Linked-Index-System spart Speicher)
- Pivot-Punkt (axisX/axisY) pro Sprite

### .air — Animations
- Text-Format mit `Begin Action N`-Blöcken
- Frame-Zeilen: `group, image, axisX, axisY, time, flip, blend`
- **Clsn1 (Hurtboxes) + Clsn2 (Hitboxes):**
  - `Clsn1Default: N` → gilt für **alle folgenden Frames** der Action
  - `Clsn1: N` → gilt nur für den **nächsten Frame**
  - Box-Zeilen: `Clsn1[i] = x1, y1, x2, y2`
  - Voller State Machine in `air.py` implementiert

### .cns — Constants + States
- INI-like
- **Konstanten-Sektionen:** `[Data]`, `[Size]`, `[Velocity]`, `[Movement]`, `[Quotes]`
- **State Machine:** `[Statedef N]` Header + `[State N, label]`-Controller
- Controller-Keys: `type`, `trigger1`/`trigger2`/`triggerall`, beliebige Params
- **Multi-File-Setup** (Mudpenis): `St=`, `St1=`..`St9=`, `StCommon=common1.cns`
- **MUGEN 1.1** (Gallon): einzelne `.st`-Dateien (NormalMoves.st, Special.st, …)

### .cmd — Commands + State -1
- `[Defaults]` → command.time, command.buffer.time
- `[Remap]` → Input-Remapping
- `[Command]`-Blöcke → name, command (`~D, DF, F, x`), time, buffer.time
- `[Statedef -1]` + `[State -1, label]` → Listener (was bei welchem Command passiert)

### .snd — Sound Pack
- **24-Byte Header:** `b"ElecbyteSnd\x00"` + version + numsounds + first_offset
- Optional Free-Text-Comment zwischen Offset 24 und first_offset
- Chain of **16-Byte Subfile-Headers:** `<iiii` = next_offset, length, group, sample
- Payload: i.d.R. RIFF WAV (manchmal MP3/anderes → wird übersprungen)
- Cycle-Defense via `visited: set[int]`

### .act — Palette
- **Genau 768 Bytes** = 256 × RGB (Adobe Color Table)
- Index 0 = transparent
- Pfad-Suche: `def_dir`, `palette/`, `palettes/`-Subordner

---

## 5. Output-Schema

Pro Character produziert die Pipeline:

```
out/<character>/
├── char.json              # Manifest mit "counts"
├── atlas.png              # Packed Sprite Atlas
├── atlas.sprites.json     # Per-Sprite Rects + Pivots
├── animations.json        # 8-dir Actions (mit Clsn-Boxen pro Frame)
├── constants.json         # CNS [Data]/[Size]/[Velocity]/[Movement]/[Quotes]
├── states.json            # Merged State Machine (alle .cns + .st + common)
├── commands.json          # Commands + State -1 Listener
├── palettes.json          # Pal1..Pal12 Color Tables
├── palettes/lut.png       # 256×N LUT-Texture (eine Zeile pro Palette)
├── sounds.json            # Sound-Index (group, sample, file)
├── sounds/<g>_<s>.wav     # RIFF-Payloads
└── sprites/<g>_<i>.png    # (optional) per-Sprite PNGs
```

### Beispiel — `char.json` Counts

| Char | sprites | actions | frames | palettes | commands | sounds |
|---|---|---|---|---|---|---|
| Mudpenis | 432 | 229 | 3605 | 10 | 71 | 61 |
| Gallon | 1571 | 813 | 8705 | 12 | 166 | 151 |

### `animations.json` — Schema (gekürzt)

```json
{
  "actions": [{
    "id": 200,
    "frames": [{
      "group": 200, "image": 0,
      "axisX": 50, "axisY": 100,
      "time": 4, "flip": "", "blend": "",
      "clsn1": [{"x1": -10, "y1": -80, "x2": 10, "y2": 0}],
      "clsn2": [{"x1": -20, "y1": -90, "x2": 20, "y2": 10}]
    }],
    "directions": {
      "E":  [...], "W":  [...],
      "NE": [{"...": "...", "fake": true}],
      "NW": [{"...": "...", "fake": true}],
      "SE": [...], "SW": [...],
      "N":  [...], "S":  [...]
    }
  }]
}
```

---

## 6. 8-Richtungen — Realitäts-Check

**MUGEN-Sprites existieren nur in Seitenansicht (E).** Die Pipeline füllt das
8-dir-Layout per Mirror-Fallback:

| Richtung | Quelle | Flag |
|---|---|---|
| E | Original | — |
| W | E + horizontal Flip | — |
| NE / SE | E kopiert | `fake:true` |
| NW / SW | W kopiert | `fake:true` |
| N / S | E kopiert | `fake:true` |

→ Für einen **Topdown-Survivor wie Riftstorm** ist das ein **Platzhalter**.
Für echte N/S/Diagonal-Ansichten später entweder:
1. Mit `fake:true`-Frames leben (viele Survivors machen das so — Stilfrage)
2. Sprites manuell oder per AI-Reprojection nachziehen
3. Diagonale Frames aus Helper-Animationen klauen, wo MUGEN-Char zur Kamera schaut

Siehe `08-isometric-sprites-richtungen.md` für das größere Richtungs-Konzept.

---

## 7. Validierte Ergebnisse

Pipeline ist gegen zwei Charaktere mit unterschiedlichen MUGEN-Versionen
verifiziert:

| Aspekt | Mudpenis | Gallon |
|---|---|---|
| MUGEN-Version | alt (DOS-Ära) | 1.1 |
| Encoding | shift_jis | utf-8-sig |
| LocalCoord | — | 320×240 |
| Palette-Pfad | `palette/`-Subordner | Root |
| State-Files | `St`, `St1..St9`, `StCommon` | `.st` einzeln |
| Exit-Code | 0 | 0 |
| Atlas-Größe | 534 KB | 2.4 MB |
| states.json | 680 KB | 1.3 MB |

---

## 8. Next Steps — Unity-Integration

Pipeline ist **fertig auf Extraktions-Seite**. Was im Unity-Projekt noch fehlt:

### Stufe 1 — Charakter sichtbar machen (1–2 Tage)
1. **StreamingAssets-Struktur:**
   `Assets/StreamingAssets/characters/<name>/` als Ziel von `--out`
2. **Loader nach `HudConfigLoader`-Pattern** (siehe `07-…`):
   - `CharacterManifestLoader` → `char.json`
   - `AnimationLoader` → `animations.json`
   - `SpriteAtlasLoader` → `atlas.png` + `atlas.sprites.json`
   - Lazy-Static-Cache + JsonConvert.DeserializeObject + Fallback auf Defaults
3. **Runtime-Komponenten:**
   - `SpriteAtlasRuntime` → Atlas + sprite-rects → `Sprite[]`-Liste
   - `AnimationPlayer` → Frame-Sequencing nach `time`-Feld, Pivot anwenden
   - `HitboxOverlay` (Debug) → Clsn1/Clsn2 als Gizmos pro aktivem Frame

### Stufe 2 — Combat-Daten lesen (nice-to-have)
- `CombatStatsLoader` → `constants.json` → max HP, attack, defence, velocities
- `SoundBank` → `sounds.json` + WAV-Loading via `UnityWebRequest`
- **Hitbox-System:** Clsn2 → OverlapBox bei aktivem Frame → Damage-Event

### Stufe 3 — Full MUGEN-Combat (mehrere Wochen, NICHT empfohlen)
MUGEN hat ~100 State-Controller-Typen (`ChangeState`, `HitDef`, `VelSet`,
`PlaySnd`, `Helper`, `Projectile`, …) und eine eigene Trigger-Mini-Sprache
(`Vel X > 0 && StateNo != 100 && Time > 5`).

**Empfehlung:** State Machine + Commands ignorieren. Riftstorm hat eigenes
Combat-System (siehe `04-animationen-combat.md`). MUGEN liefert nur:
- Sprites + Animationen (visuell)
- Hitboxen pro Frame (Damage-Timing)
- Sounds (Audio)
- Constants (als Inspiration für Stats, nicht als Quelle of Truth)

---

## 9. Regel-Konformität (Riftstorm Coding Rules)

- ✅ **JSON over ScriptableObject** — Output ist reines JSON für StreamingAssets
- ✅ **No Resources/** — kein einziger `Resources.Load`
- ✅ **Many small files** — alle Module < 300 Zeilen, eine Verantwortung
- ✅ **KISS/DRY/YAGNI** — keine spekulativen Abstraktionen, keine Vererbung
- ✅ **Explicit over implicit** — `dict[int, str]`, dataclasses statt dicts
- ✅ **Loader-Pattern-ready** — Output passt zu `HudConfigLoader`-Stil

---

## 10. Quick-Reference — Was wo nachschauen

| Frage | Datei |
|---|---|
| Wie funktioniert die SFF-Decodierung? | `mugen/sff_v1.py` |
| Wie werden Clsn-Boxen Frames zugeordnet? | `mugen/air.py` (Default vs. specific) |
| Wie merged man State Machines über Files? | `mugen/cns_states.py:merge_states()` |
| Welche Konstanten sind im char wichtig? | `out/<char>/constants.json` |
| Wo sind die Hitboxen? | `out/<char>/animations.json` → `frames[i].clsn2` |
| Wie sieht ein Command-Eintrag aus? | `out/<char>/commands.json` |
| Wo sind die WAVs? | `out/<char>/sounds/<group>_<sample>.wav` |

---

## 11. FLARE-Bridge — `mugen_to_flare.py`

> **Status (Mai 2026): pausiert.** MUGEN-Import war ein explorativer Ansatz.
> Wir bleiben zunächst bei nativen FLARE-Charakteren. Dieser Abschnitt
> hält den Stand fest, damit der Faden später ohne Re-Entry-Cost wieder
> aufgenommen werden kann.

### 11.1 Zweck

Konvertiert die Importer-Ausgabe (`animations.json` + `atlas.sprites.json`
+ optional `constants.json`/`char.json`/`states.json`) in das
FLARE-Runtime-Format:

```
<char>/<char>.json         # FLARE-Atlas: animations + per-cell flipH
<char>/<char>.stats.json   # Sidecar mit Combat-Stats + Provenance
```

Konsumiert direkt von `Assets/Scripts/Runtime/Game/Sprites/FlareAtlasLoader.cs`
→ `FlareCharacter.SetDirection(int)` (Slot = `direction & 7`).

### 11.2 Datei

`Tools/Scripts/mugen_import/mugen_to_flare.py` (eigenständige CLI,
keine Abhängigkeit zum Importer-Modul außer dessen JSON-Output).

### 11.3 Direction-Modes

| `--directions` | Verhalten | Wann nutzen |
|---|---|---|
| `2` (Default) | **2D-Side-View.** Nur E-Frames als Quelle; W/SW/NW (Slots 0/1/7) bekommen `flipH=true`, N/S/NE/SE bekommen die unflipped E-Frames. | Standard für alle MUGEN-Quellen (sind native 2D-Sidescroller). |
| `8` (Legacy) | Pro Slot der MUGEN-Action-Nummer für diese Richtung lookup, Fallback auf E. | Nur falls eine MUGEN-Quelle tatsächlich N/S/Diagonal-Frames besitzt (extrem selten). |

### 11.4 Warum 2-dir Default ist

Der Legacy-8-dir-Modus produzierte **Dreh-/Rotations-Artefakte** beim
Richtungswechsel: 5 unflipped Slots (N/S/E/NE/SE) + 3 flipped (W/SW/NW)
ergaben inkonsistente Spiegelung, weil die MUGEN-Fake-Frames die gleichen
E-Sprites waren — aber mal mit, mal ohne Flip.

**Lehre:** Wenn die Quelle fundamental 2D ist, ist ehrliches L/R-Mirror
besser als faken-mit-Lücken. FLARE-Runtime ändert sich dadurch nicht,
weil der Per-Cell `flipH` schon der kanonische Mechanismus ist.

### 11.5 Aufruf

```powershell
& 'C:\Program Files\LibreOffice\program\python.exe' `
  'd:\Riftstorm\Tools\Scripts\mugen_import\mugen_to_flare.py' `
  'd:\Riftstorm\Assets\StreamingAssets\Custom_Characters\Mudpenis' `
  'd:\Riftstorm\Assets\StreamingAssets\Custom_Characters\Gallon'
```

(Default `--directions 2` greift automatisch.)

### 11.6 Aktueller Stand der konvertierten Charaktere

Stand: Mai 2026, beide neu mit `directions=2` geschrieben.

| Character | Actions | Anim-Keys | Aliases | Skipped | Out-of-range | Dup-suffixed | Atlas-JSON |
|---|---|---|---|---|---|---|---|
| Mudpenis | 111 | 153 | 42 | 12 | 106 | 1 | 3.15 MB |
| Gallon (Jon Talbain) | 460 | 512 | 52 | 53 | 300 | 6 | 5.32 MB |

Stats-Sidecars unverändert (hp/mp/str/arm/scale/velocities/skills).
Beide haben `"directions": 2` als Provenance-Marker.

### 11.7 Konverter-Härtungen (über Sessions hinweg)

- **Duplikat-Suffix** (`_b`, `_c`, …) für mehrfach belegte FLARE-Slots
  statt stillem Überschreiben.
- **Max-9999-Filter** für MUGEN-Actions außerhalb des FLARE-Index-Raums
  (zählt als `out-of-range`).
- **Frames-empty-guard** in `_build_animation` (Action ohne nutzbare
  Frames → übersprungen, kein Crash).
- **Stats-Sidecar** trägt Provenance (`directions`, ggf. Source-Charname).

### 11.8 Showcase-Nutzbarkeit

| Use-Case | Verdict |
|---|---|
| 2D-Arena / Bossfight | ✅ Sauber |
| Billboard-Boss in 3D-Welt | ✅ Sauber |
| Standard-Topdown-Mob (nicht-vertikale Kamera) | ⚠️ Visuell „okay", bleibt Side-View |
| Echter Topdown von oben | ❌ Bleibt Platzhalter (siehe §6) |

### 11.9 Wenn-wir-zurückkommen — offene Punkte

- `NpcController.BindMugenStats(MugenCharacterStats)` ggf. entfernen
  (war an alte Stats-Form gebunden, jetzt obsolet durch Sidecar).
- `MugenAnimationShowcase` source-agnostic machen (FLARE-only Reader,
  nicht mehr MUGEN-spezifisch).
- Echte N/S-Diagonal-Frames für ausgewählte Chars manuell ergänzen, falls
  ein MUGEN-Char als richtiger Topdown-Mob eingesetzt werden soll.

