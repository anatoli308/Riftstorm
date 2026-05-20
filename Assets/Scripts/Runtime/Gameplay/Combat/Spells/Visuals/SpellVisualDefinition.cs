namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Per-Spell-Visual-Plan (5 Phasen: Casting &#8594; Travel &#8594; Impact + optional
    /// Ground (FLARE <c>go_kit</c>) + optional Aura-Loop). Wird zur Laufzeit
    /// vom <see cref="SpellVisualResolver"/> aus dem Tabellenpaar
    /// <see cref="SpellVisualKitMappingCatalog"/> +
    /// <see cref="SpellVisualKitDefinitionCatalog"/> zusammengebaut und vom
    /// Client-Spawner abgespielt.
    /// </summary>
    /// <remarks>
    /// Jede Phase ist eine vollwertige <see cref="SpellVisualPhase"/> mit bis
    /// zu zwei Sprite-Layern (<c>spranim</c> + <c>spranim_2</c>), Tint, Blend,
    /// Sound, Glow und Particle-System-Name. Wenn ein Phase-Kit im Source
    /// fehlt (Kit-ID = 0), liefert der Resolver <see cref="SpellVisualPhase.Empty"/>
    /// &#8212; nie <c>null</c>.
    /// </remarks>
    public sealed class SpellVisualDefinition
    {
        /// <summary>Spell-Entry-ID (zur Korrelation mit dem Source-Schema).</summary>
        public string SpellId = string.Empty;

        /// <summary>Cast-Phase am Caster (Channel/Wind-up).</summary>
        public SpellVisualPhase Casting = SpellVisualPhase.Empty;

        /// <summary>Travel-Phase (Projektil vom Caster zum Ziel).</summary>
        public SpellVisualPhase Travel = SpellVisualPhase.Empty;

        /// <summary>Impact-Phase am Ziel.</summary>
        public SpellVisualPhase Impact = SpellVisualPhase.Empty;

        /// <summary>Boden-Phase an der Cast-Destination (FLARE <c>go_kit</c>).</summary>
        public SpellVisualPhase Ground = SpellVisualPhase.Empty;

        /// <summary>Loopender Aura-Effekt; wird derzeit vom Renderer noch nicht ausgespielt (separater Aura-Render-Pfad geplant).</summary>
        public SpellVisualPhase AuraLoop = SpellVisualPhase.Empty;

        /// <summary>Reisegeschwindigkeit des Projektils in Welt-Einheiten/Sekunde. Nur relevant bei aktiver Travel-Phase.</summary>
        public float TravelSpeed;

        /// <summary>True, wenn irgendeine Phase etwas Visuelles/Auditives spielt.</summary>
        public bool HasAny =>
            (Casting != null && Casting.HasAny) ||
            (Travel != null && Travel.HasAny) ||
            (Impact != null && Impact.HasAny) ||
            (Ground != null && Ground.HasAny) ||
            (AuraLoop != null && AuraLoop.HasAny);
    }
}
