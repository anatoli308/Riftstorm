# 15 – Equipment-System: Runtime Atlas-Swap fuer Waffen & Offhand

Kontext zur Server-autoritativen Equipment-Pipeline, die `/weapon` und `/offhand`
auf die bereits vorhandenen 2D-FLARE-Layer im `PlayerCharacter.prefab` mappt.
Implementiert in dieser Session am 2026-05-20.

Vorgaenger-Kontext: [04-animationen-combat.md](04-animationen-combat.md),
[08-isometric-sprites-richtungen.md](08-isometric-sprites-richtungen.md).

---

## 1. Ziel & Constraint

- **Ziel:** `/weapon longbow` (und `/offhand buckler`, `/offhand none`) muss auf
  jedem Peer den entsprechenden Sprite-Layer der Spielfigur austauschen — ohne
  Prefab-Edit, ohne Resources, ohne neue ScriptableObjects.
- **Hard Constraint vom User:** Die bestehenden FLARE-Layer im Prefab
  (`m_LayerAtlases = [default_legs, default_feet, default_chest, default_hands,
  head_short, longsword, buckler]`) bleiben unangetastet. Stattdessen wird im
  Bootstrap gefiltert und es werden zwei dedizierte Slot-Layer `MainHand` und
  `OffHand` angelegt, die zur Laufzeit per NetVar-Replikation gefuellt werden.
- **Architektur-Regeln aus copilot-instructions:** Server-autoritativ
  (ServerRpc, RequireOwnership=true Default), JSON in `StreamingAssets`,
  `new()`-Syntax, deutsche XML-Doku ohne Umlaute, kein Polling, NEW Input System
  only, MVC + StateMachine + ServiceLocator beibehalten.

---

## 2. End-to-End-Flow

```
Client (Owner)        Server                          Alle Peers
-----------------     -----------------------------   ---------------------------
/weapon longbow
  │
  └─ WeaponCommand.Execute
        │ frueh-validiert via WeaponCatalogLoader.GetCached()
        └─ combat.TryRequestEquipWeapon("longbow")
              │
              └─ RequestEquipWeaponServerRpc ─────► validiert ueber Catalog
                                                    setzt m_CurrentWeaponId.Value
                                                    falls IsTwoHanded:
                                                       m_CurrentOffhandId.Value = default
                                                              │
                                                              ▼
                                                    NetworkVariable<FixedString64Bytes>
                                                    repliziert an alle Peers
                                                              │
                                                              ▼
                                                    OnNetWeaponChanged(old,new)
                                                       feuert PlayerCombat.WeaponChanged
                                                              │
                                                              ▼
                                                    PlayerEquipmentVisuals.OnWeaponChanged
                                                       ApplyAsync("MainHand", id, true)
                                                          ├─ Cache-Check (idempotent)
                                                          ├─ await FlareAtlasLoader.LoadAsync(id)
                                                          ├─ CTS-Race-Re-Check nach await
                                                          └─ character.SetLayerAtlas("MainHand", atlas)
                                                                │
                                                                ▼
                                                       FlareLayerAnimator.SwapAtlas(
                                                          atlas, currentAnim, currentDir)
                                                          ├─ atlas/state atomar setzen
                                                          ├─ m_Direction = dir & 7
                                                          └─ Play(anim, force=true)
```

---

## 3. Geaenderte / neue Dateien

| Datei | Status | Zweck |
|---|---|---|
| `Assets/Scripts/Runtime/Gameplay/Combat/Handedness.cs` | NEW | Enum `OneHanded=0`, `TwoHanded=1` |
| `Assets/Scripts/Runtime/Gameplay/Combat/WeaponDefinition.cs` | EDIT | `[JsonProperty("handedness")] [StringEnumConverter] Handedness`; `IsTwoHanded` |
| `Assets/Scripts/Runtime/Game/Sprites/FlareLayerAnimator.cs` | EDIT | `public void SwapAtlas(FlareAtlas, string anim, int dir)` — atomarer State-Reset + Play |
| `Assets/Scripts/Runtime/Game/Sprites/FlareCharacter.cs` | EDIT | `public bool SetLayerAtlas(string layerName, FlareAtlas)` — Layer-Lookup ueber `gameObject.name` |
| `Assets/Scripts/Runtime/Game/Combat/PlayerEquipmentVisuals.cs` | NEW | MonoBehaviour, abonniert `PlayerCombat`-Events, ruft `ApplyAsync` mit CTS + Cache |
| `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs` | EDIT | `m_DefaultOffhandId`, `OffhandCatalogLoader`, `CurrentWeaponId`/`CurrentOffhandId`, `WeaponChanged`/`OffhandChanged` Events, NetVar-Subscribes, `TryRequestEquipOffhand`, `RequestEquipOffhandServerRpc`, 2H-Eviction in Weapon-RPC |
| `Assets/Scripts/Runtime/Game/Bootstrap/GamePlayerBootstrap.cs` | EDIT | `BuildVisualsAsync` filtert Weapon/Offhand-IDs aus `m_LayerAtlases`, legt feste `MainHand`/`OffHand`-Layer an, bindet `PlayerEquipmentVisuals` |
| `Assets/Scripts/Runtime/Game/UI/Console/Commands/OffhandCommand.cs` | NEW | `/offhand <id|none|clear>` + Preview |
| `Assets/Scripts/Runtime/Game/UI/Console/ConsoleManager.cs` | EDIT | Registrierung `OffhandCommand` neben `WeaponCommand` |
| `Assets/StreamingAssets/combat/weapons.json` | EDIT | `"handedness": "TwoHanded"` an 12 IDs: greatsword, zweihander, war_hammer, maul, greatstaff(_purple/_red), shortbow, longbow, greatbow(_orange), slingshot |

