using Newtonsoft.Json;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// DTO fuer einen Eintrag aus <c>StreamingAssets/npc/_templates.json</c>
    /// (migriert 1:1 aus <c>game.db.npc_template</c>). Alle Felder werden per
    /// Newtonsoft aus snake_case-Spaltennamen deserialisiert.
    /// </summary>
    /// <remarks>
    /// <b>Sentinel-Konvention:</b> Werte <c>-1</c> / <c>-1.0</c> bedeuten
    /// "DB-Default verwenden" (z. B. <see cref="LootGreenChance"/>) und werden
    /// NICHT als reguläre Daten interpretiert. Konsumenten muessen sie
    /// gezielt abfangen.
    /// <para>
    /// Die vier Spell-Slots sind im SQL-Schema flach (spell_1_id ... spell_4_cooldown);
    /// hier 1:1 abgebildet, um Schema-Drift zu vermeiden. Aggregierter Zugriff
    /// per <see cref="GetSpellSlot(int)"/>.
    /// </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NpcTemplate
    {
        // ---- Identitaet -------------------------------------------------

        /// <summary>Primaerschluessel (entry).</summary>
        [JsonProperty("entry")] public int Entry { get; set; }

        /// <summary>Anzeigename (DisplayName).</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>FK -&gt; <c>npc_models.id</c>. Lookup ueber den <see cref="NpcCatalogLoader"/>.</summary>
        [JsonProperty("model_id")] public int ModelId { get; set; }

        // ---- Level-Range ------------------------------------------------

        [JsonProperty("min_level")] public int MinLevel { get; set; }
        [JsonProperty("max_level")] public int MaxLevel { get; set; }

        // ---- Klassifikation --------------------------------------------

        /// <summary>Fraktion (FK auf Faction-Table; im Spawn-Flow z. B. fuer Aggro genutzt).</summary>
        [JsonProperty("faction")] public int Faction { get; set; }

        /// <summary>NPC-Typ (Beast/Humanoid/...). Siehe <see cref="RiftstormNpcType"/>.</summary>
        [JsonProperty("type")] public RiftstormNpcType Type { get; set; }

        /// <summary>Bitmaske der Interaktionsoptionen. Siehe <see cref="NpcFlagsMask"/>.</summary>
        [JsonProperty("npc_flags")] public NpcFlagsMask NpcFlags { get; set; }

        // ---- Base-Stats -------------------------------------------------

        [JsonProperty("strength")] public int Strength { get; set; }
        [JsonProperty("agility")] public int Agility { get; set; }
        [JsonProperty("intellect")] public int Intellect { get; set; }
        [JsonProperty("willpower")] public int Willpower { get; set; }
        [JsonProperty("courage")] public int Courage { get; set; }
        [JsonProperty("armor")] public int Armor { get; set; }
        [JsonProperty("health")] public int Health { get; set; }
        [JsonProperty("mana")] public int Mana { get; set; }

        /// <summary>
        /// SQL <c>bool_elite</c>. Source-Multiplier auf die Health-Formel ist ×3,
        /// wenn das Template <c>health &lt;= 0</c> als Sentinel meldet (siehe
        /// <c>Server/src/World/Npc.cpp</c> <c>calculateHealth</c>).
        /// </summary>
        [JsonProperty("bool_elite")] public int BoolElite { get; set; }

        /// <summary>
        /// SQL <c>bool_boss</c>. Source-Multiplier auf die Health-Formel ist ×10,
        /// wenn das Template <c>health &lt;= 0</c> als Sentinel meldet.
        /// </summary>
        [JsonProperty("bool_boss")] public int BoolBoss { get; set; }

        /// <summary>True, wenn <see cref="BoolElite"/> nicht-null.</summary>
        [JsonIgnore] public bool IsElite => BoolElite != 0;

        /// <summary>True, wenn <see cref="BoolBoss"/> nicht-null.</summary>
        [JsonIgnore] public bool IsBoss => BoolBoss != 0;

        // ---- Combat -----------------------------------------------------

        /// <summary>Basis-Waffenschaden (vor Strength-/Skill-Bonus).</summary>
        [JsonProperty("weapon_value")] public int WeaponValue { get; set; }

        [JsonProperty("melee_skill")] public int MeleeSkill { get; set; }
        [JsonProperty("ranged_skill")] public int RangedSkill { get; set; }

        /// <summary>Sekunden zwischen Melee-Swings.</summary>
        [JsonProperty("melee_speed")] public float MeleeSpeed { get; set; }

        /// <summary>Sekunden zwischen Ranged-Shots.</summary>
        [JsonProperty("ranged_speed")] public float RangedSpeed { get; set; }

        /// <summary>
        /// Per-Template-Leash-Override in Meter. Sentinel <c>&lt;=0</c> ⇒ Source-Default
        /// (<c>DEFAULT_LEASH_RANGE=50</c>, siehe <c>Npc.cpp</c> <c>initFromTemplate</c>).
        /// </summary>
        [JsonProperty("leash_range")] public float LeashRange { get; set; }

        // ---- Loot -------------------------------------------------------

        /// <summary>Drop-Chance gruen (Prozent). Sentinel <c>-1</c> = DB-Default.</summary>
        [JsonProperty("loot_green_chance")] public float LootGreenChance { get; set; }
        [JsonProperty("loot_blue_chance")] public float LootBlueChance { get; set; }
        [JsonProperty("loot_gold_chance")] public float LootGoldChance { get; set; }
        [JsonProperty("loot_purple_chance")] public float LootPurpleChance { get; set; }

        /// <summary>FK -&gt; <c>item_loot</c>; <c>0</c> = generischer Loot-Pool.</summary>
        [JsonProperty("custom_loot")] public int CustomLoot { get; set; }

        /// <summary>Override fuer Goldmenge; <c>-1</c> = Default-Berechnung aus Level.</summary>
        [JsonProperty("custom_gold_ratio")] public float CustomGoldRatio { get; set; }

        // ---- Spell-Slots (flach 1:1 aus SQL) ---------------------------

        [JsonProperty("spell_1_id")] public int Spell1Id { get; set; }
        [JsonProperty("spell_1_chance")] public float Spell1Chance { get; set; }
        [JsonProperty("spell_1_interval")] public float Spell1Interval { get; set; }
        [JsonProperty("spell_1_cooldown")] public float Spell1Cooldown { get; set; }

        [JsonProperty("spell_2_id")] public int Spell2Id { get; set; }
        [JsonProperty("spell_2_chance")] public float Spell2Chance { get; set; }
        [JsonProperty("spell_2_interval")] public float Spell2Interval { get; set; }
        [JsonProperty("spell_2_cooldown")] public float Spell2Cooldown { get; set; }

        [JsonProperty("spell_3_id")] public int Spell3Id { get; set; }
        [JsonProperty("spell_3_chance")] public float Spell3Chance { get; set; }
        [JsonProperty("spell_3_interval")] public float Spell3Interval { get; set; }
        [JsonProperty("spell_3_cooldown")] public float Spell3Cooldown { get; set; }

        [JsonProperty("spell_4_id")] public int Spell4Id { get; set; }
        [JsonProperty("spell_4_chance")] public float Spell4Chance { get; set; }
        [JsonProperty("spell_4_interval")] public float Spell4Interval { get; set; }
        [JsonProperty("spell_4_cooldown")] public float Spell4Cooldown { get; set; }

        // ---- Helper -----------------------------------------------------

        /// <summary>
        /// Hat dieses Template mindestens ein gesetztes <see cref="NpcFlagsMask"/>-Bit
        /// (also irgend eine Interaktion)?
        /// </summary>
        public bool IsInteractable => NpcFlags != NpcFlagsMask.None;

        /// <summary>
        /// True, wenn dieser NPC gar nichts kaempft (kein Spell-Slot, kein
        /// Melee-Skill, kein Waffenschaden). Wird vom <see cref="FlareNpcSpawner"/>
        /// genutzt um den <see cref="NpcController"/> bei Quest-/Vendor-NPCs
        /// nicht zu aktivieren.
        /// </summary>
        public bool IsPureTalker
            => IsInteractable
               && Spell1Id <= 0 && Spell2Id <= 0 && Spell3Id <= 0 && Spell4Id <= 0
               && MeleeSkill <= 0 && RangedSkill <= 0 && WeaponValue <= 0;

        /// <summary>
        /// Liest Slot <paramref name="oneBasedIndex"/> (1..4) als Tuple zurueck.
        /// Out-of-range liefert <c>(0, 0, 0, 0)</c>.
        /// </summary>
        public (int id, float chance, float interval, float cooldown) GetSpellSlot(int oneBasedIndex)
        {
            return oneBasedIndex switch
            {
                1 => (Spell1Id, Spell1Chance, Spell1Interval, Spell1Cooldown),
                2 => (Spell2Id, Spell2Chance, Spell2Interval, Spell2Cooldown),
                3 => (Spell3Id, Spell3Chance, Spell3Interval, Spell3Cooldown),
                4 => (Spell4Id, Spell4Chance, Spell4Interval, Spell4Cooldown),
                _ => (0, 0f, 0f, 0f),
            };
        }
    }
}
