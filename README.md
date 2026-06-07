# Riftstorm

> **Multiplayer Dark-Fantasy PvPvE isometric Topdown-Action MOBA/MMO/ARPG-Hybrid Old School Runescape-Inspired**

- Iso-Sprite-Konvention, 8-Richtungs-Asset-Layout
- **Dedicated Server**, **server-authoritativ**. (Server-Only / Client-Only)
- **Reconnect-Support** erwünscht (NGO unterstützt das nativ via Persistent-Session-Pattern).
- Jeweils data-driven via JSON in `StreamingAssets/` + StateMachine, wo passend.
-

---

# notizen
Geparst, aber NICHT konsumiert (tote Felder):

cast_interrupt_flags (240/242=12, 272=8) — deklariert, nirgends ausgewertet.
prevention_type (8208/16) — deklariert, nirgends ausgewertet.
InterruptCast(22) gegen NPCs = No-op: UnitStats.ServerInterruptCast delegiert nur an PlayerCombat → der Spieler kann einen NPC-Cast nicht per Kick/Interrupt-Spell unterbrechen. NPC-Casts brechen nur durch Stun oder Zielverlust ab.

Nicht implementierte SpellEffects (fallen in default: und werden still übersprungen): ManaDrain(5), HealthDrain(8), SummonNpc(10), CreateItem(12), SummonObject(23), ScriptEffect(26), LootEffect(32), Kill(33), Gossip(34), Inspect(35), ApplyGemSocket(36), Duel(38), ApplyOrbEnchant(40/47), LearnSpell(41), NearestWp(42), DestroyGems(44), CombineItem(45), ExtractOrb(46). Tombshiver nutzt keinen davon — für ihn also irrelevant.

Bit	Wert	Mechanic
0	1	Confused
1	2	Pacify
2	4	Fear
3	8	Root
4	16	Silence
5	32	Sleep
6	64	Snare
7	128	Stun
8	256	Incapacitated
9	512	Disrupt
10	1024	Polymorph
11	2048	Charging
12	4096	Stealth
Tombshiver = 13751 = Confused + Pacify + Fear + Silence + Sleep + Stun + Incapacitated + Polymorph + Stealth + 8192.
---

# flare layer=direction, list of types in order of appearance, first item will be printed on screen first
layer=SW,main,feet,legs,hands,chest,off,head
layer=W,main,feet,legs,hands,chest,off,head
layer=NW,main,feet,legs,hands,chest,off,head
layer=N,feet,legs,hands,chest,off,head,main
layer=NE,feet,legs,hands,chest,off,head,main
layer=E,feet,legs,hands,chest,off,head,main
layer=SE,feet,legs,hands,main,chest,head,off
layer=S,main,feet,legs,hands,chest,head,off



## Netcode- & Authority-Modell

- **Server ist Single Source of Truth** für alle gameplay-relevanten Entscheidungen
  (Damage, Cooldowns, Hit-Validation, Movement-Validation, Loot-Drops).
- **Clients senden nur Inputs / Intentions** (Move-Targets, Cast-Requests).
- **Client-Prediction** für eigene Bewegung; **Reconciliation** bei Server-Korrektur.
- **Enemies werden nicht als individuelle NetworkObjects repliziert.** Snapshot-Sync
  + AOI-Culling (Area-of-Interest) bzw. Fog of War + Performance. Enemies haben Server-Only-IDs, Clients tracken sie lokal. Ähnlich wie LoL für Minions.
- **RPCs minimal halten.** Events statt vollständiger Object-Sync wo möglich.
- **Visuelle Effekte** laufen client-seitig (kein Sync für jeden Partikel).

---

## Art Direction

- **Dark Fantasy**, gothic/diablo-leaning, gedeckte Palette mit gezielten Akzentfarben.
- **Topdown Iso-Sprites**, 8 Richtungen, FLARE-Konvention.
- **Lesbarkeit > Fidelity.** In Horde-Combat müssen Enemy-Silhouetten und Telegraphs sofort erkennbar bleiben.
- **Color-Coding**:
  - Fraktion / Team → harte, konsistente Farben
  - Damage-Type → konsistente Tönungen
  - Danger-Zones → reservierte Warn-Farben (rot/orange)

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
FLARE-N entspricht Unity **−Z**. Unity-N ist **+Z**. Daher *exakt einmal* invertieren — beim Mapping von World-Velocity auf Visual-Direction in 
`UpdateVisuals()`:

```csharp
Vector2 visualDir = new(diff.x, -diff.z);
```

**Nicht** in der Physik invertieren — sonst kippt die Steuerung gegenüber der Kamera.

Ergebnis: WASD bewegt physisch +Z = north, und das Sprite zeigt Atlas-Index 6 (N). Ohne den `-diff.z` würden N und S vertauscht (FLARE-Sprite zeigt nach S obwohl Spieler nach N läuft).

---

## Sprite-Asset-Pipeline (FLARE → JSON)

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
**Pfad**: [Tools/Scripts/flare_txt_to_json.py](./Tools/Scripts/flare_txt_to_json.py)

---

## Anti-Patterns (NICHT machen)

- ❌ Coroutines für Gameplay-Flow (stattdessen State Machines)
- ❌ `Update()` mit Polling-Checks (stattdessen Events)
- ❌ Singleton-Zugriffe über `ApplicationEntryPoint.Singleton` für Services (stattdessen `ServiceLocator.Get<T>()`)
- ❌ Magic Numbers / Strings (stattdessen Konstanten/DTOs + JSON-Data)
- ❌ Monolithische Skill-Klassen (stattdessen Effect-Composition)
- ❌ Client-Side Damage/Hit-Detection (immer Server-Authoritative)
- ❌ LINQ in Gameplay-Hot-Paths
- ❌ Per-Frame Heap-Allocations in Gameplay-Loops (Object Pools!)
- ❌ Reflection in Runtime-Gameplay-Systems
- ❌ Jede Kugel / jedes Partikel als `NetworkObject` synchronisieren — nur Events/Seeds senden
- ❌ NGO-RPCs für hochfrequente Streams (NetworkVariables oder Custom Snapshot-System nutzen)
- ❌ Eigene `PlayerInput`-Component pro Player, die das Asset clonen würde — bricht NGO-Ownership-Modell.
- ❌ `map.Enable()` in jedem `OnEnable()` — Race mit anderen Instanzen.

# other ideas:
- ECS architecture wird eingebaut, um die Performance zu verbessern und die Entwicklung zu erleichtern.