---

## 4. Wichtige technische Entscheidungen

### 4.1 Warum `SwapAtlas` als atomare API?

`SetAtlas(...)` plus separates `Play(...)` reichte nicht: `Play` hat einen
Early-Out, wenn der angeforderte State/Direction dem aktuellen entspricht,
und `m_Direction` blieb auf `0` haengen, sobald der Layer frisch erzeugt
wurde. `SwapAtlas` setzt Atlas, Direction (`dir & 7`) und Animation in einem
Schritt mit `force=true` und entkoppelt damit den Race zwischen Layer-Spawn
und erstem Equip-Event.

### 4.2 Warum dedizierte `MainHand`/`OffHand`-Layer im Bootstrap statt Prefab-Edit?

Der Prefab-Stand enthaelt `longsword` + `buckler` als feste Body-Layer.
`GamePlayerBootstrap.BuildVisualsAsync` laedt jetzt zuerst beide Kataloge
(`WeaponCatalogLoader`, `OffhandCatalogLoader`), durchlaeuft
`m_LayerAtlases` und sortiert jede ID, die im Catalog vorkommt, in eine
Legacy-Liste aus. Nur die uebrigen Body-IDs werden als regulaere Layer
angelegt; danach werden immer zwei zusaetzliche Slot-Layer mit den festen
Namen `PlayerEquipmentVisuals.MainHandLayerName` und `OffHandLayerName`
erzeugt (Sorting-Order `bodyCount` bzw. `bodyCount + 1`). Diese werden
zunaechst ohne Atlas registriert; `PlayerEquipmentVisuals.Bind(...)`
applied sofort den aktuellen NetVar-Stand. So bleibt die Prefab-Quelle
unveraendert, neue Spieler bekommen trotzdem die Server-Defaults
(`m_DefaultWeaponId`, `m_DefaultOffhandId`).

Fallback-Pfad (kein `PlayerCombat`-Component): `BuildVisualsAsync` weist
die Legacy-IDs (`longsword`, `buckler`) direkt via `SetLayerAtlas` zu, damit
nicht-Combat-Prefabs visuell intakt bleiben.

### 4.3 Race-Schutz in `PlayerEquipmentVisuals.ApplyAsync`

- **CTS pro Visuals-Instanz:** `m_Cts` wird bei jedem `ApplyAsync` neu
  ausgestellt; ein nachfolgender Equip-Event canceled die laufende
  Atlas-Ladung.
- **Idempotenz-Cache:** `m_AppliedMainHandId` / `m_AppliedOffHandId`
  vermeiden doppelte Loads, wenn z. B. Bind den aktuellen Stand und
  unmittelbar danach das OnValueChanged-Event denselben Stand liefern.
- **Re-Check nach await:** Nach `await LoadAsync(...)` wird die Ziel-ID
  erneut mit der Live-ID aus `PlayerCombat` verglichen, bevor
  `SetLayerAtlas` ausgefuehrt wird.

### 4.4 2H/Offhand-Exclusivity (server-autoritativ)

- `RequestEquipWeaponServerRpc`: Nach erfolgreichem
  `m_CurrentWeaponId.Value = weaponId;` wird bei `weaponDef.IsTwoHanded`
  und nicht-leerer Offhand der Offhand-Slot ueber
  `m_CurrentOffhandId.Value = default` geleert. Replikation feuert das
  Offhand-Event automatisch.
- `RequestEquipOffhandServerRpc`: Lehnt Equip ab, falls die aktuell aktive
  Waffe (`ResolveCurrentWeapon().IsTwoHanded`) zweihaendig ist. Leerer
  Request (`""`) clearet immer.
- Default-Init in `OnNetworkSpawn` (Server-Side): Setzt
  `m_CurrentOffhandId.Value = m_DefaultOffhandId` nur, wenn die
  Default-Waffe NICHT zweihaendig ist.

