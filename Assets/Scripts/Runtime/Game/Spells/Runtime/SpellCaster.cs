using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Server-autoritative Cast-Validierung. Stateless. Vor jedem
    /// <see cref="SpellExecutor.Execute"/> aufrufen.
    /// </summary>
    /// <remarks>
    /// Reihenfolge der Checks entspricht dem Source-Server: Caster-State
    /// (lebt, nicht stunned, nicht silenced) → Resources (Mana, Cooldown, GCD)
    /// → Equipment → Target (typ, lebendig, friendly/hostile) → Range.
    /// </remarks>
    public static class SpellCaster
    {
        /// <summary>Validiert einen Cast vollstaendig.</summary>
        public static CastResult Validate(ICombatUnit caster, SpellTemplate spell, ICombatUnit target)
        {
            if (caster == null) { return CastResult.InternalError; }
            if (spell == null) { return CastResult.UnknownSpell; }

            CastResult r = CheckCasterState(caster, spell);
            if (r != CastResult.Success) { return r; }

            r = CheckResources(caster, spell);
            if (r != CastResult.Success) { return r; }

            r = CheckEquipment(caster, spell);
            if (r != CastResult.Success) { return r; }

            r = CheckRangedAgainstDeadTarget(spell, target);
            if (r != CastResult.Success) { return r; }

            r = CheckTarget(caster, spell, target);
            if (r != CastResult.Success) { return r; }

            r = CheckRange(caster, spell, target);
            if (r != CastResult.Success) { return r; }

            return CastResult.Success;
        }

        // ---------------------------------------------------------------------

        static CastResult CheckCasterState(ICombatUnit caster, SpellTemplate spell)
        {
            if (caster.IsDead) { return CastResult.CasterDead; }

            bool ignoreStun = (spell.Attributes & SpellAttributes.IgnoreStun) != 0;
            if (caster.IsStunned && !ignoreStun) { return CastResult.CasterStunned; }

            if (caster.IsSilenced) { return CastResult.CasterSilenced; }
            return CastResult.Success;
        }

        static CastResult CheckResources(ICombatUnit caster, SpellTemplate spell)
        {
            // Mana
            int manaCost = SpellUtils.CalculateManaCost(spell, caster);
            if (manaCost > 0 && caster.Mana < manaCost) { return CastResult.NotEnoughMana; }

            // HP
            int hpCost = spell.HealthCost;
            if (spell.HealthPctCost > 0f && caster.MaxHealth > 0)
            {
                hpCost += Mathf.RoundToInt(caster.MaxHealth * spell.HealthPctCost);
            }
            if (hpCost > 0 && caster.Health <= hpCost) { return CastResult.NotEnoughHealth; }

            // Cooldowns nur fuer Players (Mob-AI ignoriert CDs)
            if (caster.IsPlayer && caster.Cooldowns != null)
            {
                if (caster.Cooldowns.IsOnCooldown(spell.Entry)) { return CastResult.OnCooldown; }
                if (caster.Cooldowns.IsOnGcd()) { return CastResult.OnGlobalCooldown; }
                if (spell.CooldownCategory != 0
                    && caster.Cooldowns.IsCategoryOnCooldown(spell.CooldownCategory))
                {
                    return CastResult.OnCooldown;
                }
            }
            return CastResult.Success;
        }

        /// <summary>
        /// Validiert die ausgeruestete Waffe gegen <see cref="SpellTemplate.RequiredEquipment"/>.
        /// Werte aus <c>_templates.json</c> (FLARE/MaNGOS-Konvention):
        /// <list type="bullet">
        ///   <item><description><c>0</c> — keine Anforderung.</description></item>
        ///   <item><description><c>10</c> — Melee-Waffe (Sinister Strike, Blindside, ...).</description></item>
        ///   <item><description><c>11</c> — Schild (Shield Block, Aegis of Valor) — aktuell nicht modelliert, durchgewunken.</description></item>
        ///   <item><description><c>12</c> — Ranged-Waffe (Aimed Shot, Multi-Shot, ...).</description></item>
        /// </list>
        /// Detektion ueber <see cref="IUnitStats.BaseWeaponDamage"/> / <see cref="IUnitStats.BaseRangedWeaponDamage"/>:
        /// <c>UnitStats</c> liefert hier den BaseDamage der aktuell equippten Waffe
        /// nur dann zurueck, wenn die Waffenkategorie passt (siehe Fix Round 1).
        /// Damit erkennt der Check sowohl "gar keine Waffe" als auch "falsche
        /// Kategorie" (z. B. Aimed Shot mit blossen Faeusten — Unarmed-Fallback
        /// liefert BaseWeaponDamage&gt;0 aber BaseRangedWeaponDamage=0).
        /// </summary>
        static CastResult CheckEquipment(ICombatUnit caster, SpellTemplate spell)
        {
            long req = spell.RequiredEquipment;
            if (req == 0L) { return CastResult.Success; }

            IUnitStats stats = caster.Stats;
            if (stats == null) { return CastResult.Success; }

            switch (req)
            {
                case 10L:
                    if (stats.BaseWeaponDamage <= 0) { return CastResult.NoMeleeWeapon; }
                    break;
                case 12L:
                    if (stats.BaseRangedWeaponDamage <= 0) { return CastResult.NoRangedWeapon; }
                    break;
                // 11 = Shield: noch nicht modelliert (kein OffHand-Stat), bewusst durchgewunken.
                default:
                    break;
            }
            return CastResult.Success;
        }

        /// <summary>
        /// Harter Guard fuer Ranged-Spells (Aimed Shot / Multi-Shot / ...) auf
        /// tote Ziele: ein Bogenschuss auf eine Leiche ergibt nie Sinn,
        /// unabhaengig vom <see cref="SpellAttributes.CanTargetDead"/>-Flag in
        /// den Source-Daten. Der allgemeine <c>CheckTarget</c>-Pfad blockt das
        /// zwar schon \u2014 aber nur, wenn das Flag fehlt; <c>CanTargetDead</c>
        /// existiert fuer Effekte wie Resurrect/Loot und darf bei Ranged nicht
        /// versehentlich Schusswaffen freischalten.
        /// </summary>
        static CastResult CheckRangedAgainstDeadTarget(SpellTemplate spell, ICombatUnit target)
        {
            if (spell.RequiredEquipment != 12L) { return CastResult.Success; }
            if (target == null) { return CastResult.Success; }
            if (target.IsDead) { return CastResult.TargetDead; }
            return CastResult.Success;
        }

        static CastResult CheckTarget(ICombatUnit caster, SpellTemplate spell, ICombatUnit target)
        {
            bool selfOnly = SpellUtils.IsSelfOnly(spell);
            if (selfOnly)
            {
                return CastResult.Success;
            }

            bool requiresTarget = SpellUtils.RequiresTarget(spell);
            if (requiresTarget && (target == null || ReferenceEquals(target, caster)
                                   && (spell.Attributes & SpellAttributes.CantTargetSelf) != 0))
            {
                return CastResult.NoTarget;
            }
            if (target == null) { return CastResult.Success; }

            bool targetIsCaster = ReferenceEquals(target, caster);
            bool canFriendly = SpellUtils.CanTargetFriendly(spell);
            bool canHostile = SpellUtils.CanTargetHostile(spell);

            // Self ist nur erlaubt wenn der Spell explizit Friendly-Targets traegt
            // (z.B. Heal/Buff) oder als Self-Only deklariert ist. Hostile-only Spells
            // (Fireball, Snare, etc.) duerfen NIE auf den Caster selbst landen,
            // auch wenn das CantTargetSelf-Attribute in den Source-Daten fehlt.
            if (targetIsCaster)
            {
                if ((spell.Attributes & SpellAttributes.CantTargetSelf) != 0)
                {
                    return CastResult.TargetSelf;
                }
                if (!canFriendly)
                {
                    return CastResult.TargetSelf;
                }
            }

            bool targetDead = target.IsDead;
            bool allowDead = (spell.Attributes & SpellAttributes.CanTargetDead) != 0;
            if (targetDead && !allowDead) { return CastResult.TargetDead; }

            bool sameFaction = caster.FactionId == target.FactionId;
            if (sameFaction && !canFriendly && !targetIsCaster) { return CastResult.TargetFriendly; }
            if (!sameFaction && !canHostile) { return CastResult.TargetHostile; }
            return CastResult.Success;
        }

        static CastResult CheckRange(ICombatUnit caster, SpellTemplate spell, ICombatUnit target)
        {
            if (spell.Range <= 0f || target == null || ReferenceEquals(target, caster))
            {
                return CastResult.Success;
            }
            float maxMeters = SpellUtils.RangeToMeters(spell.Range);
            float minMeters = SpellUtils.RangeToMeters(spell.RangeMin);
            float distance = Vector3.Distance(caster.Position, target.Position);
            if (distance > maxMeters) { return CastResult.OutOfRange; }
            if (minMeters > 0f && distance < minMeters) { return CastResult.TooClose; }
            return CastResult.Success;
        }
    }
}
