using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Datengetriebene Aura-Definition. Quelle: <c>StreamingAssets/spells/auras.json</c>.
    /// Wird von <see cref="SpellEffectType.ApplyAura"/>-Effekten referenziert.
    /// </summary>
    /// <remarks>
    /// Felder spiegeln die Aura-relevanten Spalten aus
    /// <c>GameData.h::SpellTemplate</c> (duration, interval, stackAmount,
    /// dispel, mechanic + effectData[N]) — auf eigenständige Aura-Entitäten
    /// gemappt, damit ein einziger Aura-Eintrag von mehreren Spells genutzt
    /// werden kann (z. B. <c>burn</c> via <c>fireball</c> + <c>scorch</c>).
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AuraDefinition
    {
        /// <summary>Eindeutiger String-Key (z. B. <c>"burn"</c>, <c>"chilled"</c>).</summary>
        [JsonProperty("id")] public string Id;

        /// <summary>Anzeigename für UI (lokalisierbar).</summary>
        [JsonProperty("name")] public string Name = string.Empty;

        /// <summary>Icon-Key (für UI). Pfad relativ unter <c>Art/icons/</c>.</summary>
        [JsonProperty("icon")] public string Icon = string.Empty;

        /// <summary>Wirkungstyp (was die Aura macht).</summary>
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public AuraType Type = AuraType.None;

        /// <summary>
        /// School der Aura (für PeriodicDamage-Resistenz-Berechnung). Bei
        /// nicht-damage-Auren irrelevant.
        /// </summary>
        [JsonProperty("school")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellSchool School = SpellSchool.None;

        /// <summary>Gesamtdauer in Millisekunden. 0 = permanent (Passive).</summary>
        [JsonProperty("duration_ms")] public int DurationMs;

        /// <summary>
        /// Tick-Interval in Millisekunden (nur für periodische Auren).
        /// Beispiel: DurationMs=8000, IntervalMs=2000 → 4 Ticks.
        /// </summary>
        [JsonProperty("interval_ms")] public int IntervalMs;

        /// <summary>Maximale Stack-Anzahl auf einem Ziel. Default 1 = nicht stackbar.</summary>
        [JsonProperty("max_stacks")] public int MaxStacks = 1;

        /// <summary>Wie die Aura entfernt werden kann.</summary>
        [JsonProperty("dispel_type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellDispelType DispelType = SpellDispelType.None;

        /// <summary>
        /// Mechanic-Bitmask. Beeinflusst CC-Immunities und Movement-Validation
        /// auf dem Server (z. B. <c>SpellMechanic.Snare</c> → Movement-Speed-Cap).
        /// </summary>
        [JsonProperty("mechanic")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellMechanic Mechanic = SpellMechanic.None;

        /// <summary>Primär-Wert (z. B. Schaden pro Tick, +x % Speed, Stat-Id).</summary>
        [JsonProperty("data1")] public int Data1;

        /// <summary>Sekundär-Wert (Stat-Amount, School-Id für Resists).</summary>
        [JsonProperty("data2")] public int Data2;

        /// <summary>Tertiär-Wert.</summary>
        [JsonProperty("data3")] public int Data3;

        /// <summary>True = positiver Buff (grüner UI-Rahmen). False = Debuff.</summary>
        [JsonProperty("positive")] public bool Positive;

        /// <summary>
        /// Wenn <c>true</c>, setzt ein erneutes Anwenden die Restdauer auf
        /// <see cref="DurationMs"/> zurück. Default: <c>true</c>.
        /// </summary>
        [JsonProperty("refresh_on_reapply")] public bool RefreshOnReapply = true;
    }
}
