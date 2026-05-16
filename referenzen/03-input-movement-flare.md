# 03 – Input, Movement, FLARE-Sprites & Asset-Pipeline

Kompakter Kontext der letzten Arbeit. Querverweise auf die jeweiligen Quellen statt Code-Dumps.

---

## 1. Netcode-Boot-Flow (NGO)

`Boot` → `Metagame` → `Game`

- **Boot**: `ApplicationEntryPoint` initialisiert `ServiceLocator`, registriert Pure Services (`TextureManager`, `PrefabManager`). `[RuntimeInitializeOnLoadMethod]` triggert je nach `MultiplayerRoleFlags`:
  - **Server**: liest CLI (`--port`, `--target-framerate`), startet `NetworkManager`, lädt **Game**-Szene additiv.
  - **Client**: lädt **Metagame**-Szene, optional `AutoConnectOnStartup` → `NetworkManager.StartClient()`.
- **Metagame**: MVC-Hub vor dem Join (Login, Skin-Auswahl, Server-Browser).
- **Game**: MVC + NGO, eigentlicher Gameplay-Loop, server-autoritativ.

→ Siehe [01-architektur-kontext.md](./01-architektur-kontext.md), [02-scene-setup.md](./02-scene-setup.md).

---

## 2. Player-Input (`PlayerInputController.cs`)

**Pfad**: [Assets/Scripts/Runtime/Game/Input/PlayerInputController.cs](../Assets/Scripts/Runtime/Game/Input/PlayerInputController.cs)

### Was es tut
- Liest die `Move`-Action aus dem geteilten `InputActionAsset` (Map: `Player`, Action: `Move`).
- Stellt `MoveDirection` (Vector2) und `IsMoving` (bool) für `PlayerMovement` bereit.

### Wichtige Fallstricke (hart erkämpft)
- **Shared `InputActionAsset`**: Alle Player-Instanzen (Owner + Remote-Proxies) referenzieren *dasselbe* Asset.
- `OnDisable()` darf **NIEMALS** `map.Disable()` aufrufen — das deaktiviert die Action für *alle* Player gleichzeitig. WASD reagiert dann scheinbar zufällig nicht mehr.
- `OnDisable()` setzt nur die lokale `m_Move`-Referenz auf null.
- `Update()` enthält einen **silent self-heal**: wenn die Action-Reference verloren geht (z.B. Domain-Reload, Re-Spawn), wird sie ohne Log neu aufgelöst.
- Keine Diagnostic-Logs mehr drin (außer einer einmaligen `OnEnable`-Warnung bei null-Asset).

### Nicht tun
- ❌ Eigene `PlayerInput`-Component pro Player, die das Asset clonen würde — bricht NGO-Ownership-Modell.
- ❌ `map.Enable()` in jedem `OnEnable()` — Race mit anderen Instanzen.

---

## 3. Player-Movement (`PlayerMovement.cs`)

**Pfad**: [Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs](../Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs)

### Architektur
Eine einzige `NetworkBehaviour`. Server-autoritativ mit Client-Prediction & Reconciliation, kein separater Predictor/Replicator-Split.

- **Owner Client**: sammelt Input pro Tick → `Simulate(cmd)` lokal → sendet Command via RPC an Server → reconciliiert bei abweichenden Server-Snapshots.
- **Server**: validiert + simuliert authoritativ → broadcastet `NetworkTransform`-State.
- **Remote Clients**: rein interpoliert.

### Simulate-Mapping (kritisch)
```csharp
Vector3 delta = new(cmd.MoveInput.x, 0f, cmd.MoveInput.y);
```
WASD → world-axis-XZ, ungerotiert. Kamera-relative Rotation passiert *außerhalb* von `Simulate` (in der Input-Vorverarbeitung), damit Server & Client deterministisch dieselbe Funktion ausführen.

### CS0414-Cleanup
Das alte `[Header("Debug")] m_LogDiagnostics`-Feld wurde entfernt — Warnung weg, Verhalten identisch (es war nur ein toter Logger-Toggle).

---

## 4. FLARE 8-Direction Sprites

### Direction-Konvention (Atlas-Spalten-Reihenfolge)
| Index | Richtung |
|-------|----------|
| 0     | W        |
| 1     | SW       |
| 2     | S        |
| 3     | SE       |
| 4     | E        |
| 5     | NE       |
| 6     | N        |
| 7     | NW       |

→ Siehe `FlareDirection.cs` (Index ↔ Vector2-Lookup).

