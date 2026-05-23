using System;
using Unity.Netcode;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Server-autoritative Roll-Daten fuer ein konkretes Item in einem
    /// <see cref="EquipSlot"/>. Der Template-Base (bis zu 4 Stats aus
    /// <c>_templates.json</c>) wird ueber <see cref="TemplateId"/> weiterhin
    /// per <c>ItemCatalogLoader</c> aufgeloest — diese Struktur traegt nur
    /// die *zusaetzlichen* Rolls: Rarity, bis zu 2 Affixe (Prefix/Suffix) und
    /// bis zu 4 Gems. Jedes Affix referenziert per Id eine Definition im
    /// <c>AffixCatalogLoader</c> (siehe <c>_affixes.json</c>) und tragt
    /// einen 0..100 Score (D2-"quality") mit dem die Min/Max-Stat-Ranges
    /// linear interpoliert werden.
    /// <para>
    /// Wire-Size: 1 + 4 + 1 + 2*(2+1) + 4*2 + 1 = <b>21 Byte</b>. Liegt damit
    /// deutlich unter dem 64-Byte-Budget fuer
    /// <see cref="NetworkList{T}"/>-Elemente.
    /// </para>
    /// <para>
    /// <c>Empty</c> entspricht einem leeren Slot (TemplateId 0, Common,
    /// alle Affix-/Gem-Ids 0). PlayerEquipment haelt eine 13-grosse
    /// <c>NetworkList&lt;ItemInstance&gt;</c> parallel zur bestehenden
    /// <c>NetworkList&lt;int&gt;</c> mit dem TemplateId — die Instance-Liste
    /// ist der Single Source of Truth fuer Affix-Aggregation, der int-
    /// Liste-Spiegel bleibt fuer Backwards-Kompatibilitaet (CharacterHUD,
    /// PlayerCombat-Bridge, Inventory-Swap).
    /// </para>
    /// </summary>
    public struct ItemInstance : INetworkSerializable, IEquatable<ItemInstance>
    {
        /// <summary>
        /// Format-Version fuer Save/Load und ggf. spaeteres NetworkList-
        /// Migration-Handling. Wird mitserialisiert, damit aeltere Klienten
        /// neue Felder verweigern statt zu crashen.
        /// </summary>
        public const byte CurrentVersion = 1;

        /// <summary>Wire-Versionstag (immer <see cref="CurrentVersion"/> beim Erzeugen).</summary>
        public byte Version;

        /// <summary>Item-Template-Id (1:1 zum bestehenden <c>NetworkList&lt;int&gt; m_EquippedTemplates</c>). 0 = leerer Slot.</summary>
        public int TemplateId;

        /// <summary>Gerollte Rarity — steuert AffixCount/GemCount via <see cref="RarityRules"/>.</summary>
        public ItemRarity Rarity;

        /// <summary>Affix-Id Slot 1 (Prefix) — 0 wenn nicht belegt. Aufloesbar via <c>AffixCatalogLoader</c>.</summary>
        public ushort Affix1Id;

        /// <summary>Score 0..100 fuer Affix-Slot 1; interpoliert linear Min/Max der gerollten Stat-Werte.</summary>
        public byte Affix1Score;

        /// <summary>Affix-Id Slot 2 (Suffix) — 0 wenn nicht belegt.</summary>
        public ushort Affix2Id;

        /// <summary>Score 0..100 fuer Affix-Slot 2.</summary>
        public byte Affix2Score;

        /// <summary>Gem-Affix-Id Socket 0 — 0 = leerer Socket.</summary>
        public ushort Gem0Id;

        /// <summary>Gem-Affix-Id Socket 1.</summary>
        public ushort Gem1Id;

        /// <summary>Gem-Affix-Id Socket 2.</summary>
        public ushort Gem2Id;

        /// <summary>Gem-Affix-Id Socket 3.</summary>
        public ushort Gem3Id;

        /// <summary>
        /// Enchant-Level (0..N). Wirkt als globaler Bonus-Multiplikator auf
        /// Affix-Werte (z. B. +5% pro Level). Heute nur Daten — die
        /// Verrechnung passiert spaeter in <c>ItemRoller</c>/<c>PlayerStats</c>.
        /// </summary>
        public byte EnchantLevel;

        /// <summary>Leerer Slot — TemplateId 0.</summary>
        public static readonly ItemInstance Empty = default;

        /// <summary>True, wenn dieser Slot leer ist (TemplateId &lt;= 0).</summary>
        public bool IsEmpty => TemplateId <= 0;

        /// <summary>
        /// Liest den Affix-Eintrag (Id, Score) fuer Slot 0 oder 1. Andere
        /// Indizes liefern (0, 0). Score 0..100.
        /// </summary>
        public readonly (ushort id, byte score) GetAffix(int slotIndex) => slotIndex switch
        {
            0 => (Affix1Id, Affix1Score),
            1 => (Affix2Id, Affix2Score),
            _ => ((ushort)0, (byte)0),
        };

        /// <summary>Liest die Gem-Affix-Id fuer Socket 0..3. Andere Indizes liefern 0.</summary>
        public readonly ushort GetGem(int socketIndex) => socketIndex switch
        {
            0 => Gem0Id,
            1 => Gem1Id,
            2 => Gem2Id,
            3 => Gem3Id,
            _ => (ushort)0,
        };

        /// <summary>
        /// Initialisiert eine Common-Instanz nur mit TemplateId (keine
        /// Affixe, keine Gems). Convenience fuer Default-Loadout und
        /// nicht-rollende Pfade (Quest-Items, Reagents).
        /// </summary>
        public static ItemInstance FromTemplate(int templateId) => new()
        {
            Version = CurrentVersion,
            TemplateId = templateId,
            Rarity = ItemRarity.Common,
        };

        /// <inheritdoc/>
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref TemplateId);
            byte rarity = (byte)Rarity;
            serializer.SerializeValue(ref rarity);
            if (serializer.IsReader)
            {
                Rarity = (ItemRarity)rarity;
            }
            serializer.SerializeValue(ref Affix1Id);
            serializer.SerializeValue(ref Affix1Score);
            serializer.SerializeValue(ref Affix2Id);
            serializer.SerializeValue(ref Affix2Score);
            serializer.SerializeValue(ref Gem0Id);
            serializer.SerializeValue(ref Gem1Id);
            serializer.SerializeValue(ref Gem2Id);
            serializer.SerializeValue(ref Gem3Id);
            serializer.SerializeValue(ref EnchantLevel);
        }

        /// <inheritdoc/>
        public bool Equals(ItemInstance other) =>
            Version == other.Version
            && TemplateId == other.TemplateId
            && Rarity == other.Rarity
            && Affix1Id == other.Affix1Id
            && Affix1Score == other.Affix1Score
            && Affix2Id == other.Affix2Id
            && Affix2Score == other.Affix2Score
            && Gem0Id == other.Gem0Id
            && Gem1Id == other.Gem1Id
            && Gem2Id == other.Gem2Id
            && Gem3Id == other.Gem3Id
            && EnchantLevel == other.EnchantLevel;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ItemInstance other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // HashCode.Combine erlaubt max 8 Argumente — wir falten in zwei Runden.
            int h1 = HashCode.Combine(Version, TemplateId, (byte)Rarity, Affix1Id, Affix1Score, Affix2Id, Affix2Score, Gem0Id);
            int h2 = HashCode.Combine(Gem1Id, Gem2Id, Gem3Id, EnchantLevel);
            return HashCode.Combine(h1, h2);
        }

        /// <inheritdoc/>
        public static bool operator ==(ItemInstance left, ItemInstance right) => left.Equals(right);

        /// <inheritdoc/>
        public static bool operator !=(ItemInstance left, ItemInstance right) => !left.Equals(right);
    }
}
