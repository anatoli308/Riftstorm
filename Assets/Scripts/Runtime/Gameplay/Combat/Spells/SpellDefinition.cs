using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Datengetriebene Spell-Definition. Quelle: <c>StreamingAssets/spells/spells.json</c>.
    /// Modder können neue Spells ergänzen, ohne den Code anzufassen.
    /// </summary>
    /// <remarks>
    /// Felder spiegeln <c>source_server/Shared/GameData.h::SpellTemplate</c>.
    /// Cast-Time / Cooldown / Duration / Interval stehen in <b>Millisekunden</b>
    /// (Server arbeitet in ms-Ticks). UI rechnet bei Anzeige in Sekunden um.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellDefinition
    {
        /// <summary>Maximale Anzahl Effect-Slots pro Spell (1:1 zum C++-Source).</summary>
        public const int MaxEffects = 3;

        /// <summary>Eindeutiger String-Key (z. B. <c>"fireball"</c>, <c>"heal_light"</c>).</summary>
        [JsonProperty("id")] public string Id;

        /// <summary>Anzeigename für UI (lokalisierbar).</summary>
        [JsonProperty("name")] public string Name = string.Empty;

        /// <summary>Icon-Key. Pfad relativ unter <c>Art/icons/</c>.</summary>
        [JsonProperty("icon")] public string Icon = string.Empty;

        /// <summary>Beschreibungstext (Tooltip).</summary>
        [JsonProperty("description")] public string Description = string.Empty;

        /// <summary>School des Casts (für Resists, Aura-Visuals).</summary>
        [JsonProperty("school")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellSchool School = SpellSchool.None;

        /// <summary>Cast-Time in Millisekunden. 0 = Instant.</summary>
        [JsonProperty("cast_time_ms")] public int CastTimeMs;

        /// <summary>Cooldown in Millisekunden. 0 = nur GCD-limitiert.</summary>
        [JsonProperty("cooldown_ms")] public int CooldownMs;

        /// <summary>
        /// Cooldown-Kategorie (geteilter Cooldown, z. B. Potions). Leer = eigener Slot.
        /// </summary>
        [JsonProperty("cooldown_category")] public string CooldownCategory = string.Empty;

        /// <summary>Global-Cooldown-Override in Millisekunden. 0 = projektweiter Default.</summary>
        [JsonProperty("gcd_ms")] public int GcdMs;

        /// <summary>
        /// Maximal-Reichweite in Welt-Units. 0 = Self-Cast / kein Range-Check.
        /// </summary>
        [JsonProperty("range")] public float Range;

        /// <summary>Mindest-Reichweite (für Ranged-Spells mit Dead-Zone).</summary>
        [JsonProperty("range_min")] public float RangeMin;

        /// <summary>
        /// Effekt-Dauer in Millisekunden für Channeled-Spells. Für Auren-Dauer
        /// siehe <see cref="AuraDefinition.DurationMs"/>.
        /// </summary>
        [JsonProperty("duration_ms")] public int DurationMs;

        /// <summary>
        /// Tick-Interval in Millisekunden für Channeled-Spells. 0 = kein Tick.
        /// </summary>
        [JsonProperty("interval_ms")] public int IntervalMs;

        /// <summary>
        /// Projektil-Geschwindigkeit in Welt-Units / Sekunde. 0 = Instant-Hit
        /// (Hitscan / Self / AreaSrc).
        /// </summary>
        [JsonProperty("projectile_speed")] public float ProjectileSpeed;

        /// <summary>Maximale Trefferzahl bei AoE. 0 = unbegrenzt.</summary>
        [JsonProperty("max_targets")] public int MaxTargets;

        /// <summary>
        /// Auf welche Dispel-Kategorie die per <see cref="SpellEffectType.Dispel"/>
        /// entfernten Auren matchen müssen.
        /// </summary>
        [JsonProperty("dispel_type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellDispelType DispelType = SpellDispelType.None;

        /// <summary>Spell-Flags (Bitmask).</summary>
        [JsonProperty("attributes")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellAttributes Attributes = SpellAttributes.None;

        /// <summary>
        /// Welche Events laufende <i>Auren</i> dieses Spells unterbrechen
        /// (z. B. Bewegung → Stealth bricht).
        /// </summary>
        [JsonProperty("aura_interrupt_flags")]
        public int AuraInterruptFlags;

        /// <summary>
        /// Welche Events einen laufenden <i>Cast</i> dieses Spells unterbrechen
        /// (z. B. Schaden → Cast-Push-Back / Abbruch).
        /// </summary>
        [JsonProperty("cast_interrupt_flags")]
        public int CastInterruptFlags;

        /// <summary>Mana-Kosten (fester Betrag).</summary>
        [JsonProperty("mana_cost")] public int ManaCost;

        /// <summary>Mana-Kosten in % des Max-Mana-Pools.</summary>
        [JsonProperty("mana_cost_pct")] public int ManaCostPct;

        /// <summary>HP-Kosten (für Lebenskosten-Spells).</summary>
        [JsonProperty("health_cost")] public int HealthCost;

        /// <summary>HP-Kosten in % des Max-HP-Pools.</summary>
        [JsonProperty("health_cost_pct")] public int HealthCostPct;

        /// <summary>
        /// Required-Equipment-Tag (z. B. <c>"staff"</c>, <c>"bow"</c>). Leer = egal.
        /// </summary>
        [JsonProperty("required_equipment")] public string RequiredEquipment = string.Empty;

        /// <summary>Caster muss diese Mechanik tragen (z. B. Stealth-only Follow-Up).</summary>
        [JsonProperty("req_caster_mechanic")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellMechanic ReqCasterMechanic = SpellMechanic.None;

        /// <summary>Ziel muss diese Mechanik tragen (z. B. Finisher-only-auf-Stunned).</summary>
        [JsonProperty("req_target_mechanic")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellMechanic ReqTargetMechanic = SpellMechanic.None;

        /// <summary>Primär-Stat-Skalierungs-Id (für Formel-Auswertung).</summary>
        [JsonProperty("stat_scale1")] public int StatScale1;

        /// <summary>Sekundär-Stat-Skalierungs-Id.</summary>
        [JsonProperty("stat_scale2")] public int StatScale2;

        /// <summary>
        /// Bis zu <see cref="MaxEffects"/> Effekt-Slots. Reihenfolge ist die
        /// Anwendungs-Reihenfolge auf dem Server.
        /// </summary>
        [JsonProperty("effects")]
        public List<SpellEffectDefinition> Effects = new();

        /// <summary>True, wenn der Spell als Projektil reist (Hitbox spawnt).</summary>
        [JsonIgnore]
        public bool IsProjectile => ProjectileSpeed > 0f;

        /// <summary>True, wenn der Spell channeled ist (gemäß Attributes-Bit).</summary>
        [JsonIgnore]
        public bool IsChanneled => (Attributes & SpellAttributes.Channeled) != 0;

        /// <summary>True, wenn der Spell passiv permanent wirkt.</summary>
        [JsonIgnore]
        public bool IsPassive => (Attributes & SpellAttributes.Passive) != 0;

        /// <summary>True, wenn der Spell instant ist (kein Cast-Bar).</summary>
        [JsonIgnore]
        public bool IsInstant => CastTimeMs <= 0;
    }
}