### Z-Achsen-Inversion (FLARE vs. Unity)
FLARE-N entspricht Unity **−Z**. Unity-N ist **+Z**. Daher *exakt einmal* invertieren — beim Mapping von World-Velocity auf Visual-Direction in `UpdateVisuals()`:

```csharp
Vector2 visualDir = new(diff.x, -diff.z);
```

**Nicht** in der Physik invertieren — sonst kippt die Steuerung gegenüber der Kamera.

Ergebnis: WASD bewegt physisch +Z = north, und das Sprite zeigt Atlas-Index 6 (N). Ohne den `-diff.z` würden N und S vertauscht (FLARE-Sprite zeigt nach S obwohl Spieler nach N läuft).

---

## 5. Sprite-Asset-Pipeline (FLARE → JSON)

### Quelle
Stendhal-/FLARE-Atlas-Definitionen liegen extern in `c:\Users\anato\Downloads\steam-main\scripts\`:
- `player/male/*.txt` (74 Items)
- `player/female/*.txt` (73 Items — `head_long` statt `head_bald`/`head_short`)
- `npc/*.txt` (93 Gegner)

### Format
```
image=foo.png

[stance]
frames=4
duration=800ms
type=back_forth
frame=ANIM_IDX,DIR_IDX,x,y,w,h,ox,oy
...
```

### Ziel-Schema (matcht `player_male/*.json`)
```json
{
  "image": "foo.png",
  "animations": {
    "stance": {
      "frames_count": 4,
      "duration_ms": 800,
      "type": "back_forth",
      "frames": [           // outer length = frames_count
        [ {x,y,w,h,ox,oy}, ..., {...} ],   // inner length = 8 (eine pro Direction)
        ...
      ]
    }
  }
}
```

- **Indexierung**: txt `frame=A,B,...` → json `frames[A][B]` (A = anim-frame, B = direction).
- **Padding**: fehlende Direction-Slots werden `null`. Bei Player-Items selten relevant (immer 8 Richtungen), bei NPCs häufiger (nur 4 Frontal-Sprites).
- **Duration**: `"800ms"` → `800` (int, ms abgeschnitten).

### Konverter-Tool
**Pfad**: [Tools/Scripts/flare_txt_to_json.py](../Tools/Scripts/flare_txt_to_json.py)

```bash
python d:\Riftstorm\Tools\Scripts\flare_txt_to_json.py <src_dir> <dst_dir>
```

Beispiele:
```bash
# Female (bereits durchgelaufen)
python d:\Riftstorm\Tools\Scripts\flare_txt_to_json.py \
  "c:\Users\anato\Downloads\steam-main\scripts\player\female" \
  "d:\Riftstorm\Assets\StreamingAssets\player_female"
```

### Aktueller Stand (verifiziert)
| Bereich          | Quellen (.txt) | Ziel (.json) | Status |
|------------------|----------------|--------------|--------|
| `player_male`    | 74             | 74           | ✅     |
| `player_female`  | 73             | 73           | ✅     |
| `npc`            | 93             | 93           | ✅     |

`demon.json` war im NPC-Ordner gefehlt → nachträglich konvertiert.

### Asymmetrie (kein Bug)
- `player_male` enthält `head_bald` + `head_short`.
- `player_female` enthält `head_long`.
- Das ist bewusst — die Modelle haben unterschiedliche Frisuren-Slots in den Quelldaten.

---

## 6. Datei-Anker (Schnellzugriff)

| Thema              | Datei |
|--------------------|-------|
| Input              | [PlayerInputController.cs](../Assets/Scripts/Runtime/Game/Input/PlayerInputController.cs) |
| Movement           | [PlayerMovement.cs](../Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs) |
| Sprite-Richtungen  | `FlareDirection.cs` (im Game-Visual-Layer) |
| Boot               | `ApplicationEntryPoint.cs` |
| Player-Spawn       | `GamePlayerBootstrap.cs` |
| Asset-Konverter    | [flare_txt_to_json.py](../Tools/Scripts/flare_txt_to_json.py) |

---

## 7. Offene Punkte (nicht erledigt)

- **RemakeSoF-Parity-Refactor** für `PlayerMovement`: Quake3-/SoF2-Stil mit explizitem `pmove`-Frame, Step-Slide, Stairs-Snap. Aktuell läuft die simple Unity-Variante (CharacterController + manueller Gravity). Soll später durch deterministische Manual-Physics ersetzt werden — siehe Repo-Instruction "Manual Physics".
- **Sprite-Loader** in Unity: die JSON-Dateien werden noch nicht von einem Sprite-Atlas-Importer in URP-Sprites umgesetzt. Pipeline: JSON → `SpriteAtlas`-Slicing über Editor-Tool (steht aus).
