# Phase 16 — Inventory Foundation (Session A)

> Session A liefert die server-autoritative Datenebene fuer Items. Equipment-Slots,
> Stats-Aggregator und Combat-Scaling kommen in **Session B** (siehe unten).

## Scope (Session A)

| # | Deliverable | Status |
|---|-------------|--------|
| 16.1 | `ItemCatalogLoader` Model-&gt;Entries-Index | done |
| 16.2 | `InventoryItem` (NetworkSerializable) + `PlayerInventory` NetworkBehaviour | done |
| 16.3 | `/give <entry\|model> [count]` Console-Command | done |
| 16.4 | `/inventory` Console-Command + Registrierung | done |

## Out of Scope (-> Session B)

- `PlayerEquipment` NetworkBehaviour (Slots indexiert nach `EquipType`).
- Migration der `m_CurrentWeaponId` / `m_CurrentOffhandId` NetVars von
  `PlayerCombat` in `PlayerEquipment`, mit "Move-Between-Slots"-Semantik
  (Inventar &lt;-&gt; Equipment statt Duplikat).
- `PlayerStats` Aggregator (Base + Item-Bonus + Buffs).
- Port von `CombatFormulas.cpp` gegen die aggregierten Stats.
- Affixes, Gems, Sockets, Durability, Soulbound, Loot-Drop, Bank, Trade.

## File-Map

| Datei | Rolle |
|-------|-------|
| `Assets/Scripts/Runtime/Game/Items/ItemTemplate.cs` | DTO (existiert, unveraendert) |
| `Assets/Scripts/Runtime/Game/Items/ItemCatalogLoader.cs` | +Model-Index, +`GetEntriesByModel`, +`TryGetFirstEntryByModel` |
| `Assets/Scripts/Runtime/Game/Items/InventoryItem.cs` | **NEU** struct: `TemplateId`, `Count`, `INetworkSerializable` |
| `Assets/Scripts/Runtime/Game/Items/PlayerInventory.cs` | **NEU** NetworkBehaviour, 49 Slots, ServerRpcs |
| `Assets/Scripts/Runtime/Game/UI/Console/Commands/GiveCommand.cs` | **NEU** |
| `Assets/Scripts/Runtime/Game/UI/Console/Commands/InventoryCommand.cs` | **NEU** |
| `Assets/Scripts/Runtime/Game/UI/Console/ConsoleManager.cs` | +2 Command-Registrations |

## InventoryItem-Schema (v1)

```csharp
public struct InventoryItem : INetworkSerializable, IEquatable<InventoryItem>
{
    public int TemplateId; // 0 = leer
    public int Count;      // <=0 = leer
}
```

Bewusst minimal. Sockets/Enchant/Durability/Soulbound bleiben fuer Session B,
damit Diffs ueber die `NetworkList` schmal bleiben und die Pipeline erst gegen
`ItemTemplate` einrastet, bevor sie auf den vollen Source-`ItemId`-Struct geht.

## Replikations-Modell

- `PlayerInventory.m_Slots` ist eine `NetworkList<InventoryItem>`.
- Server initialisiert genau einmal `Capacity = 49` leere Eintraege in
  `OnNetworkSpawn` (Source-Parity: `PlayerDefines::Inventory::NumSlots = 49`).
- Mutationen ausschliesslich via Server (NetworkVariableWritePermission.Server).
- Owner-Clients triggern ueber `RequestGiveServerRpc` / `RequestRemoveSlotServerRpc`.
- Clients reagieren ueber `SlotChanged(int slotIndex, InventoryItem newValue)`
  -> HUD/Inventory-View kann gezielt eine Zelle redrawn.

## Server-Add-Logik

1. Wenn `IsStackable`: bestehende Stacks auffuellen bis `StackCount`.
2. Danach: erste leere Slots fuellen (`perSlot` = StackCount oder 1).
3. Rest-Menge wird **verworfen** (Inventar voll) — Ground-Drop kommt in
   Session B mit dem Loot-System.

## Console-Commands

```text
/give 6701              # gibt 1x Longbow (Template-Entry direkt)
/give longbow           # gibt 1x level-niedrigste Longbow (Model-Lookup)
/give longbow 3         # 3x
/inventory              # listet alle belegten Slots: [idx] #entry xN -- Name (model)
```

`GiveCommand` parst zuerst als Int (direkter Entry), faellt sonst auf
`ItemCatalogLoader.TryGetFirstEntryByModel` zurueck. Model-Index sortiert
pro Bucket nach `required_level asc`, Tie-Break `entry asc` — damit ist der
erste Treffer deterministisch der "schwaechste" Eintrag.

## Manueller Prefab-Schritt (PFLICHT vor Test)

`PlayerInventory` ist ein `NetworkBehaviour` und **muss vor dem Network-Spawn
auf dem Prefab liegen** — AddComponent nach Spawn wuerde nicht replizieren.

1. Unity Editor oeffnen.
2. `Assets/Prefabs/PlayerCharacter.prefab` oeffnen.
3. `Add Component` -> `Player Inventory` (Riftstorm.Game.Items).
4. Prefab speichern.

Ohne diesen Schritt loggen `/give` und `/inventory` *"local player not spawned
yet (or PlayerInventory fehlt am Prefab)"*.

## Source-Referenzen

- `source_server/Shared/PlayerDefines.h` -> `Inventory::NumSlots = 49`
- `source_server/Shared/ItemDefines.h`   -> `ItemFlags::Stackable`, `MaxEquipType`
- `source_server/Shared/ItemTemplate.h`  -> Feld-Set (1:1 in `ItemTemplate.cs`)

## Session B Preview

1. `PlayerEquipment : NetworkBehaviour` mit `NetworkVariable<int>` pro EquipType
   (Helm/Chest/Belt/Legs/Feet/Hands/Weapon/Shield/Ranged — Necklace/Ring optional).
2. `PlayerCombat.m_CurrentWeaponId/m_CurrentOffhandId` -> `PlayerEquipment`
   migrieren; `PlayerEquipmentVisuals` an die neue Quelle haengen.
3. `/equip <slotIndex>` / `/unequip <equipType>` mit Inventory-&lt;-&gt;Equipment-Move.
4. `PlayerStats`-Aggregator (Base + Equipment-StatType/StatValue-Summe).
5. Port `CombatFormulas.cpp` (CalculateDamage, Armor-Reduction, Crit) gegen
   aggregierte Stats; `PlayerCombat`-Hit-Resolution wechselt vom Stub auf real.
