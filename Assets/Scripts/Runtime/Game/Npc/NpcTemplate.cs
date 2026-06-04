using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    /// Die Spell-Slots sind im SQL-Schema flach (<c>spell_1_id</c> …
    /// <c>spell_{n}_cooldown</c>); sie werden nicht mehr explizit gemappt,
    /// sondern dynamisch ueber <see cref="SpellSlots"/> aus dem
    /// <c>[JsonExtensionData]</c>-Becken aufgebaut. Dadurch sind beliebig viele
    /// Slots moeglich, ohne das DTO zu aendern.
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

        /// <summary>Basis-Melee-Waffenschaden (vor Strength-/Skill-Bonus).</summary>
        [JsonProperty("weapon_value")] public int WeaponValue { get; set; }

        /// <summary>
        /// Basis-Ranged-Waffenschaden (vor Skill-Bonus). DB-Sentinel <c>&lt;=0</c> ⇒
        /// NPC besitzt keinen Ranged-Auto-Attack und nutzt ausschliesslich Melee.
        /// </summary>
        [JsonProperty("ranged_weapon_value")] public int RangedWeaponValue { get; set; }

        [JsonProperty("melee_skill")] public int MeleeSkill { get; set; }
        [JsonProperty("ranged_skill")] public int RangedSkill { get; set; }

        /// <summary>Millisekunden zwischen Melee-Swings (JSON-Rohwert, z. B. 2000).</summary>
        [JsonProperty("melee_speed")] public float MeleeSpeed { get; set; }

        /// <summary>Millisekunden zwischen Ranged-Shots (JSON-Rohwert).</summary>
        [JsonProperty("ranged_speed")] public float RangedSpeed { get; set; }

        /// <summary>
        /// Bitmaske der Crowd-Control-Mechaniken, gegen die dieser NPC immun ist.
        /// Ein Bit entspricht einem <see cref="Riftstorm.Game.Spells.Mechanic"/>-Wert
        /// ueber die Source-Konvention <c>bit = 1 &lt;&lt; ((int)mechanic - 1)</c>
        /// (SQL <c>mechanic_immune_mask</c>, z. B. 1535 = mehrere CC-Typen). Wird beim
        /// Aura-Apply geprueft: traegt der einzuspielende Aura-Effekt eine Mechanik,
        /// deren Bit gesetzt ist, wird der Effekt verworfen.
        /// </summary>
        [JsonProperty("mechanic_immune_mask")] public int MechanicImmuneMask { get; set; }

        /// <summary>True, wenn das Template einen Ranged-Auto-Attack besitzt.</summary>
        [JsonIgnore] public bool HasRangedWeapon => RangedWeaponValue > 0;

        /// <summary>
        /// Per-Template-Aggro-Reichweite in Meter. Sentinel <c>&lt;=0</c> ⇒ Source-Default
        /// (<c>DEFAULT_AGGRO_RANGE=5</c>, siehe <c>Npc.h</c>). Riftstorm-Erweiterung:
        /// Source haelt diesen Wert global als constexpr; hier per Template tunebar.
        /// </summary>
        [JsonProperty("aggro_range")] public float AggroRange { get; set; }

        /// <summary>
        /// Per-Template-Melee-Reichweite (Auto-Attack-Reach) in Meter. Sentinel <c>&lt;=0</c>
        /// ⇒ Source-Default (<c>DEFAULT_MELEE_RANGE=3</c>). Riftstorm-Erweiterung gegenueber
        /// Source, das diesen Wert ebenfalls global haelt.
        /// </summary>
        [JsonProperty("melee_range")] public float MeleeRange { get; set; }

        /// <summary>
        /// Per-Template-Leash-Override in Meter. Sentinel <c>&lt;=0</c> ⇒ Source-Default
        /// (<c>DEFAULT_LEASH_RANGE=50</c>, siehe <c>Npc.cpp</c> <c>initFromTemplate</c>).
        /// </summary>
        [JsonProperty("leash_range")] public float LeashRange { get; set; }

        /// <summary>
        /// Per-Template-Lauftempo in Unit/Sekunde. Sentinel <c>&lt;=0</c> ⇒ Spawner-Default
        /// (<c>NpcController.WalkSpeed</c>, abgeleitet aus Source <c>NPC_MOVE_SPEED=100 px/s</c>
        /// bei <c>PPU=64</c> ≈ 1.56 m/s). Riftstorm-Erweiterung: Source haelt diesen Wert
        /// global als <c>const float</c>; hier pro NPC tunebar (z. B. langsame Boesse,
        /// schnelle Wolves).
        /// </summary>
        [JsonProperty("move_speed")] public float MoveSpeed { get; set; }

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

        // ---- Spell-Slots (dynamisch, beliebig viele) -------------------

        /// <summary>
        /// Notfall-/Fallback-Spell (SQL <c>spell_primary</c>). Port von
        /// <c>Npc::m_primarySpellId</c> bzw. <c>NpcAI::selectSpellToCast</c>.
        /// Wird in zwei Faellen gezuendet:
        /// <list type="number">
        /// <item><b>Notfall:</b> NPC-HP <c>&lt;= 30 %</c> ⇒ hoechste Prioritaet
        /// vor allen regulaeren Slots.</item>
        /// <item><b>Fallback:</b> kein regulaerer Slot hat in diesem Tick
        /// durchgewuerfelt.</item>
        /// </list>
        /// <c>0</c> = kein Primary-Spell.
        /// </summary>
        [JsonProperty("spell_primary")] public int SpellPrimary { get; set; }

        /// <summary>
        /// Auffangbecken fuer alle nicht explizit gemappten JSON-Felder. Traegt
        /// u. a. die flachen <c>spell_{n}_id / _chance / _interval / _cooldown</c>
        /// Keys, aus denen <see cref="SpellSlots"/> die dynamische Slot-Liste
        /// aufbaut. Ermoeglicht <b>beliebig viele</b> Slots ohne DTO-Aenderung.
        /// </summary>
        [JsonExtensionData] private IDictionary<string, JToken> m_ExtraData = new Dictionary<string, JToken>();

        /// <summary>Lazy-Cache fuer <see cref="SpellSlots"/>.</summary>
        [JsonIgnore] private List<NpcSpellSlotData> m_SpellSlotsCache;

        /// <summary>
        /// Aus <see cref="m_ExtraData"/> aufgebaute, nach Slot-Index aufsteigend
        /// sortierte Liste aller gesetzten Spell-Slots (<c>id &gt; 0</c>).
        /// Unterstuetzt beliebig viele <c>spell_{n}_*</c> Eintraege.
        /// </summary>
        [JsonIgnore] public IReadOnlyList<NpcSpellSlotData> SpellSlots
            => m_SpellSlotsCache ??= BuildSpellSlots();

        // ---- Helper -----------------------------------------------------

        /// <summary>
        /// Hat dieses Template mindestens ein gesetztes <see cref="NpcFlagsMask"/>-Bit
        /// (also irgend eine Interaktion)?
        /// </summary>
        public bool IsInteractable => NpcFlags != NpcFlagsMask.None;

        /// <summary>
        /// True, wenn dieser NPC mindestens einen Kampf-Spell besitzt – sei es
        /// der <see cref="SpellPrimary"/> oder irgendein <see cref="SpellSlots"/>.
        /// </summary>
        [JsonIgnore] public bool HasCombatSpells
        {
            get
            {
                if (SpellPrimary > 0) { return true; }
                IReadOnlyList<NpcSpellSlotData> slots = SpellSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Id > 0) { return true; }
                }
                return false;
            }
        }

        /// <summary>
        /// True, wenn dieser NPC gar nichts kaempft (kein Spell-Slot, kein
        /// Primary-Spell, kein Melee-Skill, kein Waffenschaden). Wird vom
        /// <see cref="FlareNpcSpawner"/> genutzt um den <see cref="NpcController"/>
        /// bei Quest-/Vendor-NPCs nicht zu aktivieren.
        /// </summary>
        public bool IsPureTalker
            => IsInteractable
               && !HasCombatSpells
               && MeleeSkill <= 0 && RangedSkill <= 0 && WeaponValue <= 0;

        /// <summary>
        /// Baut die dynamische Slot-Liste aus den flachen
        /// <c>spell_{n}_*</c> JSON-Feldern. Robust gegen Luecken und beliebig
        /// viele Slots; das SQL-Schema liefert sie kontiguierlich, wir
        /// verlassen uns aber nicht darauf.
        /// </summary>
        private List<NpcSpellSlotData> BuildSpellSlots()
        {
            List<NpcSpellSlotData> slots = new();
            if (m_ExtraData == null || m_ExtraData.Count == 0)
            {
                return slots;
            }

            // Slot-Indizes aus allen spell_{n}_id-Keys einsammeln und sortieren.
            List<int> indices = new();
            foreach (string key in m_ExtraData.Keys)
            {
                if (TryParseSlotIndex(key, out int idx) && !indices.Contains(idx))
                {
                    indices.Add(idx);
                }
            }
            indices.Sort();

            foreach (int idx in indices)
            {
                int id = ReadExtraInt($"spell_{idx}_id");
                if (id <= 0)
                {
                    continue;
                }
                float chance = ReadExtraFloat($"spell_{idx}_chance");
                float interval = ReadExtraFloat($"spell_{idx}_interval");
                float cooldown = ReadExtraFloat($"spell_{idx}_cooldown");
                slots.Add(new NpcSpellSlotData(id, chance, interval, cooldown));
            }
            return slots;
        }

        /// <summary>
        /// Parst den Slot-Index aus einem <c>spell_{n}_id</c> Key. Liefert
        /// <c>false</c> fuer alle anderen Keys (inkl. <c>spell_primary</c>,
        /// <c>spell_{n}_chance</c>, <c>spell_{n}_targetType</c> …).
        /// </summary>
        private static bool TryParseSlotIndex(string key, out int index)
        {
            index = 0;
            const string prefix = "spell_";
            const string suffix = "_id";
            if (string.IsNullOrEmpty(key)
                || !key.StartsWith(prefix, StringComparison.Ordinal)
                || !key.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }
            int start = prefix.Length;
            int length = key.Length - suffix.Length - start;
            if (length <= 0)
            {
                return false;
            }
            string middle = key.Substring(start, length);
            return int.TryParse(middle, out index) && index > 0;
        }

        /// <summary>Liest einen Int aus <see cref="m_ExtraData"/> (<c>0</c>, wenn fehlend).</summary>
        private int ReadExtraInt(string key)
            => m_ExtraData != null && m_ExtraData.TryGetValue(key, out JToken token) && token != null
                ? token.Value<int>()
                : 0;

        /// <summary>Liest einen Float aus <see cref="m_ExtraData"/> (<c>0</c>, wenn fehlend).</summary>
        private float ReadExtraFloat(string key)
            => m_ExtraData != null && m_ExtraData.TryGetValue(key, out JToken token) && token != null
                ? token.Value<float>()
                : 0f;
    }

    /// <summary>
    /// Unveraenderliche Slot-Daten eines NPC-Spells (aus den flachen
    /// <c>spell_{n}_*</c> JSON-Feldern). Interval/Cooldown sind in
    /// <b>Millisekunden</b> (wie in der JSON); die Umrechnung in Sekunden
    /// passiert im <see cref="NpcController"/>.
    /// </summary>
    public readonly struct NpcSpellSlotData
    {
        /// <summary>Spell-Template-ID. <c>&lt;= 0</c> = leerer Slot.</summary>
        public int Id { get; }

        /// <summary>Wuerfel-Chance in Prozent (0..100).</summary>
        public float ChancePct { get; }

        /// <summary>Auswahl-Intervall in Millisekunden. <c>0</c> = kein Gate.</summary>
        public float IntervalMs { get; }

        /// <summary>Slot-Cooldown in Millisekunden. <c>0</c> = Spell-Default verwenden.</summary>
        public float CooldownMs { get; }

        /// <summary>Erzeugt einen unveraenderlichen Slot-Datensatz.</summary>
        public NpcSpellSlotData(int id, float chancePct, float intervalMs, float cooldownMs)
        {
            Id = id;
            ChancePct = chancePct;
            IntervalMs = intervalMs;
            CooldownMs = cooldownMs;
        }
    }
}
