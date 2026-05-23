using System;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Server-autoritatives Spieler-Inventar. 49 Slots, fix indexiert, leere
    /// Slots werden mit <see cref="InventoryItem.Empty"/> markiert — analog zu
    /// <c>PlayerDefines::Inventory::NumSlots</c> aus der Source.
    ///
    /// <para>
    /// Aenderungen passieren ausschliesslich server-seitig: Owner-Clients rufen
    /// <see cref="RequestGiveServerRpc"/> / <see cref="RequestRemoveSlotServerRpc"/>
    /// und sehen das Ergebnis erst, sobald die <see cref="NetworkList{InventoryItem}"/>
    /// via Netcode-Diff zurueckkommt. Banks/Trade kommen spaeter, daher hier
    /// noch keine Source-Parity zu <c>Bank</c>/<c>TradeWindow</c>.
    /// </para>
    /// <para>
    /// Komponenten-Lifecycle: muss als <c>NetworkBehaviour</c> auf dem
    /// PlayerCharacter-Prefab liegen (Drag&amp;Drop im Inspector), weil
    /// Netcode-Replikation nur fuer Komponenten greift, die vor dem
    /// Network-Spawn auf dem Prefab existieren.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : NetworkBehaviour
    {
        /// <summary>Slot-Anzahl (Source-Parity: <c>PlayerDefines::Inventory::NumSlots = 49</c>).</summary>
        public const int Capacity = 49;

        private readonly NetworkList<InventoryItem> m_Slots = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Feuert auf jedem Peer, sobald sich ein Slot aendert. Payload:
        /// <c>(slotIndex, newValue)</c>. HUDs/Inventory-Views koennen damit
        /// gezielt eine Zelle redrawn, ohne den ganzen Container zu re-rendern.
        /// </summary>
        public event Action<int, InventoryItem> SlotChanged;

        /// <summary>Gesamtanzahl der Slots (immer <see cref="Capacity"/>, sobald gespawned).</summary>
        public int Count => m_Slots.Count;

        /// <summary>Lesezugriff auf einen Slot. Out-of-range gibt <see cref="InventoryItem.Empty"/> zurueck.</summary>
        public InventoryItem GetSlot(int index)
        {
            if (index < 0 || index >= m_Slots.Count)
            {
                return InventoryItem.Empty;
            }
            return m_Slots[index];
        }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                // Server initialisiert die Slot-Liste einmalig mit Empty-Eintraegen,
                // damit Client-Diffs immer feste Indizes referenzieren koennen.
                if (m_Slots.Count == 0)
                {
                    for (int i = 0; i < Capacity; i++)
                    {
                        m_Slots.Add(InventoryItem.Empty);
                    }
                }
            }

            m_Slots.OnListChanged += HandleListChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Slots.OnListChanged -= HandleListChanged;
            base.OnNetworkDespawn();
        }

        private void HandleListChanged(NetworkListEvent<InventoryItem> evt)
        {
            // Wir interessieren uns nur fuer Slot-Mutationen — Add/Remove gibt es
            // im Steady-State nicht (Liste ist fix auf 49 Eintraege).
            if (evt.Type == NetworkListEvent<InventoryItem>.EventType.Value && SlotChanged != null)
            {
                SlotChanged(evt.Index, evt.Value);
            }
        }

        // -------------------------------------------------------------------------
        // Server-API (autoritativ)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Client-Einstieg fuer <c>/give</c>. Schickt eine ServerRpc, der
        /// Server validiert das Template und versucht das Item in die Slot-Liste
        /// zu legen. Antwort kommt implizit ueber die NetworkList-Aenderung.
        /// </summary>
        [ServerRpc]
        public void RequestGiveServerRpc(int templateId, int count, ServerRpcParams rpc = default)
        {
            if (count <= 0)
            {
                return;
            }
            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null)
            {
                Debug.LogWarning($"[PlayerInventory] /give: unbekanntes Template {templateId}");
                return;
            }
            TryAddItemServer(templateId, count, template);
        }

        /// <summary>
        /// Server-API: leert <paramref name="count"/> Stueck aus dem Slot. Wenn
        /// danach Count &lt;= 0, wird der Slot auf <see cref="InventoryItem.Empty"/>
        /// gesetzt. Owner ruft via <see cref="RequestRemoveSlotServerRpc"/>.
        /// </summary>
        public bool TryRemoveAtServer(int slotIndex, int count)
        {
            if (!IsServer || count <= 0 || slotIndex < 0 || slotIndex >= m_Slots.Count)
            {
                return false;
            }
            InventoryItem cur = m_Slots[slotIndex];
            if (cur.IsEmpty || cur.Count < count)
            {
                return false;
            }
            int newCount = cur.Count - count;
            // Phase 19: Instance erhalten, nicht aus TemplateId neu bauen
            // (sonst gehen Rarity/Affixe bei Stack-Decrement verloren).
            m_Slots[slotIndex] = newCount > 0 ? new InventoryItem(cur.Instance, newCount) : InventoryItem.Empty;
            return true;
        }

        [ServerRpc]
        public void RequestRemoveSlotServerRpc(int slotIndex, int count, ServerRpcParams rpc = default)
        {
            TryRemoveAtServer(slotIndex, count);
        }

        /// <summary>
        /// Server-seitiges Adden. Erst stacken (falls stackable und Template
        /// schon im Inventar), dann ersten leeren Slot suchen. Restmenge geht
        /// verloren — kein Ground-Drop in v1 (TODO Session B mit Loot-System).
        /// </summary>
        private bool TryAddItemServer(int templateId, int count, ItemTemplate template)
        {
            if (!IsServer || count <= 0)
            {
                return false;
            }

            int remaining = count;

            // 1) Stacken auf bestehende Slots, wenn stackable.
            if (template.IsStackable)
            {
                int stackMax = template.StackCount > 0 ? template.StackCount : int.MaxValue;
                for (int i = 0; i < m_Slots.Count && remaining > 0; i++)
                {
                    InventoryItem cur = m_Slots[i];
                    if (cur.TemplateId != templateId || cur.Count >= stackMax)
                    {
                        continue;
                    }
                    int free = stackMax - cur.Count;
                    int add = remaining < free ? remaining : free;
                    m_Slots[i] = new InventoryItem(templateId, cur.Count + add);
                    remaining -= add;
                }
            }

            // 2) Leere Slots fuellen.
            int perSlot = template.IsStackable
                ? (template.StackCount > 0 ? template.StackCount : remaining)
                : 1;
            for (int i = 0; i < m_Slots.Count && remaining > 0; i++)
            {
                if (!m_Slots[i].IsEmpty)
                {
                    continue;
                }
                int add = remaining < perSlot ? remaining : perSlot;
                m_Slots[i] = new InventoryItem(templateId, add);
                remaining -= add;
            }

            if (remaining > 0)
            {
                Debug.LogWarning($"[PlayerInventory] Inventar voll — {remaining}/{count} Stueck von Template {templateId} verworfen.");
            }
            return remaining < count;
        }

        // -------------------------------------------------------------------------
        // Server-API fuer Equipment-Move-Semantik (Phase 16B)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-API-Wrapper fuer Equipment-Unequip: legt ein Item ueber den
        /// regulaeren Add-Pfad (Stack-First, dann erster leerer Slot) zurueck
        /// ins Inventar. Templates werden ueber <see cref="ItemCatalogLoader"/>
        /// aufgeloest. Gibt <c>true</c> zurueck, sobald mindestens 1 Stueck
        /// untergebracht werden konnte.
        /// </summary>
        public bool TryAddItemServer(int templateId, int count)
        {
            if (!IsServer || count <= 0 || templateId <= 0)
            {
                return false;
            }
            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null)
            {
                Debug.LogWarning($"[PlayerInventory] TryAddItemServer: unbekanntes Template {templateId}");
                return false;
            }
            return TryAddItemServer(templateId, count, template);
        }

        /// <summary>
        /// Server-API: index des ersten leeren Slots oder -1. Wird vom
        /// Equipment-Pfad benoetigt, um vor einem Equip-Versuch zu pruefen ob
        /// ein bereits ausgeruestetes Item zurueck ins Inventar passt (Swap-Case).
        /// </summary>
        public int FindFirstEmptySlot()
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                if (m_Slots[i].IsEmpty)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Server-API (Phase 19): legt eine fertig gerollte
        /// <see cref="ItemInstance"/> in den ersten freien Slot. Macht KEIN
        /// Stack-Merge — Equipment-Items sind nicht stackable und jede
        /// Instanz hat ihren eigenen Affix-Roll. Gibt <c>false</c> zurueck,
        /// wenn das Inventar voll ist.
        /// <para>
        /// Hauptaufrufer: <c>PlayerEquipment.TryUnequipServer</c> — legt das
        /// gerade ausgeruestete Item samt Affixen zurueck ins Inventar.
        /// </para>
        /// </summary>
        public bool TryAddInstanceServer(ItemInstance instance)
        {
            if (!IsServer || instance.IsEmpty)
            {
                return false;
            }
            int slot = FindFirstEmptySlot();
            if (slot < 0)
            {
                return false;
            }
            m_Slots[slot] = new InventoryItem(instance, 1);
            return true;
        }

        /// <summary>
        /// Server-API: schreibt direkt in einen Slot (kein Stack-Merge, kein
        /// Capacity-Check). Wird vom Equipment-Pfad fuer den 1:1-Swap genutzt
        /// (Equip aus Slot N → vorher ausgeruestetes Item zurueck in Slot N).
        /// Akzeptiert <see cref="InventoryItem.Empty"/> zum Loeschen.
        /// </summary>
        public bool TrySetSlotServer(int slotIndex, InventoryItem item)
        {
            if (!IsServer || slotIndex < 0 || slotIndex >= m_Slots.Count)
            {
                return false;
            }
            m_Slots[slotIndex] = item;
            return true;
        }
    }
}
