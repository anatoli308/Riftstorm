using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Per-Spell-Visual-Kit (3-Phasen-Modell: Casting → Travel → Impact +
    /// optionaler Aura-Loop). Mirror der C++-Original-Tabelle <c>spell_visual</c>.
    /// Wird vom <see cref="SpellVisualCatalog"/> per <c>spell_id</c> gefunden und
    /// vom Runtime-Player auf Client-Seite abgespielt.
    /// </summary>
    /// <remarks>
    /// Alle <c>*_anim</c>-Werte sind Animations-Namen aus dem
    /// <see cref="SpellAnimationCatalog"/>. Leerstrings bedeuten "Phase überspringen".
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellVisualDefinition
    {
        /// <summary>Verknüpft mit <c>SpellDefinition.Id</c>.</summary>
        [JsonProperty("spell_id")] public string SpellId = string.Empty;

        /// <summary>Animations-Name für die Cast-Phase (auf dem Caster). Optional.</summary>
        [JsonProperty("casting_anim")] public string CastingAnim = string.Empty;

        /// <summary>Animations-Name für das Projektil (vom Caster zum Ziel). Optional.</summary>
        [JsonProperty("travel_anim")] public string TravelAnim = string.Empty;

        /// <summary>Animations-Name für den Impact-Effekt (am Ziel). Optional.</summary>
        [JsonProperty("impact_anim")] public string ImpactAnim = string.Empty;

        /// <summary>
        /// Optionale Looping-Aura-Animation (z. B. dauerhaftes Glühen am Ziel,
        /// wenn ein Buff aktiv ist).
        /// </summary>
        [JsonProperty("aura_loop_anim")] public string AuraLoopAnim = string.Empty;

        /// <summary>
        /// Reisegeschwindigkeit des Projektils in Welt-Einheiten/Sekunde.
        /// Nur relevant, wenn <see cref="TravelAnim"/> gesetzt ist.
        /// </summary>
        [JsonProperty("travel_speed")] public float TravelSpeed;

        /// <summary>True, wenn überhaupt eine Phase ausgespielt werden kann.</summary>
        public bool HasAny =>
            !string.IsNullOrEmpty(CastingAnim) ||
            !string.IsNullOrEmpty(TravelAnim) ||
            !string.IsNullOrEmpty(ImpactAnim) ||
            !string.IsNullOrEmpty(AuraLoopAnim);
    }
}
