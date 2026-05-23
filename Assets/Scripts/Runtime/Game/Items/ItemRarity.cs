namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Rarity-Stufen fuer gerollte Item-Instanzen. Steuert wie viele
    /// Affix-Slots (Prefix/Suffix) und Gem-Slots eine konkrete
    /// <see cref="ItemInstance"/> hat. Der Template-Base (bis zu 4 Stats aus
    /// <c>_templates.json</c>) ist davon unabhaengig — Common-Items haben
    /// also nicht zwingend 0 Stats, sondern nur 0 zusaetzliche Affix-Rolls.
    /// <para>
    /// Reihenfolge ist stabil; numerische Werte gehen ueber die
    /// <c>NetworkList&lt;ItemInstance&gt;</c>-Serialisierung als
    /// <see cref="byte"/> auf Draht, daher nicht umnummerieren.
    /// </para>
    /// </summary>
    public enum ItemRarity : byte
    {
        /// <summary>Weisses Item — nur Template-Base, 0 Affix-Slots, 0 Gem-Slots.</summary>
        Common = 0,
        /// <summary>Blaues Item — 1 Affix-Slot, 0 Gem-Slots.</summary>
        Magic = 1,
        /// <summary>Gelbes Item — 2 Affix-Slots, 0 Gem-Slots.</summary>
        Rare = 2,
        /// <summary>Lila Item — 2 Affix-Slots, 2 Gem-Slots.</summary>
        Epic = 3,
        /// <summary>Orange Item — 2 Affix-Slots, 4 Gem-Slots, Enchant-Multiplikator.</summary>
        Legendary = 4,
    }

    /// <summary>
    /// Slot-Allokation pro Rarity. Single source of truth — sowohl
    /// <see cref="ItemRoller"/> beim Erzeugen als auch
    /// <c>PlayerStats</c> beim Aggregieren konsultieren diese Tabelle, damit
    /// "wie viele Affixe / Gems hat ein Item" nirgendwo dupliziert wird.
    /// </summary>
    public static class RarityRules
    {
        /// <summary>Maximale Anzahl Affix-Slots ueber alle Rarities — fixe Array-Groesse in <see cref="ItemInstance"/>.</summary>
        public const int MaxAffixSlots = 2;

        /// <summary>Maximale Anzahl Gem-Sockets ueber alle Rarities — fixe Array-Groesse in <see cref="ItemInstance"/>.</summary>
        public const int MaxGemSlots = 4;

        /// <summary>Anzahl Affix-Slots, die fuer die angegebene Rarity tatsaechlich gerollt werden.</summary>
        public static int AffixCount(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Common => 0,
            ItemRarity.Magic => 1,
            ItemRarity.Rare => 2,
            ItemRarity.Epic => 2,
            ItemRarity.Legendary => 2,
            _ => 0,
        };

        /// <summary>Anzahl Gem-Sockets, die fuer die angegebene Rarity verfuegbar sind.</summary>
        public static int GemCount(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Common => 0,
            ItemRarity.Magic => 0,
            ItemRarity.Rare => 0,
            ItemRarity.Epic => 2,
            ItemRarity.Legendary => 4,
            _ => 0,
        };
    }
}
