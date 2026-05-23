using System;
using Unity.Netcode;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Ein einzelner Inventar-Slot-Eintrag. Wird in <c>NetworkList</c> auf
    /// <see cref="PlayerInventory"/> repliziert.
    /// <para>
    /// Phase 19: Der Slot haelt eine vollstaendige <see cref="ItemInstance"/>
    /// (TemplateId, Rarity, Affixe, Gems) plus den Stack-<see cref="Count"/>.
    /// Damit ueberleben Affix-Rolls jeden Pickup / Equip / Unequip — vorher
    /// trug der Slot nur die Template-Id und Re-Equippen aus dem Inventar
    /// resettete die Rarity auf Common.
    /// </para>
    /// <para>
    /// Wire-Size: <see cref="ItemInstance"/> (~21 B) + 4 B Count = ~25 B,
    /// weiterhin innerhalb des 64-B-Budgets fuer <c>NetworkList&lt;T&gt;</c>-
    /// Elemente.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Slot ist leer, wenn <see cref="IsEmpty"/> true ist (TemplateId &lt;= 0
    /// oder Count &lt;= 0). Damit kann der gesamte Inventory-Container als
    /// Fixed-Size-NetworkList mit 49 Slots laufen (siehe Source
    /// <c>PlayerDefines::Inventory::NumSlots</c>).
    /// </remarks>
    public struct InventoryItem : INetworkSerializable, IEquatable<InventoryItem>
    {
        /// <summary>Voller Roll-Datensatz fuer diesen Slot (Rarity + Affixe + Gems).</summary>
        public ItemInstance Instance;

        /// <summary>Stueckzahl im Slot. Bei nicht stackbaren Items immer 1.</summary>
        public int Count;

        /// <summary>Item-Template-Id im Slot. 0 = leer. Delegiert an <see cref="Instance"/>.</summary>
        public int TemplateId => Instance.TemplateId;

        /// <summary>True, wenn der Slot als leer gilt.</summary>
        public bool IsEmpty => Instance.TemplateId <= 0 || Count <= 0;

        /// <summary>
        /// Legacy-Konstruktor: erzeugt eine Common-Instanz nur aus der
        /// Template-Id. Wird vom Stack-/Consumable-Pfad weiterhin benutzt
        /// (Potions, Reagents — keine Affixe).
        /// </summary>
        public InventoryItem(int templateId, int count)
        {
            Instance = ItemInstance.FromTemplate(templateId);
            Count = count;
        }

        /// <summary>
        /// Phase-19-Konstruktor: speichert eine fertig gerollte Instanz
        /// (inklusive Rarity/Affixe) im Slot. Wird vom Equip-Swap und vom
        /// Unequip-Pfad genutzt, damit Affixe nicht verloren gehen.
        /// </summary>
        public InventoryItem(ItemInstance instance, int count)
        {
            Instance = instance;
            Count = count;
        }

        /// <summary>Leerer Slot (Instance.Empty, Count=0).</summary>
        public static InventoryItem Empty => new(ItemInstance.Empty, 0);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Instance.NetworkSerialize(serializer);
            serializer.SerializeValue(ref Count);
        }

        public bool Equals(InventoryItem other) => Instance.Equals(other.Instance) && Count == other.Count;
        public override bool Equals(object obj) => obj is InventoryItem o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Instance, Count);
        public static bool operator ==(InventoryItem a, InventoryItem b) => a.Equals(b);
        public static bool operator !=(InventoryItem a, InventoryItem b) => !a.Equals(b);

        public override string ToString() => IsEmpty ? "<empty>" : $"#{TemplateId} x{Count} [{Instance.Rarity}]";
    }
}
