using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Per-Spell-Eintrag aus <c>StreamingAssets/spells/_visuals.json</c>.
    /// 1:1 Mirror der Source-Tabelle <c>spell_visual_kit</c>: ordnet jedem
    /// Spell-Entry bis zu sechs Kit-IDs zu (Casting, Impact, Travelling,
    /// GameObject, Aura-Ontop, Aura-Below) plus zwei Animations-Indizes fuer
    /// die Unit-Animation am Caster-Sprite (Cast-Pose, Go-Pose).
    /// </summary>
    /// <remarks>
    /// Kit-IDs verweisen auf <see cref="SpellVisualKitDefinition.Id"/> (geladen
    /// aus <c>_visual_kits.json</c>). Ein Wert von <c>0</c> bedeutet "kein Kit"
    /// und die zugehoerige Phase wird uebersprungen.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellVisualKitMapping
    {
        /// <summary>Primaerschluessel (= <see cref="Spells.SpellTemplate.Entry"/>).</summary>
        [JsonProperty("entry")] public int Entry { get; set; }

        /// <summary>Kit-ID fuer die Casting-Phase am Caster. <c>0</c> = keine.</summary>
        [JsonProperty("casting_kit")] public int CastingKit { get; set; }

        /// <summary>Kit-ID fuer die Impact-Animation am Ziel. <c>0</c> = keine.</summary>
        [JsonProperty("impact_kit")] public int ImpactKit { get; set; }

        /// <summary>Kit-ID fuer das Projektil-Visual (Caster &#8594; Ziel). <c>0</c> = hitscan/instant.</summary>
        [JsonProperty("traveling_kit")] public int TravelingKit { get; set; }

        /// <summary>Kit-ID fuer ein platziertes Welt-Objekt (z. B. Portal, Trap). <c>0</c> = keines.</summary>
        [JsonProperty("go_kit")] public int GoKit { get; set; }

        /// <summary>Kit-ID fuer den Aura-Loop ueber dem Unit-Sprite. <c>0</c> = kein Overlay.</summary>
        [JsonProperty("aura_kit_ontop")] public int AuraKitOntop { get; set; }

        /// <summary>Kit-ID fuer den Aura-Loop unter dem Unit-Sprite (Fuss-Glow). <c>0</c> = kein Underlay.</summary>
        [JsonProperty("aura_kit_below")] public int AuraKitBelow { get; set; }

        /// <summary>Optionaler Index fuer eine Unit-Animation, die beim Wirken am Caster gespielt wird.</summary>
        [JsonProperty("unit_go_animation")] public int UnitGoAnimation { get; set; }

        /// <summary>Optionaler Index fuer eine Unit-Cast-Pose am Caster.</summary>
        [JsonProperty("unit_cast_animation")] public int UnitCastAnimation { get; set; }

        /// <summary>True, wenn mindestens ein Kit-Slot belegt ist.</summary>
        public bool HasAny =>
            CastingKit != 0 || ImpactKit != 0 || TravelingKit != 0
            || GoKit != 0 || AuraKitOntop != 0 || AuraKitBelow != 0;
    }
}
