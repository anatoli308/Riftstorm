using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Spells
{
    /// <summary>Ergebnis einer <see cref="SpellExecutor.Execute"/>-Auswertung.</summary>
    public readonly struct SpellExecutionResult
    {
        /// <summary>Validierungs-/Ausfuehrungs-Ergebnis.</summary>
        public readonly CastResult Result;
        /// <summary>Summe an Schaden, die diesem Cast zugeordnet ist (0 bei Heals/Auras).</summary>
        public readonly int DamageDealt;
        /// <summary>Summe an Heilung, die diesem Cast zugeordnet ist.</summary>
        public readonly int HealingDone;

        /// <summary>Konstruktor.</summary>
        public SpellExecutionResult(CastResult r, int dmg = 0, int heal = 0)
        {
            Result = r;
            DamageDealt = dmg;
            HealingDone = heal;
        }

        /// <summary>Convenience-Konstruktor fuer Erfolg ohne Werte.</summary>
        public static SpellExecutionResult Ok() => new(CastResult.Success);
    }

    /// <summary>
    /// Server-autoritative Spell-Ausfuehrung. Pipeline:
    /// <c>Validate</c> → Resource-Abzug → CD/GCD-Start → Effect-Slot-Loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementierte Effekte (v1): <see cref="SpellEffect.SchoolDamage"/>,
    /// <see cref="SpellEffect.WeaponDamage"/>, <see cref="SpellEffect.Heal"/>,
    /// <see cref="SpellEffect.HealPct"/>, <see cref="SpellEffect.RestoreMana"/>,
    /// <see cref="SpellEffect.ApplyAura"/>, <see cref="SpellEffect.ApplyAreaAura"/>,
    /// <see cref="SpellEffect.TriggerSpell"/>.
    /// </para>
    /// <para>
    /// Alle weiteren Effekte werden geloggt und ignoriert — wenn ein konkreter
    /// Spell sie braucht, wird der Handler hier zugefuegt.
    /// </para>
    /// </remarks>
    public static class SpellExecutor
    {
        // Reusable Physics-Query-Buffer — vermeidet Per-Cast-Allocs in OverlapSphereNonAlloc.
        // Cap auf 64 Hits passt zur ~15-Spieler / paar-hundert-Enemies-Skala
        // (siehe Performance-First-Philosophy in copilot-instructions).
        static readonly Collider[] s_OverlapBuffer = new Collider[64];
        // Pro-Cast wiederverwendetes Target-Set. Wird vor jeder ResolveTargets-Phase
        // geleert; nicht thread-safe — Server-Tick ist single-threaded.
        static readonly List<ICombatUnit> s_TargetBuffer = new(16);

        /// <summary>Fuehrt einen Spell vollstaendig aus (Validation + Effects).</summary>
        public static SpellExecutionResult Execute(ICombatUnit caster, SpellTemplate spell, ICombatUnit primaryTarget)
        {
            CastResult validation = SpellCaster.Validate(caster, spell, primaryTarget);
            if (validation != CastResult.Success)
            {
                return new SpellExecutionResult(validation);
            }

            ConsumeResources(caster, spell);
            StartCooldowns(caster, spell);

            int totalDamage = 0;
            int totalHeal = 0;
            for (int slot = 1; slot <= 3; slot++)
            {
                SpellTemplateEffect eff = spell.GetEffect(slot);
                if (!eff.IsActive) { continue; }

                ResolveTargets(eff, caster, spell, primaryTarget, s_TargetBuffer);
                for (int i = 0; i < s_TargetBuffer.Count; i++)
                {
                    ICombatUnit resolved = s_TargetBuffer[i];
                    if (resolved == null) { continue; }
                    ApplyEffect(caster, spell, resolved, eff, ref totalDamage, ref totalHeal);
                }
                s_TargetBuffer.Clear();
            }

            return new SpellExecutionResult(CastResult.Success, totalDamage, totalHeal);
        }

        // ---------------------------------------------------------------------

        static void ConsumeResources(ICombatUnit caster, SpellTemplate spell)
        {
            int manaCost = SpellUtils.CalculateManaCost(spell, caster);
            if (manaCost > 0)
            {
                caster.SetMana(Mathf.Max(0, caster.Mana - manaCost));
            }
            int hpCost = spell.HealthCost;
            if (spell.HealthPctCost > 0f && caster.MaxHealth > 0)
            {
                hpCost += Mathf.RoundToInt(caster.MaxHealth * spell.HealthPctCost);
            }
            if (hpCost > 0)
            {
                caster.TakeDamage(hpCost, null);
            }
        }

        static void StartCooldowns(ICombatUnit caster, SpellTemplate spell)
        {
            if (!caster.IsPlayer || caster.Cooldowns == null) { return; }
            if (spell.Cooldown > 0)
            {
                caster.Cooldowns.StartCooldown(spell.Entry, spell.Cooldown, spell.CooldownCategory);
            }
            if ((spell.Attributes & SpellAttributes.Triggered) == 0)
            {
                caster.Cooldowns.StartGcd();
            }
        }

        /// <summary>
        /// Befuellt <paramref name="outBuffer"/> mit allen vom Effekt-Slot getroffenen
        /// <see cref="ICombatUnit"/>-Zielen. Beruecksichtigt:
        /// <list type="bullet">
        ///   <item><description>Single-Target-Typen (UnitFriendly/UnitHostile/...): liefert <paramref name="primary"/>.</description></item>
        ///   <item><description>Self/None: liefert <paramref name="caster"/>.</description></item>
        ///   <item><description>UnitAreaSrc*: OverlapSphere am Caster-Pos mit <see cref="SpellTemplateEffect.Radius"/> (Source-px → m).</description></item>
        ///   <item><description>UnitAreaDst*: OverlapSphere an primaerem Ziel; faellt auf Caster zurueck falls null.</description></item>
        /// </list>
        /// Faction-Filter (friendly/hostile) per <see cref="ICombatUnit.FactionId"/>.
        /// Tote/null-Stats werden ausgefiltert. Honoriert <see cref="SpellTemplate.MaxTargets"/>
        /// (0 = unlimitiert) und sortiert in dem Fall aufsteigend nach Distanz zum Zentrum.
        /// Allokationsfrei bis auf optionales <c>List.Sort</c> (nur wenn Cap aktiv).
        /// </summary>
        static void ResolveTargets(
            SpellTemplateEffect eff,
            ICombatUnit caster,
            SpellTemplate spell,
            ICombatUnit primary,
            List<ICombatUnit> outBuffer)
        {
            outBuffer.Clear();

            // Self-/None-Slots ignorieren den Radius (Source-Konvention).
            if (eff.TargetType == SpellTargetType.None
                || eff.TargetType == SpellTargetType.UnitCaster)
            {
                if (caster != null && !caster.IsDead) { outBuffer.Add(caster); }
                return;
            }

            // Single-Target fuer alle nicht-Area-Typen.
            if (!IsAreaTargetType(eff.TargetType) || eff.Radius <= 0)
            {
                if (primary != null && !primary.IsDead) { outBuffer.Add(primary); }
                return;
            }

            // Area: Zentrum bestimmen.
            bool dstCentered = IsDstAreaTargetType(eff.TargetType);
            ICombatUnit center = dstCentered ? (primary ?? caster) : caster;
            if (center == null) { return; }
            Vector3 centerPos = center.Position;
            float radiusM = SpellUtils.RangeToMeters(eff.Radius);

            int hits = Physics.OverlapSphereNonAlloc(
                centerPos,
                radiusM,
                s_OverlapBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            bool wantFriendly = IsFriendlyAreaTargetType(eff.TargetType);
            bool wantHostile = IsHostileAreaTargetType(eff.TargetType);
            int casterFaction = caster?.FactionId ?? -1;

            for (int i = 0; i < hits; i++)
            {
                Collider col = s_OverlapBuffer[i];
                s_OverlapBuffer[i] = null;
                if (col == null) { continue; }

                UnitStats stats = col.GetComponentInParent<UnitStats>();
                if (stats == null) { continue; }
                ICombatUnit candidate = stats;
                if (candidate.IsDead) { continue; }

                bool sameFaction = candidate.FactionId == casterFaction;
                if (wantFriendly && !sameFaction) { continue; }
                if (wantHostile && sameFaction) { continue; }

                outBuffer.Add(candidate);
            }

            // MaxTargets-Cap (0 = unlimitiert). Bei aktivem Cap nach Distanz sortieren,
            // damit naheliegende Ziele zuerst getroffen werden.
            int cap = spell?.MaxTargets ?? 0;
            if (cap > 0 && outBuffer.Count > cap)
            {
                Vector3 sortCenter = centerPos;
                outBuffer.Sort((a, b) =>
                {
                    float da = (a.Position - sortCenter).sqrMagnitude;
                    float db = (b.Position - sortCenter).sqrMagnitude;
                    return da.CompareTo(db);
                });
                outBuffer.RemoveRange(cap, outBuffer.Count - cap);
            }
        }

        // ---------------------------------------------------------------------
        // Target-Type-Klassifikation (Source-Schema):
        //   *AreaSrc* → AoE am Caster
        //   *AreaDst* → AoE am Ziel-Punkt
        // ---------------------------------------------------------------------

        static bool IsAreaTargetType(SpellTargetType t)
            => t == SpellTargetType.UnitAreaSrcFriendly
            || t == SpellTargetType.UnitAreaSrcHostile
            || t == SpellTargetType.UnitAreaDstFriendly
            || t == SpellTargetType.UnitAreaDstHostile
            || t == SpellTargetType.UnitAreaDstFriendlyFromDst
            || t == SpellTargetType.UnitAreaDstHostileFromDst;

        static bool IsDstAreaTargetType(SpellTargetType t)
            => t == SpellTargetType.UnitAreaDstFriendly
            || t == SpellTargetType.UnitAreaDstHostile
            || t == SpellTargetType.UnitAreaDstFriendlyFromDst
            || t == SpellTargetType.UnitAreaDstHostileFromDst;

        static bool IsFriendlyAreaTargetType(SpellTargetType t)
            => t == SpellTargetType.UnitAreaSrcFriendly
            || t == SpellTargetType.UnitAreaDstFriendly
            || t == SpellTargetType.UnitAreaDstFriendlyFromDst;

        static bool IsHostileAreaTargetType(SpellTargetType t)
            => t == SpellTargetType.UnitAreaSrcHostile
            || t == SpellTargetType.UnitAreaDstHostile
            || t == SpellTargetType.UnitAreaDstHostileFromDst;

        static void ApplyEffect(
            ICombatUnit caster,
            SpellTemplate spell,
            ICombatUnit target,
            SpellTemplateEffect eff,
            ref int totalDamage,
            ref int totalHeal)
        {
            switch (eff.Effect)
            {
                case SpellEffect.SchoolDamage:
                case SpellEffect.WeaponDamage:
                {
                    int effValue = SpellUtils.CalculateEffectValue(eff, caster);
                    if (effValue <= 0) { return; }
                    IUnitStats attackerStats = caster.Stats;
                    IUnitStats victimStats = target.Stats;
                    if (attackerStats == null || victimStats == null) { return; }

                    // WeaponDamage = immer physikalisch (Armor-Reduktion, STR-Skalierung).
                    // SchoolDamage Physical = ebenfalls physikalisch. Alles andere = magisch.
                    bool isMagical = eff.Effect == SpellEffect.SchoolDamage
                                     && spell.MagicRollSchool != SpellSchool.Physical;
                    int resistValue = isMagical
                        ? ResolveResist(victimStats, spell.MagicRollSchool)
                        : 0;

                    DamageInfo info = CombatFormulas.CalculateSpellDamage(
                        attackerStats, victimStats, effValue, isMagical, resistValue);
                    if (info.FinalDamage <= 0) { return; }
                    target.TakeDamage(info.FinalDamage, caster);
                    totalDamage += info.FinalDamage;
                    break;
                }
                case SpellEffect.Heal:
                {
                    int effValue = SpellUtils.CalculateEffectValue(eff, caster);
                    if (effValue <= 0) { return; }
                    IUnitStats casterStats = caster.Stats;
                    if (casterStats == null) { return; }
                    int heal = CombatFormulas.CalculateSpellHeal(casterStats, effValue);
                    if (heal <= 0) { return; }
                    target.Heal(heal, caster);
                    totalHeal += heal;
                    break;
                }
                case SpellEffect.HealPct:
                {
                    int pct = SpellUtils.CalculateEffectValue(eff, caster);
                    if (pct <= 0 || target.MaxHealth <= 0) { return; }
                    int heal = (int)((long)target.MaxHealth * pct / 100);
                    if (heal <= 0) { return; }
                    target.Heal(heal, caster);
                    totalHeal += heal;
                    break;
                }
                case SpellEffect.RestoreMana:
                {
                    int amount = SpellUtils.CalculateEffectValue(eff, caster);
                    if (amount <= 0) { return; }
                    target.SetMana(Mathf.Min(target.MaxMana, target.Mana + amount));
                    break;
                }
                case SpellEffect.RestoreManaPct:
                {
                    int pct = SpellUtils.CalculateEffectValue(eff, caster);
                    if (pct <= 0 || target.MaxMana <= 0) { return; }
                    int amount = (int)((long)target.MaxMana * pct / 100);
                    target.SetMana(Mathf.Min(target.MaxMana, target.Mana + amount));
                    break;
                }
                case SpellEffect.ApplyAura:
                case SpellEffect.ApplyAreaAura:
                {
                    if (target.Auras == null) { return; }
                    target.Auras.ApplyAuraFromSpell(caster, spell, eff.Index);
                    break;
                }
                case SpellEffect.TriggerSpell:
                {
                    int triggeredEntry = (int)eff.Data1;
                    if (triggeredEntry <= 0) { return; }
                    SpellTemplate triggered = SpellCatalogLoader.GetTemplateOrNull(triggeredEntry);
                    if (triggered != null)
                    {
                        Execute(caster, triggered, target);
                    }
                    break;
                }
                default:
                    // Effekt nicht implementiert — wird kommentarlos uebersprungen,
                    // damit fehlende Handler keine Cast-Pipeline blockieren.
                    break;
            }
        }

        /// <summary>
        /// Liest die schul-spezifische Resistenz vom Opfer. Physical liefert 0,
        /// weil physischer Schaden ueber <c>Armor</c> mitigiert wird.
        /// </summary>
        static int ResolveResist(IUnitStats victim, SpellSchool school)
        {
            if (victim == null) { return 0; }
            return school switch
            {
                SpellSchool.Fire => victim.ResistFire,
                SpellSchool.Frost => victim.ResistFrost,
                SpellSchool.Arcane => victim.ResistArcane,
                SpellSchool.Nature => victim.ResistNature,
                SpellSchool.Shadow => victim.ResistShadow,
                SpellSchool.Holy => victim.ResistHoly,
                _ => 0,
            };
        }
    }
}