### 4.5 Console-Commands

- `WeaponCommand` und `OffhandCommand` validieren auf dem Owner-Client
  schon vor dem ServerRpc, indem sie den jeweiligen Catalog via
  `ServiceLocator.Get<...>()?.GetCached()` befragen. Bei unbekannter ID
  wird in den `ConsoleLog` geschrieben und der ServerRpc gar nicht erst
  gefeuert.
- `OffhandCommand` normalisiert `"none"`, `"clear"` und leeres Argument
  zu `string.Empty` und delegiert an `combat.TryRequestEquipOffhand(...)`,
  das wiederum `RequestEquipOffhandServerRpc` mit leerem
  `FixedString64Bytes` triggert.
- Registrierung erfolgt explizit (kein Reflection-Scan, siehe
  copilot-instructions: keine Reflection im Runtime-Gameplay).

---

## 5. JSON-Schema-Erweiterung

`StreamingAssets/combat/weapons.json` (Beispiel `longbow`):

```json
{
  "id": "longbow",
  "handedness": "TwoHanded",
  "type": "Bow",
  "attack_cooldown": 1.1,
  "range": 8.0,
  "base_damage": 14,
  "hit_resolve_progress": 0.45
}
```

`handedness` ist optional. Fehlt das Feld, greift der Enum-Default
`OneHanded=0` (StringEnumConverter ist tolerant gegenueber missing).

`StreamingAssets/combat/offhand_items.json` bleibt unveraendert
(`buckler`, `iron_buckler`, `shield`).

---

## 6. Manueller Test-Sweep (in-Editor)

1. Host + Client starten. Beide Figuren erscheinen mit Default-Setup
   (Longsword + Buckler oder konfigurierte Defaults).
2. `/weapon longbow` →
   - MainHand-Layer zeigt Longbow.
   - OffHand-Layer wird geleert (2H-Eviction).
3. `/weapon shortsword` → MainHand = Shortsword. OffHand bleibt leer.
4. `/offhand buckler` → OffHand-Layer zeigt Buckler.
5. `/weapon zweihander` → OffHand wird automatisch wieder evicted.
6. `/offhand buckler` waehrend `zweihander` aktiv → Server lehnt ab
   (kein Sprite-Wechsel, kein NetVar-Write).
7. `/offhand none` → OffHand-Layer leer.
8. `/weapon foo` → Console-Error, kein NetVar-Write.

---

## 7. Naechste, bewusst NICHT umgesetzte Punkte

- **Animationsumschaltung pro Weapon-Type:** Aktuell wechselt nur der
  Atlas; `swing`/`shoot`/`cast` werden weiter ueber bestehende
  Combat-Logik gewaehlt. Das Mapping `WeaponType → AnimId` lebt
  weiterhin in [04-animationen-combat.md](04-animationen-combat.md).
- **Backhand / Ammo / Quiver-Layer:** Nicht modelliert. Ein dritter
  Slot-Layer (z. B. `Quiver` oder `Back`) liesse sich analog zu
  `OffHand` ergaenzen — gleiche Pipeline, eigenes NetVar.
- **Inventory-Persistenz:** `m_DefaultWeaponId` / `m_DefaultOffhandId`
  sind serialisierte Felder am `PlayerCombat`. Echte Loadout-Persistenz
  ueber Backend-Profile ist nicht Teil dieser Session.
- **Server-seitige Range-/Cooldown-Validierung nach Equip:** Combat-Stats
  werden ueber den existierenden `WeaponDefinition`-Resolver gelesen,
  keine zusaetzliche Server-Side-Validation des Schadens-Rounds gegen
  Cheating in dieser Iteration veraendert.

---

## 8. Anker-Quellen im Repo

- Server-Defaults & RPCs:
  [PlayerCombat.cs](../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs)
- Sprite-Layer-Swap:
  [FlareLayerAnimator.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareLayerAnimator.cs),
  [FlareCharacter.cs](../Assets/Scripts/Runtime/Game/Sprites/FlareCharacter.cs)
- Visuals-Bridge:
  [PlayerEquipmentVisuals.cs](../Assets/Scripts/Runtime/Game/Combat/PlayerEquipmentVisuals.cs)
- Bootstrap-Filter:
  [GamePlayerBootstrap.cs](../Assets/Scripts/Runtime/Game/Bootstrap/GamePlayerBootstrap.cs)
- Console-Commands:
  [WeaponCommand.cs](../Assets/Scripts/Runtime/Game/UI/Console/Commands/WeaponCommand.cs),
  [OffhandCommand.cs](../Assets/Scripts/Runtime/Game/UI/Console/Commands/OffhandCommand.cs)
- Data:
  [weapons.json](../Assets/StreamingAssets/combat/weapons.json),
  [offhand_items.json](../Assets/StreamingAssets/combat/offhand_items.json)
