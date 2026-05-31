# Phase 16B — Equipment + PlayerStats

**Scope:** B2 aus dem Phase-16-Plan. CombatFormulas-Port und der
PlayerCombat-Damage-Path-Switch wurden bewusst auf Phase 16C verschoben.

## Lieferumfang

### Neue Dateien
- `Assets/Scripts/Runtime/Game/Items/PlayerEquipment.cs`
  NetworkBehaviour mit `NetworkList<int>` (Index 1..11 = Source-EquipType-Mapping,
  Index 0 reserviert). ServerRpcs `RequestEquipFromInventoryServerRpc(int)` und
  `RequestUnequipServerRpc(EquipSlot)`. Event `EquipChanged(slot, newTemplateId)`.
- `Assets/Scripts/Runtime/Game/Combat/PlayerStats.cs`
  Reiner Aggregator (MonoBehaviour, kein NetworkBehaviour). Basis aus
  `UnitStats`, Item-Boni aus `PlayerEquipment` via `ItemCatalogLoader`. Event
  `StatsChanged` fuer HUD-Refresh.
- `Assets/Scripts/Runtime/Game/UI/Console/Commands/EquipCommand.cs`
  `/equip <inventorySlotIndex>` — schickt ServerRpc, Slot-Wahl macht Server.
- `Assets/Scripts/Runtime/Game/UI/Console/Commands/UnequipCommand.cs`
  `/unequip <slot>` — Slot per Name (`weapon`, `shield`, ...) oder Zahl (1..11).

### Geaenderte Dateien
- `Assets/Scripts/Runtime/Game/Items/PlayerInventory.cs`
  + `TryAddItemServer(int templateId, int count)` (Convenience-Overload, holt
    Template selbst).
  + `FindFirstEmptySlot()` — Server-Helper fuer Equipment-Swap-Fallback.
  + `TrySetSlotServer(int slotIndex, InventoryItem item)` — Server-only,
    erlaubt Direct-Write fuer 1:1-Swap.
- `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs`
  + `using Riftstorm.Game.Items;`
  + `internal void Server_ApplyWeaponFromTemplate(int templateId)`
  + `internal void Server_ApplyOffhandFromTemplate(int templateId)`
  Beide spiegeln 1:1 die existierende RPC-Logik (Katalog-Validierung,
  Attack-Cancel, Zweihaender-Offhand-Clear). RPCs `RequestEquipWeaponServerRpc`
  und `RequestEquipOffhandServerRpc` bleiben unangetastet — `WeaponCommand` /
  `OffhandCommand` funktionieren weiter als Dev-Shortcut, ohne durch das
  Equipment-System zu laufen.
- `Assets/Scripts/Runtime/Game/UI/Console/ConsoleManager.cs`
  Registriert `EquipCommand` + `UnequipCommand`.

## Architektur-Entscheidungen

### Bridge statt Migration
PlayerEquipment **ersetzt** die bestehenden NetVars `m_CurrentWeaponId` /
`m_CurrentOffhandId` auf `PlayerCombat` **nicht**. Stattdessen ruft das
`OnListChanged`-Callback serverseitig die neuen
`Server_ApplyWeaponFromTemplate(int)` / `Server_ApplyOffhandFromTemplate(int)`
Methoden auf, die das `ItemTemplate.Model` (string) als Brueckenwert in die
existierenden Combat-NetVars schreiben.

**Vorteile:**
- Source-Parity der Combat-Pipeline (Weapon/Offhand-Catalog → CombatVisuals
  → Attack-Cooldown) bleibt unveraendert.
- WeaponCommand / OffhandCommand bleiben als Dev-Tool funktional und springen
  am Equipment vorbei.
- Phase 16C (CombatFormulas) konsumiert `PlayerStats.GetTotal(StatId)`
  unabhaengig davon, ob das Item-System oder ein Dev-Command die Waffe gesetzt
  hat.

### Equip-Move-Semantik
1:1-Swap. Equipment-Items sind in v1 nicht stackbar; der freigewordene
Inventory-Slot bekommt das vorher ausgeruestete Item zurueck. Zweihaender-Equip
raeumt **vor** dem eigentlichen Swap die Slots Shield + Ranged ins Inventar
zurueck — schlaegt einer dieser Stash-Versuche fehl (Inventar voll), wird der
gesamte Equip-Vorgang abgebrochen, damit nichts verloren geht. Warnung im Log.

