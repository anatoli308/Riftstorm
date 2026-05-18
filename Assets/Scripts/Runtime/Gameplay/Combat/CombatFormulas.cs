using UnityEngine;
using Riftstorm.Gameplay.Combat.Spells;

namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Server-seitige Schadens- und Hit-Resolution-Formeln. Portiert aus dem
    /// C++-Vorbild (SoF/Steam-Server, <c>CombatFormulas::Config</c>) auf die
    /// Riftstorm-Datenstruktur — bewusst minimal gehalten (Phase 4 MVP).
    ///
    /// <para>
    /// Reine Static-Klasse, keine Allokationen pro Hit. Würfelt mit
    /// <see cref="UnityEngine.Random"/> (server-only, kein Determinismus
    /// erforderlich für Phase 4).
    /// </para>
    /// </summary>
    public static class CombatFormulas
    {
        // ---------------------------------------------------------------------
        // Config-Konstanten (1:1 aus C++ CombatFormulas::Config übernommen)
        // ---------------------------------------------------------------------

        /// <summary>Schaden-Multiplikator bei Crit (C++: <c>CRIT_MULTIPLIER</c>).</summary>
        public const float CritMultiplier = 2f;

        /// <summary>Schaden-Multiplikator bei Glancing Blow (C++: <c>GLANCING_MULTIPLIER</c>).</summary>
        public const float GlancingMultiplier = 0.7f;

        /// <summary>± Streuung um den Grundschaden (C++: <c>DAMAGE_VARIANCE</c>).</summary>
        public const float DamageVariance = 0.10f;

        /// <summary>Konstante in der Armor-Reduktionsformel (C++: <c>ARMOR_CONSTANT</c>).</summary>
        public const float ArmorConstant = 400f;

        /// <summary>Obergrenze für Armor-Schadensreduktion in Prozent.</summary>
        public const int MaxArmorReductionPercent = 75;

        /// <summary>Basis-Trefferchance in Prozent (C++: <c>BASE_HIT_CHANCE</c>).</summary>
        public const int BaseHitChance = 95;

        /// <summary>Basis-Crit-Chance in Prozent (C++: <c>BASE_CRIT_CHANCE</c>).</summary>
        public const int BaseCritChance = 5;

        /// <summary>Basis-Dodge-Chance in Prozent (C++: <c>BASE_DODGE_CHANCE</c>).</summary>
        public const int BaseDodgeChance = 5;

        /// <summary>Maximale Level-Differenz, die Hit/Crit/Resist beeinflusst.</summary>
        public const int MaxLevelDiff = 10;

        /// <summary>Schadens-Untergrenze (C++: <c>MIN_DAMAGE</c>).</summary>
        public const int MinDamage = 1;

        // ---------------------------------------------------------------------
        // Hit-Resolution
        // ---------------------------------------------------------------------

        /// <summary>
        /// Würfelt das Hit-Ergebnis für einen Melee-Angriff. Reihenfolge:
        /// Miss → Dodge → Crit → Hit (vereinfachte C++-Pipeline).
        /// </summary>
        public static HitResult RollMeleeHit(IUnitStats attacker, IUnitStats victim)
        {
            int levelDiff = Mathf.Clamp(attacker.Level - victim.Level, -MaxLevelDiff, MaxLevelDiff);

            // Angreifer mit höherem Level trifft häufiger (Cap ±10).
            int hitChance = Mathf.Clamp(BaseHitChance + levelDiff, 5, 100);
            if (Roll100() >= hitChance)
            {
                return HitResult.Miss;
            }

            // Opfer mit höherem Level dodged häufiger.
            int dodgeChance = Mathf.Clamp(BaseDodgeChance - levelDiff, 0, 95);
            if (Roll100() < dodgeChance)
            {
                return HitResult.Dodge;
            }

            // Angreifer mit höherem Level crittet häufiger.
            int critChance = Mathf.Clamp(BaseCritChance + levelDiff, 0, 95);
            if (Roll100() < critChance)
            {
                return HitResult.Crit;
            }

            return HitResult.Hit;
        }

        // ---------------------------------------------------------------------
        // Schaden
        // ---------------------------------------------------------------------

        /// <summary>
        /// Berechnet kompletten Melee-Schaden inkl. Hit-Roll, Strength-Skalierung,
        /// Variance, Armor-Reduktion und Crit-Bonus.
        /// </summary>
        public static DamageInfo CalculateMeleeDamage(IUnitStats attacker, IUnitStats victim, Riftstorm.Gameplay.Combat.WeaponDefinition weapon)
        {
            HitResult hit = RollMeleeHit(attacker, victim);

            // Bei Miss/Dodge/Parry/Resist/Immune → kein Schaden.
            if (hit is HitResult.Miss or HitResult.Dodge or HitResult.Parry or HitResult.Resist or HitResult.Immune)
            {
                return new DamageInfo
                {
                    HitResult = hit,
                    BaseDamage = 0,
                    FinalDamage = 0,
                    Absorbed = 0,
                };
            }

            // 1) Grundschaden: Waffe + halber Strength-Bonus (vereinfacht).
            int baseDamage = weapon.BaseDamage + (attacker.Strength / 2);

            // 2) Variance (± DamageVariance).
            float varianceMul = 1f + Random.Range(-DamageVariance, DamageVariance);
            int afterVariance = Mathf.RoundToInt(baseDamage * varianceMul);

            // 3) Armor-Reduktion.
            int afterArmor = ApplyArmorReduction(afterVariance, victim.Armor, victim.Level);
            int absorbedByArmor = Mathf.Max(0, afterVariance - afterArmor);

            // 4) Crit-/Glancing-Multiplier.
            int finalDamage = hit switch
            {
                HitResult.Crit => Mathf.RoundToInt(afterArmor * CritMultiplier),
                HitResult.GlancingBlow => Mathf.RoundToInt(afterArmor * GlancingMultiplier),
                HitResult.Block => Mathf.RoundToInt(afterArmor * 0.5f),
                _ => afterArmor,
            };

            // 5) Floor.
            finalDamage = Mathf.Max(MinDamage, finalDamage);

            return new DamageInfo
            {
                HitResult = hit,
                BaseDamage = baseDamage,
                FinalDamage = finalDamage,
                Absorbed = absorbedByArmor,
            };
        }

        /// <summary>
        /// Reduziert Schaden anhand Armor und Opfer-Level. Formel:
        /// <c>reduction = armor / (armor + ArmorConstant * level)</c>, capped auf
        /// <see cref="MaxArmorReductionPercent"/>.
        /// </summary>
        public static int ApplyArmorReduction(int damage, int armor, int level)
        {
            if (armor <= 0 || damage <= 0)
            {
                return damage;
            }
            float effectiveLevel = Mathf.Max(1, level);
            float reduction = armor / (armor + ArmorConstant * effectiveLevel);
            float cap = MaxArmorReductionPercent / 100f;
            if (reduction > cap)
            {
                reduction = cap;
            }
            return Mathf.RoundToInt(damage * (1f - reduction));
        }

        // ---------------------------------------------------------------------
        // Würfel-Helfer
        // ---------------------------------------------------------------------

        /// <summary>Würfelt einen Wert in [0,100). Server-only.</summary>
        public static int Roll100() => Random.Range(0, 100);

        /// <summary>True, wenn ein Roll-100 die Chance unterschreitet.</summary>
        public static bool RollChance(int percent) => Roll100() < percent;

        // ---------------------------------------------------------------------
        // Spell-Hit-Resolution (1:1 zu C++ CombatFormulas::rollToHit)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Würfelt das Hit-Ergebnis für einen Spell-Cast. Reihenfolge:
        /// Miss → Resist (nur magische Schools) → Crit → Hit. Heals/Buffs
        /// (positive Effekte) crit-rollen aber missen/resisten nicht.
        /// </summary>
        /// <param name="attacker">Caster-Stats.</param>
        /// <param name="victim">Ziel-Stats (für friendly Spells = Caster selbst).</param>
        /// <param name="school">Spell-School (Physical = Armor, sonst Resist).</param>
        /// <param name="isPositive">True für Heal/Buff — überspringt Miss/Resist.</param>
        public static HitResult RollSpellHit(IUnitStats attacker, IUnitStats victim, SpellSchool school, bool isPositive)
        {
            int levelDiff = Mathf.Clamp(attacker.Level - victim.Level, -MaxLevelDiff, MaxLevelDiff);

            if (!isPositive)
            {
                // Miss: Spells haben i. d. R. höhere Trefferchance als Melee (+4 wie C++).
                int hitChance = Mathf.Clamp(BaseHitChance + 4 + levelDiff, 5, 100);
                if (Roll100() >= hitChance)
                {
                    return HitResult.Miss;
                }

                // Resist nur für magische Schools — Physical kümmert sich Armor.
                if (school != SpellSchool.Physical && school != SpellSchool.None)
                {
                    int resistChance = Mathf.Clamp(2 - levelDiff, 0, 75);
                    if (Roll100() < resistChance)
                    {
                        return HitResult.Resist;
                    }
                }
            }

            // Crit gilt auch für Heals.
            int critChance = Mathf.Clamp(BaseCritChance + levelDiff, 0, 95);
            if (Roll100() < critChance)
            {
                return HitResult.Crit;
            }

            return HitResult.Hit;
        }

        // ---------------------------------------------------------------------
        // Spell-Damage (1:1 zu C++ CombatFormulas::calculateDamage)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Berechnet kompletten Spell-Schaden für einen einzelnen Effect-Slot
        /// inkl. Hit-Roll, Caster-Skalierung, Variance, School-Reduktion und Crit.
        /// </summary>
        /// <param name="attacker">Caster-Stats.</param>
        /// <param name="victim">Ziel-Stats.</param>
        /// <param name="spell">Spell-Definition (für School + Stat-Scaling).</param>
        /// <param name="effectIndex">Index in <see cref="SpellDefinition.Effects"/>.</param>
        public static DamageInfo CalculateSpellDamage(
            IUnitStats attacker,
            IUnitStats victim,
            SpellDefinition spell,
            int effectIndex)
        {
            if (spell == null || (uint)effectIndex >= (uint)spell.Effects.Count)
            {
                return default;
            }

            SpellEffectDefinition effDef = spell.Effects[effectIndex];

            HitResult hit = RollSpellHit(attacker, victim, spell.School, isPositive: false);

            if (hit is HitResult.Miss or HitResult.Resist or HitResult.Immune or HitResult.Dodge or HitResult.Parry)
            {
                return new DamageInfo
                {
                    HitResult = hit,
                    BaseDamage = 0,
                    FinalDamage = 0,
                    Absorbed = 0,
                };
            }

            // 1) Grundschaden aus Effect-Data1 + halbem Level-Bonus (vereinfachte Skalierung).
            int baseDamage = Mathf.Max(0, effDef.Data1) + (attacker.Level / 2);

            // 2) Variance.
            float varianceMul = 1f + Random.Range(-DamageVariance, DamageVariance);
            int afterVariance = Mathf.RoundToInt(baseDamage * varianceMul);

            // 3) School-spezifische Reduktion: Physical → Armor, magisch → Resist-Heuristik.
            int afterReduction;
            int absorbed;
            if (spell.School == SpellSchool.Physical)
            {
                afterReduction = ApplyArmorReduction(afterVariance, victim.Armor, victim.Level);
                absorbed = Mathf.Max(0, afterVariance - afterReduction);
            }
            else
            {
                afterReduction = ApplyResistReduction(afterVariance, victim, spell.School);
                absorbed = Mathf.Max(0, afterVariance - afterReduction);
            }

            // 4) Crit / Block.
            int finalDamage = hit switch
            {
                HitResult.Crit => Mathf.RoundToInt(afterReduction * CritMultiplier),
                HitResult.Block => Mathf.RoundToInt(afterReduction * 0.5f),
                HitResult.GlancingBlow => Mathf.RoundToInt(afterReduction * GlancingMultiplier),
                _ => afterReduction,
            };

            // 5) Floor.
            finalDamage = Mathf.Max(MinDamage, finalDamage);

            return new DamageInfo
            {
                HitResult = hit,
                BaseDamage = baseDamage,
                FinalDamage = finalDamage,
                Absorbed = absorbed,
            };
        }

        /// <summary>
        /// Reduziert magischen Schaden anhand eines simplen Level-basierten
        /// Resist-Modells (Phase-4-MVP — Resist-Stat pro School kann später in
        /// <see cref="IUnitStats"/> ergänzt werden, ohne Call-Sites zu brechen).
        /// </summary>
        /// <remarks>
        /// Im C++-Vorbild liest <c>calculateResistReduction</c> einen Resist-Wert
        /// aus den Victim-Variables. Solange Riftstorm noch keinen Resist-Stat
        /// trackt, gilt eine Pauschale, die Mob-Level über Caster-Level berücksichtigt.
        /// </remarks>
        public static int ApplyResistReduction(int damage, IUnitStats victim, SpellSchool school)
        {
            if (damage <= 0 || school == SpellSchool.None || school == SpellSchool.Physical)
            {
                return damage;
            }
            // Pauschal ~10 % bei gleichem Level, ±2 % pro Levelschritt.
            int basePct = 10 + Mathf.Clamp(victim.Level / 5, 0, 20);
            int reductionPct = Mathf.Clamp(basePct, 0, MaxArmorReductionPercent);
            return Mathf.RoundToInt(damage * (1f - reductionPct / 100f));
        }

        // ---------------------------------------------------------------------
        // Heal (1:1 zu C++ CombatFormulas::calculateHeal)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Berechnet den Heilwert für einen einzelnen Effect-Slot. Heals können
        /// crit-en, missen aber nicht und werden nicht durch Armor/Resist reduziert.
        /// </summary>
        /// <param name="healer">Caster-Stats.</param>
        /// <param name="target">Ziel-Stats.</param>
        /// <param name="spell">Spell-Definition.</param>
        /// <param name="effectIndex">Index in <see cref="SpellDefinition.Effects"/>.</param>
        public static HealInfo CalculateHeal(
            IUnitStats healer,
            IUnitStats target,
            SpellDefinition spell,
            int effectIndex)
        {
            if (spell == null || (uint)effectIndex >= (uint)spell.Effects.Count)
            {
                return default;
            }

            SpellEffectDefinition effDef = spell.Effects[effectIndex];
            HitResult hit = RollSpellHit(healer, target, spell.School, isPositive: true);

            // 1) Grund-Heal je nach Effect-Type.
            int baseHeal;
            if (effDef.Type == SpellEffectType.HealPct)
            {
                // Data1 = Prozent vom Ziel-MaxHp.
                baseHeal = Mathf.Max(0, (target.MaxHp * effDef.Data1) / 100);
            }
            else
            {
                baseHeal = Mathf.Max(0, effDef.Data1) + (healer.Level / 2);
            }

            // 2) Variance.
            float varianceMul = 1f + Random.Range(-DamageVariance, DamageVariance);
            int afterVariance = Mathf.RoundToInt(baseHeal * varianceMul);

            // 3) Crit-Multiplier.
            int finalHeal = hit == HitResult.Crit
                ? Mathf.RoundToInt(afterVariance * CritMultiplier)
                : afterVariance;

            // 4) Overheal-Cap.
            int missingHp = Mathf.Max(0, target.MaxHp - target.CurrentHp);
            int applied = Mathf.Min(finalHeal, missingHp);
            int overheal = finalHeal - applied;
            bool fullHeal = (target.CurrentHp + applied) >= target.MaxHp;

            return new HealInfo
            {
                HitResult = hit,
                BaseHeal = baseHeal,
                FinalHeal = applied,
                Overheal = overheal,
                FullHeal = fullHeal,
            };
        }
    }
}
