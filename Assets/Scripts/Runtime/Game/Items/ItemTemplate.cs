using Newtonsoft.Json;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// DTO fuer einen Eintrag aus <c>StreamingAssets/items/_templates.json</c>.
    /// Spiegelt 1:1 die Source-Tabelle <c>item_template</c> wider
    /// (siehe <c>source_server/Server/src/Database/GameData.cpp::loadItems</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Items sind in der Source eine eigene Tabelle, getrennt von <c>spell_template</c>.
    /// Consumables (Traenke, Scrolls, Manatraenke etc.) verlinken per <see cref="Spell1"/>
    /// auf einen Spell-Effekt aus <see cref="Riftstorm.Game.Spells.SpellTemplate"/>.
    /// Equipment (Helme/Brust/Waffen) hat <see cref="Spell1"/> = 0 und nutzt nur
    /// <see cref="StatType1"/>..<see cref="StatType4"/> als passive Stat-Boni.
    /// </para>
    /// <para>
    /// Felder die nicht im JSON liegen (z. B. <c>spell_2..spell_5</c>, <c>quest_offer</c>,
    /// <c>stat_type5..10</c>), fehlen in der migrierten Datenbasis und werden bewusst
    /// nicht abgebildet. Wenn sie spaeter gebraucht werden, muss der Migrator
    /// (<c>Tools/Scripts/migrate_game_db.py</c>) erweitert werden, nicht dieses DTO.
    /// </para>
    /// </remarks>
    public sealed class ItemTemplate
    {
        /// <summary>Primary Key. Stabile ID, ueber die Inventory/Equipment referenziert.</summary>
        [JsonProperty("entry")]
        public int Entry { get; set; }

        /// <summary>Sortierschluessel fuer UI-Listen (Bank, Vendor, Loot). Default = <see cref="Entry"/>.</summary>
        [JsonProperty("sort_entry")]
        public int SortEntry { get; set; }

        /// <summary>Anzeigename, EN/lokalisiert.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Beschreibung (Tooltip). Im JSON optional; Source liest hier interessanterweise einen int — wir nehmen string und akzeptieren beides als Text.</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Dateiname unter <c>Art/item_icons/</c> (z. B. <c>icon_item_potion_hp02_1.png</c>).</summary>
        [JsonProperty("icon")]
        public string Icon { get; set; }

        /// <summary>Sound beim Benutzen/Aufheben (z. B. <c>item_gen_potion.ogg</c>).</summary>
        [JsonProperty("icon_sound")]
        public string IconSound { get; set; }

        /// <summary>3D-Modell fuer World-Drop / Equipment-Slot ("0" = keins).</summary>
        [JsonProperty("model")]
        public string Model { get; set; }

        /// <summary>Mindest-Charakterlevel zum Benutzen/Anlegen.</summary>
        [JsonProperty("required_level")]
        public int RequiredLevel { get; set; }

        /// <summary>Klassen-Bitmaske; 0 = alle Klassen.</summary>
        [JsonProperty("required_class")]
        public int RequiredClass { get; set; }

        /// <summary>Item-Flags-Bitmaske; siehe <c>ItemDefines::ItemFlags</c> in der Source (Soulbound, QuestItem, Skillbook, Unique, Stackable, Consumable).</summary>
        [JsonProperty("flags")]
        public int Flags { get; set; }

        /// <summary>0 = Poor (grey), 1 = Common (white), 2 = Uncommon (green), 3 = Rare (blue), 4 = Epic (purple), 5 = Legendary (orange).</summary>
        [JsonProperty("quality")]
        public int Quality { get; set; }

        /// <summary>Item-Level fuer Affix-Scaling.</summary>
        [JsonProperty("item_level")]
        public int ItemLevel { get; set; }

        /// <summary>Slot-Typ; siehe <c>ItemDefines::EquipType</c> (1=Helm, 3=Chest, 9=Weapon, 10=Shield, ...). 0 = nicht anlegbar (Consumable / Quest-Item).</summary>
        [JsonProperty("equip_type")]
        public int EquipType { get; set; }

        /// <summary>Armor-Material (1=Cloth, 2=Leather, 3=Mail, 4=Plate). 0 = keine Ruestung.</summary>
        [JsonProperty("armor_type")]
        public int ArmorType { get; set; }

        /// <summary>Waffentyp; siehe <c>ItemDefines::WeaponType</c> (Sword/Axe/Bow/...). 0 = keine Waffe.</summary>
        [JsonProperty("weapon_type")]
        public int WeaponType { get; set; }

        /// <summary>Waffen-Material (Wood/Iron/Steel/...). 0 = keins.</summary>
        [JsonProperty("weapon_material")]
        public int WeaponMaterial { get; set; }

        /// <summary>Anzahl Sockel fuer Gems.</summary>
        [JsonProperty("num_sockets")]
        public int NumSockets { get; set; }

        /// <summary>Anfangs-Durability (% bzw. Hits). 0 = nicht abnutzbar.</summary>
        [JsonProperty("durability")]
        public int Durability { get; set; }

        /// <summary>Max Stack-Groesse (1 = nicht stapelbar).</summary>
        [JsonProperty("stack_count")]
        public int StackCount { get; set; }

        /// <summary>Vendor-Verkaufspreis in Kupfer. Source-DB hat hier teilweise
        /// Float-Werte (z. B. <c>5.32</c>), die migriert wurden — daher
        /// <see cref="float"/> statt <see cref="int"/>, damit der Parser nicht
        /// auf Legacy-Eintraegen kippt.</summary>
        [JsonProperty("sell_price")]
        public float SellPrice { get; set; }

        /// <summary>Multiplikator fuer Vendor-Kaufpreis (Source: <c>buy_cost_ratio</c>).</summary>
        [JsonProperty("buy_cost_ratio")]
        public int BuyCostRatio { get; set; }

        /// <summary>1 = vom Item-Generator prozedural erzeugt (Loot-Pool); 0 = handgesetzt (Quest, Crafting).</summary>
        [JsonProperty("generated")]
        public int Generated { get; set; }

        /// <summary>
        /// Optionale On-Use-/On-Equip-Spell-Referenz auf <c>spells/_templates.json</c>.
        /// 0 = kein Spell. Source hat bis zu 5 Slots (<c>spell_1..spell_5</c>), die
        /// migrierte Datenbasis enthaelt nur <c>spell_1</c>.
        /// </summary>
        [JsonProperty("spell_1")]
        public int Spell1 { get; set; }

        // ---- Passive Stats (bis zu 4 Paare in der migrierten Datenbasis) ----

        /// <summary>Stat-Typ-ID fuer Stat-Slot 1 (siehe <c>UnitDefines::Stat</c>).</summary>
        [JsonProperty("stat_type1")]
        public int StatType1 { get; set; }

        /// <summary>Stat-Wert fuer Slot 1.</summary>
        [JsonProperty("stat_value1")]
        public int StatValue1 { get; set; }

        /// <summary>Stat-Typ-ID fuer Slot 2.</summary>
        [JsonProperty("stat_type2")]
        public int StatType2 { get; set; }

        /// <summary>Stat-Wert fuer Slot 2.</summary>
        [JsonProperty("stat_value2")]
        public int StatValue2 { get; set; }

        /// <summary>Stat-Typ-ID fuer Slot 3.</summary>
        [JsonProperty("stat_type3")]
        public int StatType3 { get; set; }

        /// <summary>Stat-Wert fuer Slot 3.</summary>
        [JsonProperty("stat_value3")]
        public int StatValue3 { get; set; }

        /// <summary>Stat-Typ-ID fuer Slot 4.</summary>
        [JsonProperty("stat_type4")]
        public int StatType4 { get; set; }

        /// <summary>Stat-Wert fuer Slot 4.</summary>
        [JsonProperty("stat_value4")]
        public int StatValue4 { get; set; }

        // ---- Convenience -------------------------------------------------

        /// <summary>True wenn das Item beim Klick einen Spell-Effekt ausloest.</summary>
        [JsonIgnore]
        public bool HasUseEffect => Spell1 > 0;

        /// <summary>True wenn das Item in einem Equipment-Slot getragen werden kann.</summary>
        [JsonIgnore]
        public bool IsEquippable => EquipType > 0;

        /// <summary>True wenn das Item gestapelt werden kann.</summary>
        [JsonIgnore]
        public bool IsStackable => StackCount > 1;
    }
}
