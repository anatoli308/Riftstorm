using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Baut zur Laufzeit eine <see cref="SpellVisualDefinition"/> aus dem
    /// Source-Tabellenpaar <see cref="SpellVisualKitMappingCatalog"/> +
    /// <see cref="SpellVisualKitDefinitionCatalog"/> zusammen. Brueckt damit
    /// das Source-Schema (per-Entry Kit-IDs &#8594; per-ID Kit-Defs) auf den
    /// renderer-freundlichen <see cref="SpellVisualPhase"/>-Plan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phasen-Mapping (Source &#8594; <see cref="SpellVisualDefinition"/>):
    /// <list type="bullet">
    ///   <item><c>casting_kit</c>     &#8594; <see cref="SpellVisualDefinition.Casting"/></item>
    ///   <item><c>traveling_kit</c>   &#8594; <see cref="SpellVisualDefinition.Travel"/></item>
    ///   <item><c>impact_kit</c>      &#8594; <see cref="SpellVisualDefinition.Impact"/></item>
    ///   <item><c>go_kit</c>          &#8594; <see cref="SpellVisualDefinition.Ground"/> (Boden-Anim an Cast-Destination)</item>
    ///   <item><c>aura_kit_ontop</c> bzw. Fallback <c>aura_kit_below</c> &#8594; <see cref="SpellVisualDefinition.AuraLoop"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// Pro Kit werden saemtliche Phase-2-Felder uebertragen: zweiter Sprite-
    /// Layer (<c>spranim_2</c>) inkl. Offset/Tint/Blend, Sound, Glow-Farben
    /// und Particle-System-Name.
    /// </para>
    /// <para>
    /// <b>Travel-Speed</b>: Im Source-Schema nicht enthalten &#8212; konstanter
    /// Default (<see cref="DefaultTravelSpeed"/>). Per-Spell-Override kommt
    /// spaeter ueber das Spell-Template oder eine Travel-Speed-Tabelle.
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
                Casting = ResolvePhase(m.CastingKit, defs),
                Travel = ResolvePhase(m.TravelingKit, defs),
                Impact = ResolvePhase(m.ImpactKit, defs),
                Ground = ResolvePhase(m.GoKit, defs),
                AuraLoop = ResolvePhase(auraKit, defs),
                TravelSpeed = DefaultTravelSpeed,
            };
            return def.HasAny ? def : null;
        }

        /// <summary>
        /// Baut eine <see cref="SpellVisualPhase"/> aus einer Kit-ID. Liefert
        /// <see cref="SpellVisualPhase.Empty"/>, wenn <paramref name="kitId"/>
        /// = 0 oder kein Eintrag im Catalog steht.
        /// </summary>
        public static SpellVisualPhase ResolvePhase(int kitId, SpellVisualKitDefinitionCatalog defs)
        {
            if (kitId == 0) { return SpellVisualPhase.Empty; }
            if (!defs.TryGet(kitId, out SpellVisualKitDefinition d) || d == null) { return SpellVisualPhase.Empty; }

            return new SpellVisualPhase
            {
                PrimaryAnim = d.PrimaryAnimationName,
                SecondaryAnim = d.SecondaryAnimationName,
                PrimaryOffsetPx = new Vector2(d.SpranimX, ResolveYPx(d.SpranimY)),
                SecondaryOffsetPx = new Vector2(d.Spranim2X, ResolveYPx(d.Spranim2Y)),
                PrimaryOffsetYHeightFactor = ResolveYHeightFactor(d.SpranimY),
                SecondaryOffsetYHeightFactor = ResolveYHeightFactor(d.Spranim2Y),
                PrimaryTint = DecodeTint(d.Sprcolor),
                SecondaryTint = DecodeTint(d.Sprcolor2),
                PrimaryBlend = DecodeBlend(d.SpranimBlend),
                SecondaryBlend = DecodeBlend(d.Spranim2Blend),
                SecondaryTopmost = d.Spranim2Topmost,
                SoundFile = d.Sound ?? string.Empty,
                GroundGlowColor = DecodeGlow(d.GroundGlowColor),
                UnitGlowColor = DecodeGlow(d.UnitGlowColor),
                ParticleSystemName = d.Psystem ?? string.Empty,
            };
        }

        // ---- Helpers ---------------------------------------------------

        /// <summary>
        /// Decodiert eine Source-Tint-Farbe (32-Bit ARGB als unsigned). Werte
        /// <c>&lt;= 0</c> &#8594; weiss (kein Tint).
        /// </summary>
        private static Color DecodeTint(long argb)
        {
            if (argb <= 0L) { return Color.white; }
            return ArgbToColor(argb);
        }

        /// <summary>
        /// Decodiert eine Source-Glow-Farbe. <c>0</c> &#8594; transparent (kein Glow).
        /// Bei alpha == 0 mit nicht-leeren RGB wird alpha auf 1 hochgezogen,
        /// damit ein explizit gesetzter Glow nicht stumm "unsichtbar" wird.
        /// </summary>
        private static Color DecodeGlow(long argb)
        {
            if (argb <= 0L) { return new Color(0f, 0f, 0f, 0f); }
            Color c = ArgbToColor(argb);
            if (c.a <= 0f && (c.r > 0f || c.g > 0f || c.b > 0f))
            {
                c.a = 1f;
            }
            return c;
        }

        /// <summary>
        /// Konvertiert 0xAARRGGBB &#8594; <see cref="Color"/>. Source-DB-Export
        /// haelt ARGB als unsigned 32-Bit; wir behandeln den long als
        /// 0xAARRGGBB.
        /// </summary>
        private static Color ArgbToColor(long argb)
        {
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        /// <summary>Decodiert <c>spranim_blend</c>/<c>spranim2_blend</c> in einen <see cref="SpellVisualBlend"/>.</summary>
        private static SpellVisualBlend DecodeBlend(int blend)
        {
            return blend == 1 ? SpellVisualBlend.Additive : SpellVisualBlend.Default;
        }

        /// <summary>
        /// Versucht, einen Y-Offset-String als Pixel-Integer zu interpretieren.
        /// Liefert nur den statischen Anteil &#8212; sprite-hoehen-relative
        /// Tags (siehe <see cref="ResolveYHeightFactor"/>) tragen 0 bei.
        /// </summary>
        private static int ResolveYPx(string raw)
        {
            if (string.IsNullOrEmpty(raw)) { return 0; }
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int px))
            {
                return px;
            }
            return 0;
        }

        /// <summary>
        /// Decodiert FLARE-hoehen-relative Y-Tags in einen Faktor, der zur
        /// Render-Zeit mit der Sprite-Hoehe in Pixeln multipliziert wird.
        /// Erkannte Formen:
        /// <list type="bullet">
        ///   <item><c>"heightp"</c> &#8594; <c>1</c></item>
        ///   <item><c>"-heightp"</c> &#8594; <c>-1</c></item>
        ///   <item><c>"0.5heightp"</c>, <c>"-0.5heightp"</c> usw. &#8594; numerischer Faktor</item>
        /// </list>
        /// Liefert <c>0</c>, wenn kein Hoehen-Tag erkannt wurde.
        /// </summary>
        private static float ResolveYHeightFactor(string raw)
        {
            if (string.IsNullOrEmpty(raw)) { return 0f; }
            int suffix = raw.IndexOf("heightp", System.StringComparison.OrdinalIgnoreCase);
            if (suffix < 0) { return 0f; }

            string prefix = raw.Substring(0, suffix).Trim();
            if (prefix.Length == 0) { return 1f; }
            if (prefix == "-") { return -1f; }
            if (prefix == "+") { return 1f; }
            if (float.TryParse(prefix, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float factor))
            {
                return factor;
            }
            return 0f;
        }
    }
}
