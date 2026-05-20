using UnityEngine;

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

            // 4b) Aura-Modifier (ModifyDamageDealtPct / ModifyDamageReceivedPct).
            finalDamage = ApplyDamageAuraModifiers(finalDamage, attacker, victim);

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
        // Spell-Hit / Spell-Damage / Spell-Heal
        // ---------------------------------------------------------------------

        /// <summary>± Streuung um den Heal-Grundwert.</summary>
        public const float HealVariance = 0.05f;

        /// <summary>Konstante in der Resist-Reduktionsformel (analog Armor).</summary>
        public const float ResistConstant = 400f;

        /// <summary>Obergrenze fuer Resist-Schadensreduktion in Prozent.</summary>
        public const int MaxResistReductionPercent = 75;

        /// <summary>
        /// Reduziert magischen Schaden anhand des passenden Resist-Wertes und
        /// Opfer-Level. Spiegelt <see cref="ApplyArmorReduction"/>.
        /// </summary>
        public static int ApplyResistReduction(int damage, int resist, int level)
        {
            if (resist <= 0 || damage <= 0)
            {
                return damage;
            }
            float effectiveLevel = Mathf.Max(1, level);
            float reduction = resist / (resist + ResistConstant * effectiveLevel);
            float cap = MaxResistReductionPercent / 100f;
            if (reduction > cap)
            {
                reduction = cap;
            }
            return Mathf.RoundToInt(damage * (1f - reduction));
        }

        /// <summary>
        /// Wuerfelt das Hit-Ergebnis fuer einen Spell. Vereinfacht ggü. Melee:
        /// kein Dodge/Parry (Caster-Skills treffen normalerweise), aber Miss
        /// durch Level-Diff und Crit durch <c>attacker.CritChance</c>.
        /// </summary>
        public static HitResult RollSpellHit(IUnitStats attacker, IUnitStats victim)
        {
            int levelDiff = Mathf.Clamp(attacker.Level - victim.Level, -MaxLevelDiff, MaxLevelDiff);

            int hitChance = Mathf.Clamp(BaseHitChance + levelDiff, 5, 100);
            if (Roll100() >= hitChance)
            {
                return HitResult.Miss;
            }

            int critChance = Mathf.Clamp(BaseCritChance + attacker.CritChance + levelDiff, 0, 95);
            if (Roll100() < critChance)
            {
                return HitResult.Crit;
            }

            return HitResult.Hit;
        }

        /// <summary>
        /// Berechnet Spell-Schaden. <paramref name="effectValue"/> ist der bereits
        /// per <c>ScaleFormula</c> evaluierte Effekt-Wert (Source-Datentabelle).
        /// <paramref name="isMagical"/> entscheidet zwischen Intelligence-Bonus +
        /// Resist-Reduktion (magisch) und Strength-Bonus + Armor-Reduktion
        /// (physikalisch, z.B. <c>WeaponDamage</c>-Effekt). <paramref name="resistValue"/>
        /// ist die schul-spezifische Resistenz des Opfers (z.B. <c>ResistFire</c>).
        /// </summary>
        public static DamageInfo CalculateSpellDamage(
            IUnitStats attacker,
            IUnitStats victim,
            int effectValue,
            bool isMagical,
            int resistValue)
        {
            HitResult hit = RollSpellHit(attacker, victim);

            if (hit is HitResult.Miss or HitResult.Resist or HitResult.Immune)
            {
                return new DamageInfo
                {
                    HitResult = hit,
                    BaseDamage = effectValue,
                    FinalDamage = 0,
                    Absorbed = 0,
                };
            }

            // 1) Grundschaden = Effekt-Wert + Attribut-Bonus.
            int attributeBonus = isMagical
                ? (attacker.Intelligence / 20)
                : (attacker.Strength / 10) + attacker.WeaponDamage;
            int baseDamage = Mathf.Max(0, effectValue + attributeBonus);

            // 2) Variance (± DamageVariance).
            float varianceMul = 1f + Random.Range(-DamageVariance, DamageVariance);
            int afterVariance = Mathf.RoundToInt(baseDamage * varianceMul);

            // 3) Mitigation (Armor fuer phys, Resist fuer magisch).
            int afterMitigation = isMagical
                ? ApplyResistReduction(afterVariance, resistValue, victim.Level)
                : ApplyArmorReduction(afterVariance, victim.Armor, victim.Level);
            int absorbed = Mathf.Max(0, afterVariance - afterMitigation);

            // 4) Hit-Mods.
            int finalDamage = hit switch
            {
                HitResult.Crit => Mathf.RoundToInt(afterMitigation * CritMultiplier),
                HitResult.GlancingBlow => Mathf.RoundToInt(afterMitigation * GlancingMultiplier),
                HitResult.Block => Mathf.RoundToInt(afterMitigation * 0.5f),
                _ => afterMitigation,
            };

            // 4b) Aura-Modifier (ModifyDamageDealtPct / ModifyDamageReceivedPct).
            finalDamage = ApplyDamageAuraModifiers(finalDamage, attacker, victim);

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
        /// Berechnet Heal-Betrag. <paramref name="effectValue"/> ist der bereits
        /// per <c>ScaleFormula</c> evaluierte Effekt-Wert. Skaliert mit Willpower
        /// (haupt) und Intelligence (sekundaer), rollt Crit, addiert Variance.
        /// <paramref name="victim"/> wird nur fuer <c>ModifyHealingRecvPct</c>-Auren
        /// benoetigt — darf <c>null</c> sein (Self-Heal, Tests).
        /// Overheal-Cap erfolgt im Caller (<c>Heal</c>-Pfad).
        /// </summary>
        public static int CalculateSpellHeal(IUnitStats caster, IUnitStats victim, int effectValue)
        {
            if (effectValue <= 0) { return 0; }

            int attributeBonus = (caster.Willpower / 15) + (caster.Intelligence / 30);
            int baseHeal = Mathf.Max(0, effectValue + attributeBonus);

            float varianceMul = 1f + Random.Range(-HealVariance, HealVariance);
            int afterVariance = Mathf.RoundToInt(baseHeal * varianceMul);

            int critChance = Mathf.Clamp(BaseCritChance + caster.CritChance, 0, 95);
            if (Roll100() < critChance)
            {
                afterVariance = Mathf.RoundToInt(afterVariance * CritMultiplier);
            }

            // Aura-Modifier (ModifyHealingDealtPct / ModifyHealingRecvPct).
            afterVariance = ApplyHealingAuraModifiers(afterVariance, caster, victim);

            return Mathf.Max(0, afterVariance);
        }

        /// <summary>
        /// Wendet <c>ModifyDamageDealtPct</c> (Caster) und
        /// <c>ModifyDamageReceivedPct</c> (Opfer) multiplikativ an. Beide
        /// Modifier sind ganzzahlige Prozentwerte (z.B. <c>+25</c> = +25 %).
        /// Stacking ist <em>multiplikativ</em> (Caster-Mod × Opfer-Mod), wie
        /// im C++-Vorbild (<c>AuraSystem::getAuraModifier</c>).
        /// </summary>
        public static int ApplyDamageAuraModifiers(int damage, IUnitStats attacker, IUnitStats victim)
        {
            if (damage <= 0) { return damage; }
            float mul = 1f;
            if (attacker != null) { mul *= 1f + (attacker.DamageDealtPctMod / 100f); }
            if (victim != null) { mul *= 1f + (victim.DamageReceivedPctMod / 100f); }
            if (mul < 0f) { mul = 0f; }
            return Mathf.RoundToInt(damage * mul);
        }

        /// <summary>
        /// Wendet <c>ModifyHealingDealtPct</c> (Heiler) und
        /// <c>ModifyHealingRecvPct</c> (Ziel) multiplikativ an. Beide
        /// Modifier sind ganzzahlige Prozentwerte.
        /// </summary>
        public static int ApplyHealingAuraModifiers(int amount, IUnitStats caster, IUnitStats victim)
        {
            if (amount <= 0) { return amount; }
            float mul = 1f;
            if (caster != null) { mul *= 1f + (caster.HealingDealtPctMod / 100f); }
            if (victim != null) { mul *= 1f + (victim.HealingReceivedPctMod / 100f); }
            if (mul < 0f) { mul = 0f; }
            return Mathf.RoundToInt(amount * mul);
        }
    }
}
