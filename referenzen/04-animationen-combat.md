# 04 – Animationen, Waffen-Mapping & Combat-Integration

Kontext zum FLARE-Animationssystem und wie es **server-authoritativ** in Riftstorm eingebunden wird.
Basis: Reverse-Engineering aus `c:\Users\anato\Downloads\steam-main\` (Stendhal-Quellcode) + bestehender Riftstorm-Code in [Assets/Scripts/Runtime/Game/Sprites/](../Assets/Scripts/Runtime/Game/Sprites/).

---

## 1. Vollständige Animationsliste

Jedes Player- und NPC-Sprite-Asset enthält maximal diese 8 States (im FLARE-Atlas):

| Section (.txt) | `AnimId` enum | Zweck | Trigger-Typ |
|---|---|---|---|
| `[stance]` | `Stance = 0` | Idle | passiv (Default) |
| `[run]` | `Run = 1` | Laufen | bewegungsabgeleitet |
| `[swing]` | `Swing = 9` | Nahkampf-Schlag | **Aktion** (Attack mit Melee-Waffe) |
| `[shoot]` | `Shoot = 11` | Fernkampf-Schuss | **Aktion** (Attack mit Ranged-Waffe) |
| `[cast]` | `Cast = 3` | Zauberspruch | **Aktion** (Spell wirken) |
| `[block]` | `Block = 10` | Schild-Block aktiv | **Zustand** (Schild equipped) |
| `[hit]` | `Hit = 4` | Treffer empfangen | **Reaktion** (Damage-Event) |
| `[die]` | `Die = 5` | Tod | **Zustand** (HP ≤ 0) |

Im Engine-Enum `UnitDefines::AnimId` existieren zusätzlich `Attack=2`, `CastAlt=6`, `Spawn=7`, `CritDie=8`.
Die sind in Player-Sprites **nicht** vorhanden:
- `Attack` ist ein generischer Alias.
- `Spawn`, `CritDie` sind NPC-only.
- `CastAlt` ist optional (kein Player-Sprite verwendet es).

Quelle: [UnitDefines.h](file:///c%3A/Users/anato/Downloads/steam-main/source_server/Shared/UnitDefines.h) Z. 120–134.

---

## 2. Waffen-Typen und Anim-Dispatch

### `ItemDefines::WeaponType` ([ItemDefines.h](file:///c%3A/Users/anato/Downloads/steam-main/source_server/Shared/ItemDefines.h) Z. 8–22)

```
None=0, Sword=1, Axe=2, Mace=3, Dagger=4, Staff=5,
Bow=6, Crossbow=7, Wand=8, Gun=9, Polearm=10, Fist=11
```

### Regel für die Basisattacke

Aus Sprite-Coverage rekonstruiert (siehe `StreamingAssets/player_male/*.json`):

```
Bow | Crossbow | Gun        → AnimId.Shoot
sonst (inkl. None=unarmed)  → AnimId.Swing
```

**Beweis:** Genau diese 10 Melee-Items haben **kein** `[shoot]` im Atlas:
`battle_axe, club, hand_axe, infantry_axe, mace, maul, reinforced_club,
smith_hammer, war_hammer, zweihander`.

### Cast ist **nicht** waffenabhängig

`AnimId.Cast` wird durch das Wirken eines **Spells** getriggert, nicht durch
die equippte Waffe. Deshalb hat jedes Item-Sprite `[cast]` — auch Äxte.
Im Original läuft das über `Server/src/Combat/SpellCaster.cpp`.

### Block / Hit / Die

- `Block`: Zustand, solange der Spieler aktiv blockt (Schild hoch / Stance halten).
- `Hit`: Reaktion auf ein **eingehendes Damage-Event** (kurz, `PlayOnce`).
- `Die`: Endzustand, läuft einmal und friert auf dem letzten Frame ein.

---

## 3. Anim-Typen (Loop-Verhalten)

Aus `ModelScript.cpp` Z. 138–151:

| FLARE `type=` | Verhalten | Typische Anims |
|---|---|---|
| `looped` | endlos | stance, run, block |
| `play_once` | einmal abspielen, dann stehen bleiben | swing, shoot, cast, hit, die |
| `back_forth` | hin und zurück | (selten, manche Spell-Effekte) |

Schon korrekt umgesetzt in [`FlareAnimationType`](../Assets/Scripts/Runtime/Game/Sprites/FlareAtlasData.cs).

---

## 4. Sprite-Asset-Inventar

| Pfad | Status |
|---|---|
| `Assets/StreamingAssets/player_male/*.json` | 74/74 vollständig |
| `Assets/StreamingAssets/player_female/*.json` | 73/73 vollständig |
| `Assets/StreamingAssets/npc/*.json` | 93/93 vollständig (inkl. `demon.json`) |

Asymmetrie Male vs. Female ist **quellseitig gewollt**: Male hat `head_bald` + `head_short`, Female `head_long`. Nicht "fixen".

Konverter: [Tools/Scripts/flare_txt_to_json.py](../Tools/Scripts/flare_txt_to_json.py).

---

## 5. Riftstorm-Direction-Mapping (WICHTIG)

Riftstorm-Index-Order (siehe [`PlayerMovement.ComputeFlareDirection`](../Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs)):

```
0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW
```

Stendhal C++ `Direction` enum hat eine **andere** Reihenfolge:
```
South=0, SouthWest=1, West=2, NorthWest=3,
North=4, NorthEast=5, East=6, SouthEast=7
```

Der `flare_txt_to_json.py`-Konverter übernimmt die `.txt`-Indizes **as-is**.
Da die Sprite-Sheets im Atlas selbst nach FLARE-Direction sortiert sind und der
Konverter das Layout 1:1 spiegelt, ist das aktuell konsistent — der Riftstorm-Code
mappt nur Unity-`Vector2` → eigenen Index. **Nicht** auf C++-Indizes verlassen,
falls jemals ein zweites Datenformat dazukommt.

Z-Flip nur in `PlayerMovement.UpdateVisuals()` (`new Vector2(diff.x, -diff.z)`),
**niemals** in der Physik.

---

## 6. Bestehende Riftstorm-Komponenten

| Komponente | Datei | Verantwortung |
|---|---|---|
| `FlareAtlasData` | [FlareAtlasData.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareAtlasData.cs) | JSON-Schema (image, animations[name].frames[fr][dir]) |
| `FlareAtlasLoader` | [FlareAtlasLoader.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareAtlasLoader.cs) | Lädt JSON + zugehörige Textur aus `StreamingAssets` |
| `FlareAtlas` | [FlareAtlas.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareAtlas.cs) | Runtime-Repräsentation: name → `FlareAnimation` |
| `FlareLayerAnimator` | [FlareLayerAnimator.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareLayerAnimator.cs) | Eine Schicht (z. B. chest), spielt einen Anim-State auf einem `SpriteRenderer` ab |
| `FlareCharacter` | [FlareCharacter.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareCharacter.cs) | Komposition aus Layern, synchronisiert `Play(name)` + `SetDirection(idx)` |
| `PlayerMovement.UpdateVisuals` | [PlayerMovement.cs](../Assets/Scripts/Runtime/Game/Movement/PlayerMovement.cs) | Treibt aktuell `stance`/`run` aus Transform-Diff |

**Fehlt komplett:** Combat-State (Swing/Shoot/Cast/Block/Hit/Die), Weapon-Equipment-System, Netzwerk-Sync für Combat-Anims.

---

## 7. Server-autoritatives Combat-Modell

### Was muss server-authoritativ sein?

| State | Authority | Begründung |
|---|---|---|
| **HP / Damage / Tod** | Server | Trivially exploit-anfällig. Client darf das **nie** entscheiden. |
| **Hit-Validation** (trifft Spieler X Spieler Y?) | Server | Cheat-Vektor #1. |
| **Cooldowns** (kann ich jetzt attacken?) | Server | Speed-Hack-Schutz. |
| **Welche Anim spielt** (Swing vs. Shoot vs. Cast) | Server entscheidet, Client darf **lokal vorhersagen** | Konsistenz für Remote-Beobachter; Predicted Visuals für Owner. |
| **Block-Zustand** | Server (NetworkVariable) | Reduziert eingehenden Schaden → autoritativ. |
| **Direction / Anim-Frame** | Client-lokal abgeleitet | Visuell, nicht gameplay-relevant. NGO-Bandwidth sparen. |

### Net-Topology (NGO)

```
[Client Input]
      │  ClientRpc "AttackPressed" (mit timestamp)
      ▼
[Server PlayerCombat]
      │  validiert: cooldown? alive? in range?
      │  würfelt: hit/miss, damage
      ├─► NetworkVariable<CombatState> { State, EndTick, WeaponType }
      │   (alle Clients sehen das automatisch)
      └─► ClientRpc "DamageDealt" → trigger Hit-Anim auf Ziel
                                    + Screen-Shake auf Owner
```

`CombatState` ist ein kleines Struct mit `enum CombatAnim { None, Swing, Shoot, Cast, Block, Hit, Die }` + Server-Tick, ab dem die Anim läuft. Sobald `NetworkVariable` sich ändert, treibt jeder Peer lokal `FlareCharacter.Play(name)` — Server-authoritative Anim ohne RPC-Sturm.

---

## 9. Anti-Patterns (nicht machen)

- ❌ Anim-Wechsel per `ClientRpc` an alle Spieler senden. Stattdessen `NetworkVariable<CombatStateSnapshot>` → ein State-Wert, jeder Peer leitet die Anim lokal ab.
- ❌ Pro Projektil ein `NetworkObject`. Stattdessen hitscan + ein Event.
- ❌ Hit-Detection auf dem Client. Client darf **maximal** Telegraph-Visuals zeigen, niemals HP ändern.
- ❌ Coroutines für Anim-Timing. Stattdessen `FlareLayerAnimator.IsFinished` + Tick-basierte Resolves im Server.
- ❌ `Time.deltaTime` für authoritative Gameplay-Logik. Server-Tick verwenden.
- ❌ Anim-Strings hartcoden außerhalb der `WeaponDefinition` / `CombatAnim`-Helper.