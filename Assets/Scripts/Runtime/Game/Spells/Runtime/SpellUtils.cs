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
        /// Liefert den skalierten Effekt-Wert. Wenn <see cref="SpellTemplateEffect.ScaleFormula"/>
        /// gesetzt ist, wird die Formel mit <c>value = Data1</c> evaluiert; sonst
        /// faellt der Wert auf <c>Data1</c> zurueck. Negative Ergebnisse werden
        /// nicht geclamped — Caller (z.B. Heal vs. Damage) entscheidet.
        /// </summary>
        /// <remarks>
        /// Source-Aequivalent: <c>SpellMgr::calculateEffectValue</c>
        /// (<c>source_server/Server/src/Combat/SpellMgr.cpp</c>).
        /// </remarks>
        public static int CalculateEffectValue(SpellTemplateEffect eff, ICombatUnit caster)
        {
            if (string.IsNullOrEmpty(eff.ScaleFormula))
            {
                return (int)System.Math.Min(int.MaxValue, eff.Data1);
            }
            int baseValue = (int)System.Math.Min(int.MaxValue, eff.Data1);
            SpellFormulaContext ctx = BuildContext(caster, baseValue);
            return SpellFormulaEvaluator.Evaluate(eff.ScaleFormula, ctx);
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
