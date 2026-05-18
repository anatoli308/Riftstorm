namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Baut zur Laufzeit eine <see cref="SpellVisualDefinition"/> aus dem
    /// Source-Tabellenpaar <see cref="SpellVisualKitMappingCatalog"/> +
    /// <see cref="SpellVisualKitDefinitionCatalog"/> zusammen. Brueckt damit
    /// das neue Source-Schema (per-Entry Kit-IDs &#8594; per-ID Kit-Defs) auf den
    /// bestehenden <c>WorldSpellAnimation</c>-/<c>SpellVisualSpawner</c>-Pfad.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phasen-Mapping (Source &#8594; <c>WorldSpellAnimation</c>):
    /// <list type="bullet">
    ///   <item><c>casting_kit</c>   &#8594; <see cref="SpellVisualDefinition.CastingAnim"/></item>
    ///   <item><c>traveling_kit</c> &#8594; <see cref="SpellVisualDefinition.TravelAnim"/></item>
    ///   <item><c>impact_kit</c>    &#8594; <see cref="SpellVisualDefinition.ImpactAnim"/></item>
    ///   <item><c>aura_kit_ontop</c> bzw. Fallback <c>aura_kit_below</c> &#8594; <see cref="SpellVisualDefinition.AuraLoopAnim"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// Aktuell nicht uebernommen (Phase-2-Themen): zweiter Sprite-Layer
    /// (<c>spranim_2</c>), Glow-Farben, Sound, Partikel-System, Blend-Modi.
    /// Diese Felder werden bewusst ignoriert, bis der Spawner zwei-Layer-faehig ist.
    /// </para>
    /// <para>
    /// <b>Travel-Speed</b>: Im Source-Schema nicht enthalten — wir nehmen einen
    /// konstanten Default (siehe <see cref="DefaultTravelSpeed"/>). Ein
    /// per-Spell-Override kommt in einem spaeteren Schritt (z. B. ueber das
    /// Spell-Template oder eine zusaetzliche Travel-Speed-Tabelle).
    /// </para>
    /// </remarks>
    public static class SpellVisualResolver
    {
        /// <summary>Default-Travel-Speed in Units/Sekunde (Riftstorm-Konvention).</summary>
        public const float DefaultTravelSpeed = 20f;

        /// <summary>
        /// Resolves den Visual-Plan fuer <paramref name="spellEntry"/>. Liefert
        /// <c>null</c>, wenn weder Mapping noch Kit-Lookup ein nutzbares Visual
        /// ergeben.
        /// </summary>
        public static SpellVisualDefinition Resolve(
            int spellEntry,
            SpellVisualKitMappingCatalog mappings,
            SpellVisualKitDefinitionCatalog defs)
        {
            if (mappings == null || defs == null) { return null; }
            if (!mappings.TryGet(spellEntry, out SpellVisualKitMapping m) || m == null) { return null; }
            if (!m.HasAny) { return null; }

            int auraKit = m.AuraKitOntop != 0 ? m.AuraKitOntop : m.AuraKitBelow;

            SpellVisualDefinition def = new()
            {
                SpellId = spellEntry.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CastingAnim = ResolveAnimationName(m.CastingKit, defs),
                TravelAnim = ResolveAnimationName(m.TravelingKit, defs),
                ImpactAnim = ResolveAnimationName(m.ImpactKit, defs),
                AuraLoopAnim = ResolveAnimationName(auraKit, defs),
                TravelSpeed = DefaultTravelSpeed,
            };
            return def.HasAny ? def : null;
        }

        private static string ResolveAnimationName(int kitId, SpellVisualKitDefinitionCatalog defs)
        {
            if (kitId == 0) { return string.Empty; }
            if (!defs.TryGet(kitId, out SpellVisualKitDefinition d) || d == null) { return string.Empty; }
            return d.PrimaryAnimationName;
        }
    }
}
