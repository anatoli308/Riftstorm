using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Visualisierungs-Daten fuer eine einzelne Phase eines Spell-Casts
    /// (Casting, Travel, Impact, Ground, Aura-Loop). 1:1 die Felder eines
    /// FLARE <c>spell_visual</c>-Kits — aufgeloest zur Laufzeit vom
    /// <see cref="SpellVisualResolver"/> in eine renderer-freundliche Form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eine Phase kann bis zu zwei Sprite-Layer haben (<c>spranim</c> +
    /// <c>spranim_2</c>). Beide haben eigene Offsets, Tints und Blend-Modi.
    /// </para>
    /// <para>
    /// <b>Sound</b> wird beim Phase-Start einmal ausgeloest
    /// (<c>AudioSource.PlayClipAtPoint</c>).
    /// </para>
    /// <para>
    /// <b>Glow-Farben</b> spawnen jeweils ein <see cref="Light"/>-Child:
    /// <see cref="GroundGlowColor"/> ein <c>Point Light</c> am Visual-Anker
    /// (=&gt; faerbt den Boden), <see cref="UnitGlowColor"/> ein
    /// <c>Point Light</c> am betroffenen Unit-Transform. <c>alpha == 0</c>
    /// bei beiden Komponenten = kein Glow.
    /// </para>
    /// <para>
    /// <b>Particle System</b> ist aktuell nur als Name vorhanden — Spawner
    /// loggt eine einmalige Warnung pro unbekanntem Namen, bis der
    /// <c>psystem</c>-Porter steht.
    /// </para>
    /// </remarks>
    public sealed class SpellVisualPhase
    {
        /// <summary>Primaerer Sprite-Layer (Animations-Name ohne <c>.sa</c>). Leer = keine Phase.</summary>
        public string PrimaryAnim = string.Empty;

        /// <summary>Sekundaerer Sprite-Layer (Animations-Name ohne <c>.sa</c>). Optional.</summary>
        public string SecondaryAnim = string.Empty;

        /// <summary>Pixel-Offset des primaeren Layers (Source-Koordinaten, X rechts, Y unten).</summary>
        public Vector2 PrimaryOffsetPx;

        /// <summary>Pixel-Offset des sekundaeren Layers.</summary>
        public Vector2 SecondaryOffsetPx;

        /// <summary>
        /// Sprite-hoehen-relativer Y-Faktor fuer den primaeren Layer (FLARE
        /// <c>"-heightp"</c>/<c>"heightp"</c>). Der finale Y-Offset ist
        /// <c>PrimaryOffsetPx.y + PrimaryOffsetYHeightFactor * animHeightPx</c>
        /// und wird vom Renderer aufgeloest, sobald die Animation bekannt ist.
        /// 0 = kein hoehen-relativer Anteil.
        /// </summary>
        public float PrimaryOffsetYHeightFactor;

        /// <summary>Sprite-hoehen-relativer Y-Faktor fuer den sekundaeren Layer. Siehe <see cref="PrimaryOffsetYHeightFactor"/>.</summary>
        public float SecondaryOffsetYHeightFactor;

        /// <summary>Tint-Color des primaeren Layers. Default = <see cref="Color.white"/> (kein Tint).</summary>
        public Color PrimaryTint = Color.white;

        /// <summary>Tint-Color des sekundaeren Layers.</summary>
        public Color SecondaryTint = Color.white;

        /// <summary>Blend-Mode des primaeren Layers.</summary>
        public SpellVisualBlend PrimaryBlend = SpellVisualBlend.Default;

        /// <summary>Blend-Mode des sekundaeren Layers.</summary>
        public SpellVisualBlend SecondaryBlend = SpellVisualBlend.Default;

        /// <summary>True, wenn der sekundaere Layer ueber dem primaeren liegt.</summary>
        public bool SecondaryTopmost;

        /// <summary>Sound-Dateiname (inkl. Extension) fuer Phase-Start. Leer = stumm.</summary>
        public string SoundFile = string.Empty;

        /// <summary>Boden-Glow-Farbe. <c>alpha == 0</c> = kein Glow.</summary>
        public Color GroundGlowColor = new(0f, 0f, 0f, 0f);

        /// <summary>Unit-Glow-Farbe. <c>alpha == 0</c> = kein Glow.</summary>
        public Color UnitGlowColor = new(0f, 0f, 0f, 0f);

        /// <summary>Particle-System-Bezeichner (Source-Format). Aktuell nur geloggt.</summary>
        public string ParticleSystemName = string.Empty;

        /// <summary>True, wenn der primaere Sprite-Layer gesetzt ist (Phase wird gespielt).</summary>
        public bool HasPrimary => !string.IsNullOrEmpty(PrimaryAnim);

        /// <summary>True, wenn der sekundaere Sprite-Layer gesetzt ist.</summary>
        public bool HasSecondary => !string.IsNullOrEmpty(SecondaryAnim);

        /// <summary>True, wenn irgendetwas Visuelles/Auditives in der Phase passiert.</summary>
        public bool HasAny =>
            HasPrimary
            || HasSecondary
            || !string.IsNullOrEmpty(SoundFile)
            || GroundGlowColor.a > 0f
            || UnitGlowColor.a > 0f
            || !string.IsNullOrEmpty(ParticleSystemName);

        /// <summary>Leere Phase-Singleton-Instanz (alle Felder Default). Wird vom Resolver fuer ungesetzte Phasen zurueckgegeben.</summary>
        public static readonly SpellVisualPhase Empty = new();

        /// <summary>
        /// Liefert den effektiven Pixel-Offset des primaeren Layers inklusive
        /// sprite-hoehen-relativem Anteil. <paramref name="animCanvasPx"/>
        /// stammt aus <c>SpellAnimationDefinition.CanvasSize</c> &#8212; FLARE
        /// positioniert relativ zur Canvas, NICHT zur ausgeschnittenen Frame-Hoehe,
        /// damit der Y-Faktor stabil bleibt, wenn unterschiedliche Frames
        /// unterschiedliche <c>h</c> haben.
        /// </summary>
        /// <remarks>
        /// <b>Topdown-Anpassung gegenueber Source:</b> Der X-Anteil von
        /// <see cref="PrimaryOffsetPx"/> wird hier verworfen. In FLARE
        /// definiert <c>spranim_x</c> den Pivot innerhalb der Sprite-Canvas
        /// fuer die iso-2D-Rendering-Konvention &#8212; im topdown-billboarded
        /// Riftstorm-Setup uebersetzt er sich faelschlich in einen
        /// Welt-Seiten-Offset (z.B. Scorch-Impact landet 0.75 m neben dem
        /// Ziel). Die Y-Komponente bleibt, da <c>heightp</c>-Faktoren echte
        /// Hub-Hoehe oberhalb des Ziels kodieren.
        /// </remarks>
        public Vector2 EffectivePrimaryOffsetPx(int animCanvasPx)
        {
            return new Vector2(0f, PrimaryOffsetPx.y + PrimaryOffsetYHeightFactor * animCanvasPx);
        }

        /// <summary>
        /// Liefert den effektiven Pixel-Offset des sekundaeren Layers
        /// inklusive sprite-hoehen-relativem Anteil. Erwartet die Canvas-Hoehe
        /// (siehe <see cref="EffectivePrimaryOffsetPx(int)"/>).
        /// </summary>
        /// <remarks>Siehe Hinweis in <see cref="EffectivePrimaryOffsetPx(int)"/> zur X-Null-Setzung.</remarks>
        public Vector2 EffectiveSecondaryOffsetPx(int animCanvasPx)
        {
            return new Vector2(0f, SecondaryOffsetPx.y + SecondaryOffsetYHeightFactor * animCanvasPx);
        }
    }
}
