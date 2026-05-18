using Newtonsoft.Json;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Wertstruktur fuer einen der bis zu drei Effekt-Slots eines
    /// <see cref="SpellTemplate"/>. Wird per <see cref="SpellTemplate.GetEffect(int)"/>
    /// gelesen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Die Slots sind in <c>spell_template</c> flach abgelegt
    /// (<c>effect1</c>, <c>effect1_data1</c>, ...) — gleiche Konvention wie
    /// die Spell-Slots in <see cref="Riftstorm.Game.Npc.NpcTemplate"/>. Diese
    /// Struktur ist nur ein Lese-Accessor; sie wird nicht direkt aus JSON
    /// deserialisiert.
    /// </para>
    /// <para>
    /// <b>Data-Felder:</b> Die drei <c>dataN</c>-Werte sind effekt-typ-spezifisch.
    /// Beispiel <see cref="SpellEffect.ApplyAura"/>: <c>data1</c> = <see cref="AuraType"/>,
    /// <c>data2</c> = Bitmaske ueber Stat-/Mechanic-IDs, <c>data3</c> =
    /// Stack-/Charges-Konfig. <c>data2</c> kann 64-Bit-Werte enthalten (Stat-Bitmask
    /// fuer Blessing of Defense: <c>137438953472</c> = <c>1L &lt;&lt; 37</c>), daher
    /// <see cref="long"/>.
    /// </para>
    /// </remarks>
    public readonly struct SpellTemplateEffect
    {
        /// <summary>1-basierter Slot-Index (1..3); <c>0</c> = leer/out-of-range.</summary>
        public int Index { get; }

        /// <summary>Effekt-Typ; <see cref="SpellEffect.None"/> wenn Slot leer.</summary>
        public SpellEffect Effect { get; }

        /// <summary>Ziel-Selektion fuer diesen Effekt.</summary>
        public SpellTargetType TargetType { get; }

        /// <summary>Erstes effekt-spezifisches Datenfeld (z. B. <see cref="AuraType"/>-int bei ApplyAura).</summary>
        public long Data1 { get; }

        /// <summary>Zweites Datenfeld (z. B. Stat-Bitmaske, kann 64 Bit nutzen).</summary>
        public long Data2 { get; }

        /// <summary>Drittes Datenfeld.</summary>
        public long Data3 { get; }

        /// <summary>True, wenn dieser Effekt ein Buff/positiver Effekt ist (DB-Spalte <c>effectN_positive</c>).</summary>
        public bool Positive { get; }

        /// <summary>AoE-Radius in Source-Pixeln. <c>0</c> = Single-Target.</summary>
        public int Radius { get; }

        /// <summary>
        /// FLARE-Formel zur Skalierung des Effekt-Werts (z. B. <c>"value+splvl"</c>).
        /// <c>null</c> wenn nicht gesetzt.
        /// </summary>
        public string ScaleFormula { get; }

        /// <summary>True, wenn dieser Slot tatsaechlich einen Effekt referenziert.</summary>
        public bool IsActive => Effect != SpellEffect.None;

        internal SpellTemplateEffect(
            int index,
            SpellEffect effect,
            SpellTargetType targetType,
            long data1,
            long data2,
            long data3,
            bool positive,
            int radius,
            string scaleFormula)
        {
            Index = index;
            Effect = effect;
            TargetType = targetType;
            Data1 = data1;
            Data2 = data2;
            Data3 = data3;
            Positive = positive;
            Radius = radius;
            ScaleFormula = scaleFormula;
        }
    }

    /// <summary>
    /// DTO fuer einen Eintrag aus <c>StreamingAssets/spells/_templates.json</c>
    /// (migriert 1:1 aus <c>game.db.spell_template</c>). Alle Felder werden per
    /// Newtonsoft aus den DB-Spaltennamen deserialisiert. Mischt snake_case und
    /// camelCase, weil der Source-DB-Export beide Schreibweisen nutzt (z. B.
    /// <c>cast_school</c> vs. <c>effect1_targetType</c>, <c>maxTargets</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sentinel-Konvention:</b> wie in <see cref="Riftstorm.Game.Npc.NpcTemplate"/>;
    /// numerische Felder, die in der DB nicht gesetzt sind, kommen als <c>0</c>
    /// an. <c>Cooldown</c>, <c>Range</c> usw. sind nur dann zu interpretieren,
    /// wenn <c>&gt; 0</c>.
    /// </para>
    /// <para>
    /// <b>Effekt-Slots:</b> bis zu drei Slots (Source <c>NumEffectIdx=3</c>), hier
    /// 1:1 flach abgebildet. Aggregierter Zugriff per
    /// <see cref="GetEffect(int)"/>; Slot ist aktiv, wenn
    /// <c>EffectN != SpellEffect.None</c>.
    /// </para>
    /// <para>
    /// <b>Einheiten:</b> Zeiten in Millisekunden, Reichweiten in Source-Pixeln
    /// (Konvertierung in Unity-Meter erfolgt im SpellCaster, nicht im DTO —
    /// gleiches Pattern wie <c>NpcController.WalkSpeed</c>).
    /// </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellTemplate
    {
        // ---- Identitaet -------------------------------------------------

        /// <summary>Primaerschluessel (entry).</summary>
        [JsonProperty("entry")] public int Entry { get; set; }

        /// <summary>Anzeigename.</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Icon-Pfad (relativ unter Spell-Icons-Ordner). Kann <c>null</c> sein.</summary>
        [JsonProperty("icon")] public string Icon { get; set; }

        /// <summary>Tooltip-Text (mit FLARE-Platzhaltern wie <c>$DUR</c>, <c>$E1min</c>).</summary>
        [JsonProperty("description")] public string Description { get; set; }

        /// <summary>Aura-Tooltip-Text (bei Buffs/Debuffs ueber dem Buff-Icon angezeigt).</summary>
        [JsonProperty("aura_description")] public string AuraDescription { get; set; }

        // ---- Cast-Parameter --------------------------------------------

        /// <summary>Cast-Zeit in Millisekunden. <c>0</c> = Instant-Cast.</summary>
        [JsonProperty("cast_time")] public int CastTime { get; set; }

        /// <summary>Cooldown in Millisekunden. <c>0</c> = kein Cooldown.</summary>
        [JsonProperty("cooldown")] public int Cooldown { get; set; }

        /// <summary>
        /// Geteilte Cooldown-Kategorie (mehrere Spells teilen sich denselben Cooldown
        /// ueber diesen Bucket). <c>0</c> = isolierter Cooldown.
        /// </summary>
        [JsonProperty("cooldown_category")] public int CooldownCategory { get; set; }

        /// <summary>Schule, mit der der Cast magisch resolved wird (Resistenz-Check etc.).</summary>
        [JsonProperty("cast_school")] public SpellSchool CastSchool { get; set; }

        /// <summary>Schule fuer den Hit-Roll (Miss/Resist). Oft identisch mit <see cref="CastSchool"/>.</summary>
        [JsonProperty("magic_roll_school")] public SpellSchool MagicRollSchool { get; set; }

        /// <summary>Bitmaske der Ereignisse, die einen laufenden Cast unterbrechen.</summary>
        [JsonProperty("cast_interrupt_flags")] public int CastInterruptFlags { get; set; }

        /// <summary>Bitmaske der Ereignisse, die eine bereits applizierte Aura entfernen.</summary>
        [JsonProperty("aura_interrupt_flags")] public int AuraInterruptFlags { get; set; }

        /// <summary>
        /// Bitmaske der CC-Mechaniken, die diesen Cast verhindern (z. B. Stun, Silence).
        /// Korrespondiert zu <c>SpellDefines::Mechanics</c>.
        /// </summary>
        [JsonProperty("prevention_type")] public int PreventionType { get; set; }

        /// <summary>
        /// Projektil-Geschwindigkeit in Source-Pixeln/Sekunde. <c>0</c> = Hitscan/Instant.
        /// </summary>
        [JsonProperty("speed")] public float Speed { get; set; }

        /// <summary>Maximalreichweite in Source-Pixeln. <c>0</c> = Self/uneingeschraenkt.</summary>
        [JsonProperty("range")] public float Range { get; set; }

        /// <summary>Mindestreichweite in Source-Pixeln (z. B. Charge). <c>0</c> = keine.</summary>
        [JsonProperty("range_min")] public float RangeMin { get; set; }

        /// <summary>Maximal getroffene Ziele bei AoE. <c>0</c> = unbegrenzt / nicht relevant.</summary>
        [JsonProperty("maxTargets")] public int MaxTargets { get; set; }

        /// <summary>Bitmaske aus <see cref="SpellAttributes"/>.</summary>
        [JsonProperty("attributes")] public SpellAttributes Attributes { get; set; }

        // ---- Kosten -----------------------------------------------------

        /// <summary>
        /// FLARE-Formel fuer Mana-Kosten (z. B. <c>"1+((clvl*10)/20)"</c>). Auswertung
        /// im SpellCaster zur Cast-Zeit gegen <c>clvl</c>/<c>splvl</c>.
        /// </summary>
        [JsonProperty("mana_formula")] public string ManaFormula { get; set; }

        /// <summary>Mana-Kosten als Prozent des Mana-Maximums.</summary>
        [JsonProperty("mana_pct")] public float ManaPct { get; set; }

        /// <summary>Health-Kosten (Flat). Z. B. Life-Tap.</summary>
        [JsonProperty("health_cost")] public int HealthCost { get; set; }

        /// <summary>Health-Kosten als Prozent der Max-HP.</summary>
        [JsonProperty("health_pct_cost")] public float HealthPctCost { get; set; }

        // ---- Dauer / Periodic ------------------------------------------

        /// <summary>Aura-Dauer in Millisekunden. <c>0</c> = nicht-persistierend.</summary>
        [JsonProperty("duration")] public int Duration { get; set; }

        /// <summary>FLARE-Formel zur Skalierung der Dauer (z. B. <c>"value+(splvl*1000)"</c>).</summary>
        [JsonProperty("duration_formula")] public string DurationFormula { get; set; }

        /// <summary>Tick-Intervall fuer periodische Effekte (DoT/HoT) in Millisekunden.</summary>
        [JsonProperty("interval")] public int Interval { get; set; }

        /// <summary>Max-Stacks fuer stackbare Auren. <c>0</c> oder <c>1</c> = nicht-stackbar.</summary>
        [JsonProperty("stack_amount")] public int StackAmount { get; set; }

        /// <summary>Dispel-Kategorie (welche Dispel-Typen entfernen diese Aura).</summary>
        [JsonProperty("dispel")] public DispelType Dispel { get; set; }

        // ---- Requirements ----------------------------------------------

        /// <summary>FK auf benoetigte Caster-Aura (Voraussetzung fuer den Cast).</summary>
        [JsonProperty("req_caster_aura")] public int RequiredCasterAura { get; set; }

        /// <summary>FK auf benoetigte Caster-Mechanic.</summary>
        [JsonProperty("req_caster_mechanic")] public int RequiredCasterMechanic { get; set; }

        /// <summary>FK auf benoetigte Target-Aura (z. B. Finisher, der nur auf Bleeding-Targets wirkt).</summary>
        [JsonProperty("req_tar_aura")] public int RequiredTargetAura { get; set; }

        /// <summary>FK auf benoetigte Target-Mechanic.</summary>
        [JsonProperty("req_tar_mechanic")] public int RequiredTargetMechanic { get; set; }

        /// <summary>Bitmaske erforderlicher Waffen-/Equip-Slots.</summary>
        [JsonProperty("required_equipment")] public long RequiredEquipment { get; set; }

        /// <summary>FK auf Spell, der diesen automatisch triggert beim Erlernen (Talent-Chain).</summary>
        [JsonProperty("activated_by_in")] public int ActivatedByIn { get; set; }

        /// <summary>FK auf Spell, der diesen Spell beim Entfernen aktiviert.</summary>
        [JsonProperty("activated_by_out")] public int ActivatedByOut { get; set; }

        // ---- Talent-Tree -----------------------------------------------

        /// <summary>Talent-Tab-Index (1..N), falls dieser Spell ueber einen Skill-Tree gelernt wird.</summary>
        [JsonProperty("abilities_tab")] public int AbilitiesTab { get; set; }

        /// <summary>0/1: ob der Spell ueber Spell-Level (<c>splvl</c>) gesteigert werden kann.</summary>
        [JsonProperty("can_level_up")] public int CanLevelUp { get; set; }

        /// <summary>True wenn <see cref="CanLevelUp"/> nicht-null.</summary>
        [JsonIgnore] public bool IsLevelUpAble => CanLevelUp != 0;

        // ---- Stat-Scaling ----------------------------------------------

        /// <summary>FLARE-Formel fuer primaeres Stat-Scaling (caster-stat bonus).</summary>
        [JsonProperty("stat_scale_1")] public string StatScale1 { get; set; }

        /// <summary>FLARE-Formel fuer sekundaeres Stat-Scaling.</summary>
        [JsonProperty("stat_scale_2")] public string StatScale2 { get; set; }

        // ---- Effekt-Slots (flach 1:1 aus SQL, bis zu 3 Slots) ----------

        [JsonProperty("effect1")] public SpellEffect Effect1 { get; set; }
        [JsonProperty("effect1_targetType")] public SpellTargetType Effect1TargetType { get; set; }
        [JsonProperty("effect1_data1")] public long Effect1Data1 { get; set; }
        [JsonProperty("effect1_data2")] public long Effect1Data2 { get; set; }
        [JsonProperty("effect1_data3")] public long Effect1Data3 { get; set; }
        [JsonProperty("effect1_positive")] public int Effect1Positive { get; set; }
        [JsonProperty("effect1_radius")] public int Effect1Radius { get; set; }
        [JsonProperty("effect1_scale_formula")] public string Effect1ScaleFormula { get; set; }

        [JsonProperty("effect2")] public SpellEffect Effect2 { get; set; }
        [JsonProperty("effect2_targetType")] public SpellTargetType Effect2TargetType { get; set; }
        [JsonProperty("effect2_data1")] public long Effect2Data1 { get; set; }
        [JsonProperty("effect2_data2")] public long Effect2Data2 { get; set; }
        [JsonProperty("effect2_data3")] public long Effect2Data3 { get; set; }
        [JsonProperty("effect2_positive")] public int Effect2Positive { get; set; }
        [JsonProperty("effect2_radius")] public int Effect2Radius { get; set; }
        [JsonProperty("effect2_scale_formula")] public string Effect2ScaleFormula { get; set; }

        [JsonProperty("effect3")] public SpellEffect Effect3 { get; set; }
        [JsonProperty("effect3_targetType")] public SpellTargetType Effect3TargetType { get; set; }
        [JsonProperty("effect3_data1")] public long Effect3Data1 { get; set; }
        [JsonProperty("effect3_data2")] public long Effect3Data2 { get; set; }
        [JsonProperty("effect3_data3")] public long Effect3Data3 { get; set; }
        [JsonProperty("effect3_positive")] public int Effect3Positive { get; set; }
        [JsonProperty("effect3_radius")] public int Effect3Radius { get; set; }
        [JsonProperty("effect3_scale_formula")] public string Effect3ScaleFormula { get; set; }

        // ---- Helper -----------------------------------------------------

        /// <summary>True wenn ueberhaupt kein Slot gesetzt ist (leerer DB-Stub).</summary>
        public bool HasAnyEffect
            => Effect1 != SpellEffect.None
               || Effect2 != SpellEffect.None
               || Effect3 != SpellEffect.None;

        /// <summary>True wenn dieser Spell mindestens einen Schaden verursachenden Effekt hat.</summary>
        public bool IsOffensive
            => IsDamageEffect(Effect1) || IsDamageEffect(Effect2) || IsDamageEffect(Effect3);

        private static bool IsDamageEffect(SpellEffect e)
            => e == SpellEffect.SchoolDamage
               || e == SpellEffect.WeaponDamage
               || e == SpellEffect.HealthDrain
               || e == SpellEffect.ManaBurn
               || e == SpellEffect.MeleeAtk
               || e == SpellEffect.RangedAtk;

        /// <summary>
        /// Liest Slot <paramref name="oneBasedIndex"/> (1..3) als value-type
        /// zurueck. Out-of-range oder leerer Slot liefert eine inaktive
        /// Struktur (<see cref="SpellTemplateEffect.IsActive"/> = false).
        /// </summary>
        public SpellTemplateEffect GetEffect(int oneBasedIndex)
        {
            return oneBasedIndex switch
            {
                1 => new SpellTemplateEffect(
                    1, Effect1, Effect1TargetType,
                    Effect1Data1, Effect1Data2, Effect1Data3,
                    Effect1Positive != 0, Effect1Radius, Effect1ScaleFormula),
                2 => new SpellTemplateEffect(
                    2, Effect2, Effect2TargetType,
                    Effect2Data1, Effect2Data2, Effect2Data3,
                    Effect2Positive != 0, Effect2Radius, Effect2ScaleFormula),
                3 => new SpellTemplateEffect(
                    3, Effect3, Effect3TargetType,
                    Effect3Data1, Effect3Data2, Effect3Data3,
                    Effect3Positive != 0, Effect3Radius, Effect3ScaleFormula),
                _ => default,
            };
        }
    }
}
