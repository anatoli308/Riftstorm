using System;
using Unity.Netcode;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Ein einzelner Inventar-Slot-Eintrag. Wird in <c>NetworkList</c> auf
    /// <see cref="PlayerInventory"/> repliziert. Bewusst minimal in v1 — keine
    /// Affixes/Gems/Durability, damit Replikation und Diffs schmal bleiben.
    /// Erweiterungen (Sockets, Enchant, Soulbound-Flag) folgen, wenn die
    /// Equip-/Loot-Pipeline gegen <c>ItemTemplate</c> hinausgewachsen ist.
    /// </summary>
    /// <remarks>
    /// Slot ist leer, wenn <see cref="IsEmpty"/> true ist (TemplateId &lt;= 0
    /// oder Count &lt;= 0). Damit kann der gesamte Inventory-Container als
    /// Fixed-Size-NetworkList mit 49 Slots laufen (siehe Source
    /// <c>PlayerDefines::Inventory::NumSlots</c>).
    /// </remarks>
    public struct InventoryItem : INetworkSerializable, IEquatable<InventoryItem>
    {
        /// <summary>Eintrag in <c>StreamingAssets/items/_templates.json</c>. 0 = leer.</summary>
        public int TemplateId;

        /// <summary>Stueckzahl im Slot. Bei nicht stackbaren Items immer 1.</summary>
        public int Count;

        /// <summary>True, wenn der Slot als leer gilt.</summary>
        public bool IsEmpty => TemplateId <= 0 || Count <= 0;

        public InventoryItem(int templateId, int count)
        {
            TemplateId = templateId;
            Count = count;
        }

        /// <summary>Leerer Slot (TemplateId=0, Count=0).</summary>
        public static InventoryItem Empty => new(0, 0);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TemplateId);
            serializer.SerializeValue(ref Count);
        }

        public bool Equals(InventoryItem other) => TemplateId == other.TemplateId && Count == other.Count;
        public override bool Equals(object obj) => obj is InventoryItem o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(TemplateId, Count);
        public static bool operator ==(InventoryItem a, InventoryItem b) => a.Equals(b);
        public static bool operator !=(InventoryItem a, InventoryItem b) => !a.Equals(b);

        public override string ToString() => IsEmpty ? "<empty>" : $"#{TemplateId} x{Count}";
    }
}