### Zweihaender-Erkennung
Source liefert in `ItemTemplate.WeaponType` Numerik, die Riftstorm nicht
durchgehend gemappt hat. Wir leiten den 2H-Flag stattdessen aus
`WeaponCatalog` ab: `ItemTemplate.Model` → `WeaponDefinition.IsTwoHanded`.
Damit bleibt der Combat-Catalog die einzige Quelle der Wahrheit fuer
Waffen-Mechanik. Default bei jedem Lookup-Failure: `false` (sicherer als
Equip-Abort).

### Stat-Aggregation
`PlayerStats.GetTotal(StatId) = GetBase(StatId) + GetEquipmentBonus(StatId)`.
Buffs/Auras fehlen — der `+ 0`-Term ist als markiert. `StatId`
ist eine Subset-Kopie der Source-`Stat`-Enum mit identischen numerischen
Werten, damit `ItemTemplate.StatTypeN` ohne Mapping konsumiert wird.

`UnitStats`-Mapping fuer `GetBase` deckt ab, was UnitStats heute exponiert
(Health, Armor, Strength, Willpower, Intelligence, WeaponValue, MeleeCritical,
DodgeRating, BlockRating, ResistFire/Frost/Shadow/Holy). Alles andere faellt
auf 0 zurueck und wird de facto reines Item-Stat — bewusst, damit
`ItemTemplate`-Boni auf z.B. Agility/MeleeCooldown/RangedCritical bereits
heute live sind, sobald CombatFormulas sie liest.

## Deferred → Phase 16C
- `Riftstorm.Gameplay.Combat.CombatFormulas` Port aus Source.
- PlayerCombat-Damage-Path-Switch auf den neuen Formulas-Pfad mit
  `PlayerStats.GetTotal(StatId)` als Input.
- Buff/Aura-Layer in `PlayerStats` (das `+ 0` aus `GetTotal`).
- HUD-Bindings an `PlayerStats.StatsChanged` (Stat-Sheet, Tooltips).

## Manueller Schritt im Editor
**Wichtig:** `PlayerEquipment` ist eine NetworkBehaviour und muss vor dem
Network-Spawn auf dem Prefab existieren. Beim Anfassen:

1. `Assets/Prefabs/PlayerCharacter.prefab` oeffnen.
2. Component `PlayerEquipment` adden (NetworkBehaviour — Reihenfolge relativ zu
   `PlayerInventory`/`PlayerCombat` ist egal, alle drei sollen auf dem Root
   sitzen).
3. Component `PlayerStats` adden (MonoBehaviour).
4. Im Inspector von `PlayerStats`:
   - `m_BaseStats` → `UnitStats`-Component des Prefabs droppen (Awake-Fallback
     loest auch ohne, aber explizit ist sauberer).
   - `m_Equipment` → die frisch addete `PlayerEquipment` droppen.
5. Prefab speichern.

`PlayerInventory.TryAddItemServer` ist unveraendert kompatibel — `/give`
funktioniert wie vorher.

## Validierung
- `get_errors` ueber alle 7 angefassten Files: clean.
- Console-Workflow (Smoke-Test im Editor):
  1. Server starten.
  2. `/give 25 1` (Buckler-Template-Id aus Phase 15 Notes anpassen).
  3. `/inventory` → Slot 0 zeigt das Item.
  4. `/equip 0` → Slot wandert in EquipSlot.Shield.
  5. `/weapon greatsword` (oder Equip eines 2H-Items per `/equip`) → Shield
     landet zurueck im Inventar.
  6. `/unequip weapon` oder `/unequip 9` → Waffe geht zurueck ins Inventar.

## Naechste Phase (16C)
CombatFormulas-Port + Damage-Pipeline-Switch. Ab dann liest die Schaden-
Resolution `PlayerStats.GetTotal(StatId.WeaponValue)` / `StatId.Strength` etc.
statt direkt `UnitStats`.
