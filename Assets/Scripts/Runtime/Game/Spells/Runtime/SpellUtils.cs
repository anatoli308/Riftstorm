using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Klassifikations-Helfer auf <see cref="SpellTemplate"/>. Stateless,
    /// allokationsfrei. Bewusst minimal — Formel-Evaluator und Mana-Berechnung
    /// kommen erst, wenn ein konkreter Spell sie wirklich braucht (YAGNI).
    /// </summary>
    public static class SpellUtils
    {
        /// <summary>Konvertiert Source-Pixel-Reichweite in Unity-Meter.</summary>
        /// <remarks>
        /// 64 px / m (gleiche Konvention wie <c>NpcController</c>-Movement).
        /// </remarks>
        public const float PixelsPerMeter = 64f;

        /// <summary>Source-Pixel-Range zu Unity-Metern.</summary>
        public static float RangeToMeters(float rangePixels) => rangePixels / PixelsPerMeter;

        /// <summary>
        /// Konvertiert FLARE-Tile-Distanz in Unity-Meter. Wird fuer
        /// Movement-Effekte (KnockBack/PullTo/Charge/SlideFrom/TeleportForward)
        /// genutzt, deren <c>Data1</c> in der Source-Datentabelle in Tiles
        /// ausgedrueckt ist (kleine Ganzzahlen 1-4), nicht in Pixeln. 1 Tile
        /// entspricht im Topdown-Grid 1 Meter.
        /// </summary>
        public const float MetersPerTile = 1f;

        /// <summary>FLARE-Tile-Distanz zu Unity-Metern (1 Tile = 1 m).</summary>
        public static float TilesToMeters(float tiles) => tiles * MetersPerTile;

        /// <summary>
        /// Logische Source-Engine-Framerate, gegen die Projectile-Speeds
        /// kalibriert sind. FLARE und vergleichbare 2D-Engines speichern
        /// <c>speed</c> als <i>Pixel pro logischem Frame</i> bei 60 fps.
        /// </summary>
        public const float SourceFramesPerSecond = 60f;

        /// <summary>
        /// Konvertiert eine Source-Projectile-Geschwindigkeit
        /// (<see cref="SpellTemplate.Speed"/>, Einheit: Pixel pro 60-fps-Frame)
        /// in Unity-Meter/Sekunde. Liefert <c>0</c>, wenn der Eingabewert
        /// nicht positiv ist (Spell ist dann nicht als Projectile zu behandeln).
        /// </summary>
        public static float ProjectileSpeedToMps(float speedPixelsPerFrame)
        {
            if (speedPixelsPerFrame <= 0f) { return 0f; }
            return speedPixelsPerFrame * SourceFramesPerSecond / PixelsPerMeter;
        }

        /// <summary>True wenn <see cref="SpellTemplate.CastTime"/> == 0.</summary>
        public static bool IsInstant(SpellTemplate spell) => spell != null && spell.CastTime <= 0;

        /// <summary>True wenn mindestens ein Effekt-Slot einen Radius > 0 hat.</summary>
        public static bool IsAoE(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return spell.Effect1Radius > 0 || spell.Effect2Radius > 0 || spell.Effect3Radius > 0;
        }

        /// <summary>True wenn der Spell ausschliesslich auf den Caster wirkt.</summary>
        public static bool IsSelfOnly(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return AllActiveTargetsAre(spell, SpellTargetType.UnitCaster);
        }

        /// <summary>True wenn der Spell ein explizites Ziel benoetigt.</summary>
        public static bool RequiresTarget(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return AnyActiveTargetMatches(spell, IsExplicitTargetType);
        }

        /// <summary>True wenn der Spell auf verbuendete Einheiten wirken kann.</summary>
        public static bool CanTargetFriendly(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return AnyActiveTargetMatches(spell, IsFriendlyTarget);
        }

        /// <summary>True wenn der Spell auf feindliche Einheiten wirken kann.</summary>
        public static bool CanTargetHostile(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return AnyActiveTargetMatches(spell, IsHostileTarget);
        }

        /// <summary>True wenn mindestens ein Effekt-Slot Schaden verursacht.</summary>
        public static bool IsDamageSpell(SpellTemplate spell)
            => spell != null && spell.IsOffensive;

        /// <summary>True wenn mindestens ein Effekt-Slot heilt.</summary>
        public static bool IsHealSpell(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return spell.Effect1 == SpellEffect.Heal || spell.Effect1 == SpellEffect.HealPct
                || spell.Effect2 == SpellEffect.Heal || spell.Effect2 == SpellEffect.HealPct
                || spell.Effect3 == SpellEffect.Heal || spell.Effect3 == SpellEffect.HealPct;
        }

        /// <summary>True wenn mindestens ein Effekt-Slot eine Aura appliziert.</summary>
        public static bool IsAuraSpell(SpellTemplate spell)
        {
            if (spell == null) { return false; }
            return IsApplyAura(spell.Effect1) || IsApplyAura(spell.Effect2) || IsApplyAura(spell.Effect3);
        }

        /// <summary>
        /// Bestimmt die Mana-Kosten. Wertet <see cref="SpellTemplate.ManaFormula"/>
        /// per <see cref="SpellFormulaEvaluator"/> aus (Variablen <c>clvl</c>,
        /// <c>splvl</c>, plus Caster-Stats falls in der Formel referenziert).
        /// Addiert anteilige Kosten aus <see cref="SpellTemplate.ManaPct"/>.
        /// </summary>
        public static int CalculateManaCost(SpellTemplate spell, ICombatUnit caster)
        {
            if (spell == null) { return 0; }
            int flat = 0;
            if (!string.IsNullOrEmpty(spell.ManaFormula))
            {
                flat = SpellFormulaEvaluator.Evaluate(spell.ManaFormula, BuildContext(caster));
            }
            if (spell.ManaPct > 0f && caster != null)
            {
                flat += Mathf.RoundToInt(caster.MaxMana * spell.ManaPct);
            }
            return Mathf.Max(0, flat);
        }

        /// <summary>
        /// Liefert den skalierten Effekt-Wert nach Source-Konvention.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Source-Aequivalent: <c>SpellUtils::calculateEffectValue</c>
        /// (<c>source_server/Server/src/Combat/SpellUtils.cpp:193-209</c>).
        /// </para>
        /// <para>
        /// Wenn <see cref="SpellTemplateEffect.ScaleFormula"/> gesetzt ist,
        /// <b>ersetzt</b> deren Ergebnis den Wert vollstaendig — Data1/Data2
        /// werden ignoriert, es gibt <b>keinen RNG-Roll</b>. Ohne Formel gilt
        /// <c>data1 + data2 * clvl</c>. Variance (<c>±10%</c>) wird erst in
        /// <c>CombatFormulas.CalculateSpellDamage</c> als finaler Schritt addiert,
        /// genau wie in <c>CombatFormulas::applyDamageVariance</c> der Source.
        /// </para>
        /// </remarks>
        public static int CalculateEffectValue(SpellTemplateEffect eff, ICombatUnit caster)
        {
            return EvaluateEffectFormula(eff, caster);
        }

        /// <summary>
        /// Min-Variante fuer Tooltip-Anzeige (<c>$E&lt;N&gt;min</c>). Da Source
        /// keine Data1/Data2-Range kennt, ist Min == Max == Server-Wert vor
        /// Variance. Die ±10% Variance ist Damage-Roll-Logik und wird hier
        /// bewusst nicht reflektiert.
        /// </summary>
        public static int CalculateEffectValueMin(SpellTemplateEffect eff, ICombatUnit caster)
        {
            return EvaluateEffectFormula(eff, caster);
        }

        /// <summary>
        /// Max-Variante fuer Tooltip-Anzeige (<c>$E&lt;N&gt;max</c>). Identisch
        /// zu <see cref="CalculateEffectValueMin"/> — siehe dort.
        /// </summary>
        public static int CalculateEffectValueMax(SpellTemplateEffect eff, ICombatUnit caster)
        {
            return EvaluateEffectFormula(eff, caster);
        }

        /// <summary>
        /// Einheitliche Effekt-Wert-Berechnung: Formel ersetzt, sonst
        /// <c>data1 + data2 * clvl</c>. FLARE-kanonische Semantik (volle
        /// Formel-Unterstuetzung mit allen Tokens, nicht die abgespeckte
        /// cpp-Port-Variante, die nur <c>clvl</c> kennt).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Effekt-spezifischer <c>value</c>-Seed</b>: In den JSON-Templates
        /// ist die Bedeutung des Tokens <c>value</c> in <c>scale_formula</c>
        /// effektabhaengig:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        ///   Damage / Heal / Mana / Movement-Effekte (z.B.
        ///   <see cref="SpellEffect.WeaponDamage"/>, <see cref="SpellEffect.SchoolDamage"/>,
        ///   <see cref="SpellEffect.Heal"/>): <c>value = data2</c> (Basisbetrag,
        ///   z.B. Aimed Shot data2=115 = 115% Waffenschaden).
        /// </description></item>
        /// <item><description>
        ///   Aura-Effekte (<see cref="SpellEffect.ApplyAura"/>,
        ///   <see cref="SpellEffect.ApplyAreaAura"/>): <c>value = data3</c>
        ///   (Aura-Magnitude, z.B. Arrow Flurry data3=-50 = -50% Cooldown).
        ///   data1=AuraType, data2=MiscValue/School-Bitmask &#8212; <i>nicht</i>
        ///   der Skalar.
        /// </description></item>
        /// </list>
        /// </remarks>
        static int EvaluateEffectFormula(SpellTemplateEffect eff, ICombatUnit caster)
        {
            if (!string.IsNullOrEmpty(eff.ScaleFormula))
            {
                int valueSeed = IsApplyAura(eff.Effect)
                    ? (int)eff.Data3
                    : (int)eff.Data2;
                return SpellFormulaEvaluator.Evaluate(eff.ScaleFormula, BuildContext(caster, valueSeed));
            }
            int clvl = caster?.Level ?? 1;
            return (int)eff.Data1 + (int)eff.Data2 * clvl;
        }

        /// <summary>
        /// Liefert die Aura-/Effekt-Dauer in Millisekunden. Bevorzugt
        /// <see cref="SpellTemplate.DurationFormula"/> (mit Caster-Kontext);
        /// faellt sonst auf den statischen <see cref="SpellTemplate.Duration"/>
        /// zurueck. Rueckgabe <c>0</c> bedeutet permanent (kein Ablauf).
        /// </summary>
        public static int CalculateDuration(SpellTemplate spell, ICombatUnit caster)
        {
            if (spell == null) { return 0; }
            if (!string.IsNullOrEmpty(spell.DurationFormula))
            {
                int evaluated = SpellFormulaEvaluator.Evaluate(spell.DurationFormula, BuildContext(caster));
                if (evaluated > 0) { return evaluated; }
            }
            return Mathf.Max(0, spell.Duration);
        }

        // ---------------------------------------------------------------------

        /// <summary>
        /// Baut den Formel-Kontext aus dem Caster. Fehlende Stats (z.B. INT/WIL/CUR
        /// vor Phase-3-Stats-Ausbau) bleiben <c>0</c> — der Evaluator behandelt
        /// fehlende Variablen ebenfalls als <c>0</c>.
        /// </summary>
        static SpellFormulaContext BuildContext(ICombatUnit caster, int value = 0)
        {
            if (caster == null) { return new SpellFormulaContext(0, 0, value); }
            IUnitStats s = caster.Stats;
            int str = s?.Strength ?? 0;
            int intStat = s?.Intelligence ?? 0;
            int wil = s?.Willpower ?? 0;
            // CUR (Cunning) ist im aktuellen Stat-Modell nicht vorgesehen —
            // bleibt 0; Source-Formeln degradieren sauber (Variable wird zu 0).
            return new SpellFormulaContext(
                clvl: caster.Level,
                splvl: 0,
                value: value,
                str: str,
                intStat: intStat,
                wil: wil,
                cur: 0);
        }

        // ---------------------------------------------------------------------

        static bool IsApplyAura(SpellEffect e)
            => e == SpellEffect.ApplyAura || e == SpellEffect.ApplyAreaAura;

        static bool AllActiveTargetsAre(SpellTemplate spell, SpellTargetType type)
        {
            bool anyActive = false;
            for (int slot = 1; slot <= 3; slot++)
            {
                SpellTemplateEffect e = spell.GetEffect(slot);
                if (!e.IsActive) { continue; }
                anyActive = true;
                if (e.TargetType != type) { return false; }
            }
            return anyActive;
        }

        delegate bool TargetTypePredicate(SpellTargetType t);

        static bool AnyActiveTargetMatches(SpellTemplate spell, TargetTypePredicate predicate)
        {
            for (int slot = 1; slot <= 3; slot++)
            {
                SpellTemplateEffect e = spell.GetEffect(slot);
                if (!e.IsActive) { continue; }
                if (predicate(e.TargetType)) { return true; }
            }
            return false;
        }

        static bool IsExplicitTargetType(SpellTargetType t)
            => t == SpellTargetType.UnitFriendly
            || t == SpellTargetType.UnitHostile
            || t == SpellTargetType.UnitAny
            || t == SpellTargetType.TargetGameObject
            || t == SpellTargetType.TargetItem;

        static bool IsFriendlyTarget(SpellTargetType t)
            => t == SpellTargetType.UnitFriendly
            || t == SpellTargetType.UnitAreaSrcFriendly
            || t == SpellTargetType.UnitAreaDstFriendly
            || t == SpellTargetType.UnitAreaDstFriendlyFromDst
            || t == SpellTargetType.UnitCaster
            || t == SpellTargetType.UnitAny;

        static bool IsHostileTarget(SpellTargetType t)
            => t == SpellTargetType.UnitHostile
            || t == SpellTargetType.UnitAreaSrcHostile
            || t == SpellTargetType.UnitAreaDstHostile
            || t == SpellTargetType.UnitAreaDstHostileFromDst
            || t == SpellTargetType.UnitAny;
    }
}
