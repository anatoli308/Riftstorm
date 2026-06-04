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

        /// <summary>Basis-Parry-Chance in Prozent (C++: <c>BASE_PARRY_CHANCE</c>,
        /// <c>CombatFormulas.h</c> L95). Wird in <see cref="GetParryChance"/>
        /// nur addiert, wenn der Verteidiger überhaupt eine Waffe trägt.</summary>
        public const int BaseParryChance = 5;

        /// <summary>Cap fuer Parry- bzw. Block-Chance (Source: <c>clamp(0, 75)</c>).</summary>
        public const int MaxAvoidanceChance = 75;

        /// <summary>Maximale Level-Differenz, die Hit/Crit/Resist beeinflusst.</summary>
        public const int MaxLevelDiff = 10;

        /// <summary>
        /// Skill-Punkte pro 1 % Trefferchance-Bonus (Riftstorm-Erweiterung). Die
        /// NPC-Templates fuehren <c>melee_skill</c>/<c>ranged_skill</c> (5..125), die
        /// im Original-Server zwar geladen, aber nie in die Hit-Formel verrechnet
        /// wurden (<c>// TODO: Add hit rating</c>). Mit Faktor 25 ergibt sich ein
        /// moderater Bonus von 0..5 %, der exakt in den Headroom bis zum 100 %-Cap
        /// passt und vor allem den Level-Malus gegen hoehere Ziele abfedert.
        /// </summary>
        public const int SkillPerHitPercent = 25;

        /// <summary>Schadens-Untergrenze (C++: <c>MIN_DAMAGE</c>).</summary>
        public const int MinDamage = 1;

        // ---------------------------------------------------------------------
        // Hit-Resolution
        // ---------------------------------------------------------------------

        /// <summary>
        /// Würfelt das Hit-Ergebnis für einen Melee-Angriff. Reihenfolge:
        /// Miss → Dodge → Parry → Block → Crit → Hit. Spiegelt die Source-
        /// Pipeline aus <c>CombatFormulas.cpp</c> (<c>getHitResult</c>) wider.
        /// </summary>
        /// <param name="attacker">Angreifer.</param>
        /// <param name="victim">Verteidiger.</param>
        /// <param name="attackerHitBonus">
        /// Zusaetzlicher Trefferchance-Bonus in Prozent (Riftstorm-Erweiterung,
        /// z. B. aus NPC-<c>melee_skill</c>/<c>ranged_skill</c> via
        /// <see cref="SkillPerHitPercent"/>). Spieler-Pfad uebergibt 0.
        /// </param>
        public static HitResult RollMeleeHit(IUnitStats attacker, IUnitStats victim, int attackerHitBonus = 0)
        {
            int levelDiff = Mathf.Clamp(attacker.Level - victim.Level, -MaxLevelDiff, MaxLevelDiff);

            // Angreifer mit höherem Level trifft häufiger (Cap ±10). Skill-Bonus
            // (NPC) erhoeht die Trefferchance additiv, bleibt aber durch den
            // 100 %-Cap begrenzt.
            int hitChance = Mathf.Clamp(BaseHitChance + levelDiff + Mathf.Max(0, attackerHitBonus), 5, 100);
            if (Roll100() >= hitChance)
            {
                return HitResult.Miss;
            }

            // Opfer mit höherem Level dodged häufiger. Zusätzlich AGI/20 (Original-Formel
            // aus CombatFormulas.cpp L149-150: 1 % Dodge pro 20 Agility) und der Gear-/Talent-
            // DodgeRating-Wert. Cap bei 75 % wie im Source (clamp(0, 75)).
            int dodgeChance = Mathf.Clamp(
                BaseDodgeChance - levelDiff + (victim.Agility / 20) + victim.DodgeChance,
                0,
                MaxAvoidanceChance);
            if (Roll100() < dodgeChance)
            {
                return HitResult.Dodge;
            }

            // Parry erfordert eine Waffe (Source: Player-Pfad checkt hasWeaponEquipped,
            // NPC-Pfad lässt es zu). <see cref="GetParryChance"/> kapselt Base+Rating+Bonus+
            // CRG/30 und Cap 75 %. Liefert 0, wenn keine Waffe vorhanden.
            int parryChance = GetParryChance(victim, victim.HasWeapon);
            if (parryChance > 0 && Roll100() < parryChance)
            {
                return HitResult.Parry;
            }

            // Block erfordert ein Schild — siehe Source <c>hasShieldEquipped</c> bzw.
            // <c>ShieldSkill > 0</c> für NPCs. Aggregat aus BlockRating+Bonus+SLD/5+FRT/30,
            // Cap 75 %. Block setzt den Angriff nicht ab, reduziert ihn aber in
            // <see cref="CalculateMeleeDamage"/> (Block-Anteil).
            int blockChance = GetBlockChance(victim, victim.HasShield);
            if (blockChance > 0 && Roll100() < blockChance)
            {
                return HitResult.Block;
            }

            // Angreifer mit höherem Level crittet häufiger; Melee-Schul-Crit additiv.
            int critChance = Mathf.Clamp(BaseCritChance + attacker.MeleeCritChance + levelDiff, 0, 95);
            if (Roll100() < critChance)
            {
                return HitResult.Crit;
            }

            return HitResult.Hit;
        }

        // ---------------------------------------------------------------------
        // Parry/Block — Avoidance-Helpers (Phase 16D, Source-faithful + FRT/CRG)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Aggregierte Parry-Chance fuer einen Verteidiger. Source-Pipeline aus
        /// <c>CombatFormulas.cpp</c> L162-184 (<c>getParryChance</c>):
        /// <c>BASE_PARRY_CHANCE + ParryRating + ParryChanceBonus</c>, geclamped auf 0..75.
        /// Riftstorm-Erweiterung: zusaetzlich <c>CRG/30 %</c>, damit das Courage-
        /// Attribut Combat-relevant wird.
        /// </summary>
        /// <param name="victim">Der potentielle Parrier.</param>
        /// <param name="hasWeapon">
        /// Ob der Verteidiger eine Waffe traegt. Falls <c>false</c>, liefert die
        /// Methode 0 (Source: Player-Pfad checkt <c>hasWeaponEquipped</c>; NPCs
        /// rufen die Methode nur mit <c>true</c> auf).
        /// </param>
        /// <returns>Parry-Chance in Prozent, 0..75.</returns>
        public static int GetParryChance(IUnitStats victim, bool hasWeapon)
        {
            if (victim == null || !hasWeapon) { return 0; }
            int raw = BaseParryChance
                      + victim.ParryRating
                      + victim.ParryChanceBonus
                      + (victim.Courage / 30);
            return Mathf.Clamp(raw, 0, MaxAvoidanceChance);
        }

        /// <summary>
        /// Aggregierte Block-Chance fuer einen Verteidiger. Source-Pipeline aus
        /// <c>CombatFormulas.cpp</c> L186-222 (<c>getBlockChance</c>):
        /// <c>BlockRating + BlockChanceBonus + ShieldSkill/5</c>, geclamped auf 0..75.
        /// Source hat <em>kein</em> Base-Block — Schild ist Pflicht (Player:
        /// <c>hasShieldEquipped</c>, NPC: <c>ShieldSkill > 0</c>). Riftstorm-
        /// Erweiterung: <c>FRT/30 %</c>, damit Fortitude jenseits des HP-Pools
        /// einen Defense-Nutzen hat.
        /// </summary>
        /// <param name="victim">Der potentielle Blocker.</param>
        /// <param name="hasShield">Ob ein Schild verfuegbar ist.</param>
        /// <returns>Block-Chance in Prozent, 0..75.</returns>
        public static int GetBlockChance(IUnitStats victim, bool hasShield)
        {
            if (victim == null || !hasShield) { return 0; }
            int raw = victim.BlockRating
                      + victim.BlockChanceBonus
                      + (victim.ShieldSkill / 5)
                      + (victim.Fortitude / 30);
            return Mathf.Clamp(raw, 0, MaxAvoidanceChance);
        }

        // ---------------------------------------------------------------------
        // Schaden
        // ---------------------------------------------------------------------

        /// <summary>
        /// Berechnet kompletten Melee-Schaden inkl. Hit-Roll, Strength-Skalierung,
        /// Variance, Armor-Reduktion und Crit-Bonus.
        /// </summary>
        /// <param name="attacker">Angreifer.</param>
        /// <param name="victim">Verteidiger.</param>
        /// <param name="weapon">Transiente Waffen-Definition (Base-Damage/Range).</param>
        /// <param name="attackerHitBonus">
        /// Trefferchance-Bonus in Prozent (NPC-Skill, Riftstorm-Erweiterung).
        /// Spieler-Pfad uebergibt 0.
        /// </param>
        public static DamageInfo CalculateMeleeDamage(IUnitStats attacker, IUnitStats victim, Riftstorm.Gameplay.Combat.WeaponDefinition weapon, int attackerHitBonus = 0)
        {
            HitResult hit = RollMeleeHit(attacker, victim, attackerHitBonus);

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

            // 1) Grundschaden: Waffe + WeaponDamage-Bonus aus Item-Stats
            //    (Original 'WeaponValue' / StatId.WeaponValue, Phase 16C) +
            //    halber Strength-Bonus (vereinfachte Source-Parity-Formel).
            //    <see cref="IUnitStats.WeaponDamage"/> wird bei Spielern aus
            //    <c>PlayerStats.GetTotal(StatId.WeaponValue)</c> aggregiert
            //    (UnitStats-Routing) — damit fliessen Waffen- und Ruestungs-
            //    Item-Boni hier in den Melee-Schaden.
            int baseDamage = weapon.BaseDamage + attacker.WeaponDamage + (attacker.Strength / 2);

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
        /// durch Level-Diff und Crit durch <c>attacker.SpellCritChance</c>.
        /// </summary>
        public static HitResult RollSpellHit(IUnitStats attacker, IUnitStats victim)
        {
            int levelDiff = Mathf.Clamp(attacker.Level - victim.Level, -MaxLevelDiff, MaxLevelDiff);

            int hitChance = Mathf.Clamp(BaseHitChance + levelDiff, 5, 100);
            if (Roll100() >= hitChance)
            {
                return HitResult.Miss;
            }

            int critChance = Mathf.Clamp(BaseCritChance + attacker.SpellCritChance + levelDiff, 0, 95);
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
        /// <paramref name="weaponPercent"/> aktiviert die FLARE-kanonische
        /// <c>WeaponDamage</c>-Semantik: <paramref name="effectValue"/> ist dann
        /// ein <b>Prozent-Multiplier</b> auf den effektiven Waffenschaden
        /// (<c>weapon.BaseDamage + WeaponValue-Stat</c>), nicht ein flacher Bonus.
        /// <paramref name="useRangedWeapon"/> entscheidet, ob die Ranged-Waffe
        /// (Bow/Crossbow/Gun) oder die Melee-Waffe zur Skalierung herangezogen
        /// wird (Aimed Shot vs. Sinister Strike); der Caller setzt das anhand
        /// der equippten Waffe des Casters.
        /// <paramref name="useMeleeAvoidance"/> schaltet die physische
        /// Ausweich-Mechanik (Dodge/Parry/Block, <see cref="RollMeleeHit"/>)
        /// statt des reinen Spell-Rolls (<see cref="RollSpellHit"/>) ein. Wird
        /// von Auto-Attack-Effekten (<see cref="SpellEffect.MeleeAtk"/>/
        /// <see cref="SpellEffect.RangedAtk"/>, Spell 81/82) gesetzt, damit
        /// sowohl Spieler- als auch NPC-Auto-Attacks ausweichbar sind — exakt
        /// wie der direkte Waffenschlag im Source (<c>getHitResult</c>).
        /// <paramref name="attackerHitBonus"/> ist der Skill-/Treffer-Bonus, der
        /// nur im Avoidance-Pfad an <see cref="RollMeleeHit"/> durchgereicht wird.
        /// </summary>
        public static DamageInfo CalculateSpellDamage(
            IUnitStats attacker,
            IUnitStats victim,
            int effectValue,
            bool isMagical,
            int resistValue,
            bool weaponPercent = false,
            bool useRangedWeapon = false,
            bool useMeleeAvoidance = false,
            int attackerHitBonus = 0)
        {
            HitResult hit = useMeleeAvoidance
                ? RollMeleeHit(attacker, victim, attackerHitBonus)
                : RollSpellHit(attacker, victim);

            if (hit is HitResult.Miss or HitResult.Resist or HitResult.Immune
                or HitResult.Dodge or HitResult.Parry)
            {
                return new DamageInfo
                {
                    HitResult = hit,
                    BaseDamage = effectValue,
                    FinalDamage = 0,
                    Absorbed = 0,
                };
            }

            // 1) Grundschaden.
            //    a) WeaponDamage-Spell (weaponPercent=true): effectValue ist %
            //       auf den effektiven Waffenschaden (BaseDamage des Items +
            //       WeaponValue/RangedWeaponValue-Stat), plus halbierter
            //       STR-Bonus (Skill-Anteil).
            //    b) Magic-Spell:        flat effectValue + INT/20.
            //    c) Phys-Flat-Spell:    flat effectValue + STR/10 + Waffenschaden.
            int baseDamage;
            if (weaponPercent && !isMagical)
            {
                int weaponBase = useRangedWeapon ? attacker.BaseRangedWeaponDamage : attacker.BaseWeaponDamage;
                int weaponStat = useRangedWeapon ? attacker.RangedWeaponDamage : attacker.WeaponDamage;
                int totalWeapon = weaponBase + weaponStat;
                int weaponContribution = (int)((long)totalWeapon * effectValue / 100);
                // Skill-Bonus: Melee skaliert mit STR/10 (Original CombatFormulas.cpp L417),
                // Ranged mit AGI/14 (Riftstorm-Erweiterung, Classic-Hunter-RAP-Faktor).
                // Im Source-Original gab es keinen Ranged-eigenen Stat-Pfad — Bogenschuetzen
                // skalierten ueber dieselbe STR-Formel. Wir trennen das hier bewusst, damit
                // AGI-Builds mechanisch sinnvoll sind.
                int skillBonus = useRangedWeapon ? (attacker.Agility / 14) : (attacker.Strength / 10);
                baseDamage = Mathf.Max(0, weaponContribution + skillBonus);
            }
            else
            {
                int attributeBonus = isMagical
                    ? (attacker.Intelligence / 20)
                    : (attacker.Strength / 10) + attacker.WeaponDamage;
                baseDamage = Mathf.Max(0, effectValue + attributeBonus);
            }

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
        /// (haupt) und Intelligence (sekundaer), rollt Crit
        /// (<c>SpellCritChance</c> inkl. <c>INT/30</c> + <c>WIL/40</c> aus diesem
        /// Heal-Pfad — Heal-only, damit WIL nicht auf Spell-Damage-Crit leakt),
        /// addiert Variance. <paramref name="victim"/> wird nur fuer
        /// <c>ModifyHealingRecvPct</c>-Auren benoetigt — darf <c>null</c> sein
        /// (Self-Heal, Tests). Overheal-Cap erfolgt im Caller (<c>Heal</c>-Pfad).
        /// </summary>
        public static int CalculateSpellHeal(IUnitStats caster, IUnitStats victim, int effectValue)
        {
            if (effectValue <= 0) { return 0; }

            int attributeBonus = (caster.Willpower / 15) + (caster.Intelligence / 30);
            int baseHeal = Mathf.Max(0, effectValue + attributeBonus);

            float varianceMul = 1f + Random.Range(-HealVariance, HealVariance);
            int afterVariance = Mathf.RoundToInt(baseHeal * varianceMul);

            int critChance = Mathf.Clamp(BaseCritChance + caster.SpellCritChance + (caster.Willpower / 40), 0, 95);
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
