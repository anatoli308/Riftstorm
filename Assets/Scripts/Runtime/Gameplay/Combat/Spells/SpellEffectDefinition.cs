using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Ein einzelner Effekt-Slot eines Spells. Ein Spell hat bis zu
    /// <see cref="SpellDefinition.MaxEffects"/> Slots, die alle auf den
    /// gleichen Cast (Mana, Range, LoS) hängen, aber unabhängig auflösen.
    /// </summary>
    /// <remarks>
    /// Felder spiegeln <c>GameData.h::SpellTemplate.effect[N] / effectData[N] /
    /// effectTargetType[N] / effectRadius[N] / effectPositive[N] /
    /// effectScaleFormula[N]</c>.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellEffectDefinition
    {
        /// <summary>Wirkungstyp des Slots.</summary>
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellEffectType Type = SpellEffectType.None;

        /// <summary>Zielwahl-Schema des Slots.</summary>
        [JsonProperty("target_type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpellTargetType TargetType = SpellTargetType.None;

        /// <summary>Primär-Daten (z. B. Schadensbetrag, Aura-Data1).</summary>
        [JsonProperty("data1")] public int Data1;

        /// <summary>Sekundär-Daten (z. B. Aura-Data2, Stat-Id).</summary>
        [JsonProperty("data2")] public int Data2;

        /// <summary>Tertiär-Daten (selten benutzt).</summary>
        [JsonProperty("data3")] public int Data3;

        /// <summary>
        /// Radius in Welt-Units für AoE-Targeting (<see cref="SpellTargetType.AreaSrcHostile"/> &amp;
        /// Varianten). 0 = Single-Target.
        /// </summary>
        [JsonProperty("radius")] public float Radius;

        /// <summary>
        /// True = Heal / Buff / Friendly-Beneficial. False = Damage / Debuff.
        /// Beeinflusst Faction-Checks und UI-Farbe der Effekt-Vorschau.
        /// </summary>
        [JsonProperty("positive")] public bool Positive;

        /// <summary>
        /// Skalierungsformel als String (z. B. <c>"sp*0.5+15"</c>). Wird vom
        /// Formula-Evaluator zur Laufzeit interpretiert; leer = nur <see cref="Data1"/>.
        /// </summary>
        [JsonProperty("scale_formula")] public string ScaleFormula = string.Empty;

        /// <summary>
        /// Aura-Id, die dieser Effekt anwendet, wenn <see cref="Type"/> =
        /// <see cref="SpellEffectType.ApplyAura"/>. Referenziert
        /// <c>StreamingAssets/spells/auras.json</c>.
        /// </summary>
        [JsonProperty("aura_id")] public string AuraId = string.Empty;

        /// <summary>
        /// Trigger-Spell-Id, wenn <see cref="Type"/> =
        /// <see cref="SpellEffectType.TriggerSpell"/>.
        /// </summary>
        [JsonProperty("trigger_spell_id")] public string TriggerSpellId = string.Empty;
    }
}
