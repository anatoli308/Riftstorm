using Newtonsoft.Json;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Statische Beschreibung eines Affix-Eintrags aus
    /// <c>StreamingAssets/items/_affixes.json</c>. Schema ist 1:1 Source-Parity
    /// (D2-aehnlicher Magic-Affix-Katalog): bis zu 4 Stat-Paare, Level-Range,
    /// und ein <see cref="NameSingleNoun"/>-Flag, das Prefix (0) und Suffix
    /// (1) trennt.
    /// <para>
    /// Der Source-Wert <c>stat_value*</c> ist ein <see cref="float"/>. Beim
    /// finalen Stat-Aggregat wird er ueber den 0..100 Score (
    /// <see cref="ItemInstance.GetAffix"/>) linear interpoliert:
    /// <c>final = round(stat_value * (0.5 + score/200))</c> — Score 0 ergibt
    /// 50 %, Score 100 ergibt 100 % des Source-Wertes. Die Verrechnung
    /// passiert in <c>PlayerStats</c>, damit dieser Datentyp pure Daten bleibt.
    /// </para>
    /// </summary>
    public sealed class ItemAffix
    {
        /// <summary>Stable Id (Source: <c>entry</c>). Im Bereich 1..65535 — passt in <see cref="ushort"/>.</summary>
        [JsonProperty("entry")]
        public int Entry { get; set; }

        /// <summary>Anzeigename, z. B. "Novice" oder "Fabled Vigor".</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Affix-Klasse: <c>0 = Prefix</c> (Adjektiv vorne), <c>1 = Suffix</c>
        /// (Nomen hinten, "of …"). Source-Konvention.
        /// </summary>
        [JsonProperty("name_single_noun")]
        public int NameSingleNoun { get; set; }

        /// <summary>Untergrenze des Item-Levels, ab dem dieser Affix gerollt werden darf.</summary>
        [JsonProperty("min_level")]
        public int MinLevel { get; set; }

        /// <summary>Obergrenze des Item-Levels.</summary>
        [JsonProperty("max_level")]
        public int MaxLevel { get; set; }

        /// <summary>Stat-Typ-Id Slot 1 (siehe <c>UnitDefines::Stat</c>).</summary>
        [JsonProperty("stat_type1")]
        public int StatType1 { get; set; }

        /// <summary>Source-Wert Slot 1 (Float — wird im Aggregat gerundet).</summary>
        [JsonProperty("stat_value1")]
        public float StatValue1 { get; set; }

        /// <summary>Stat-Typ-Id Slot 2.</summary>
        [JsonProperty("stat_type2")]
        public int StatType2 { get; set; }

        /// <summary>Source-Wert Slot 2.</summary>
        [JsonProperty("stat_value2")]
        public float StatValue2 { get; set; }

        /// <summary>Stat-Typ-Id Slot 3.</summary>
        [JsonProperty("stat_type3")]
        public int StatType3 { get; set; }

        /// <summary>Source-Wert Slot 3.</summary>
        [JsonProperty("stat_value3")]
        public float StatValue3 { get; set; }

        /// <summary>Stat-Typ-Id Slot 4.</summary>
        [JsonProperty("stat_type4")]
        public int StatType4 { get; set; }

        /// <summary>Source-Wert Slot 4.</summary>
        [JsonProperty("stat_value4")]
        public float StatValue4 { get; set; }

        /// <summary>True, wenn dieser Affix ein Prefix ist (Source: <c>name_single_noun == 0</c>).</summary>
        public bool IsPrefix => NameSingleNoun == 0;

        /// <summary>True, wenn dieser Affix ein Suffix ist (Source: <c>name_single_noun == 1</c>).</summary>
        public bool IsSuffix => NameSingleNoun == 1;

        /// <summary>Liest Stat-Typ/Source-Wert fuer Slot 0..3. Andere Indizes liefern (0, 0f).</summary>
        public (int statType, float statValue) GetStat(int slotIndex) => slotIndex switch
        {
            0 => (StatType1, StatValue1),
            1 => (StatType2, StatValue2),
            2 => (StatType3, StatValue3),
            3 => (StatType4, StatValue4),
            _ => (0, 0f),
        };
    }
}
