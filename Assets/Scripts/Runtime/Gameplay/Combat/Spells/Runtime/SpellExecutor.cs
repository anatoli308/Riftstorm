using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Ergebnis eines kompletten Cast-Versuchs. Enthält den Validation-Ausgang
    /// und (bei Erfolg) die Anzahl tatsächlich getroffener Effekte. Pro-Target-
    /// Details bleiben fürs MVP der Telemetrie auf dem Server überlassen.
    /// </summary>
    public struct SpellExecutionResult
    {
        /// <summary>Ergebnis der Validierung. <see cref="CastResult.Success"/> bedeutet "Cast wurde ausgeführt".</summary>
        public CastResult Result;
        /// <summary>Wie viele Effekt-Slots tatsächlich gegen ein Ziel aufgelöst wurden.</summary>
        public int EffectsApplied;
        /// <summary>Mana, das vom Caster abgezogen wurde.</summary>
        public int ManaSpent;
        /// <summary>HP-Kosten, die der Caster gezahlt hat.</summary>
        public int HealthSpent;
    }

    /// <summary>
    /// Server-only Orchestrator für einen kompletten Spell-Cast. Validiert,
    /// zieht Ressourcen, startet Cooldowns/GCD und führt jeden Effekt-Slot aus.
    ///
    /// <para>
    /// Mirror zu <c>source_server/Server/src/Combat/SpellCaster.cpp::executeCast</c>
    /// — die Validation lebt weiterhin in <see cref="SpellCaster.Validate"/>,
    /// dieser Executor ist die Pipeline danach.
    /// </para>
    /// </summary>
    public static class SpellExecutor
    {
        /// <summary>
        /// Führt einen Cast komplett aus (Validate → Resources → Cooldown → Effects).
        /// Reine Server-Logik — niemals vom Client aus aufrufen.
        /// </summary>
        /// <param name="caster">Caster (Player oder Mob).</param>
        /// <param name="spell">Spell-Definition aus dem <see cref="SpellCatalog"/>.</param>
        /// <param name="primaryTarget">Vom Client gewähltes Primärziel (kann null sein).</param>
        /// <param name="auraCatalog">Aktiver <see cref="AuraCatalog"/> für ApplyAura-Effekte.</param>
        public static SpellExecutionResult Execute(
            ICombatUnit caster,
            SpellDefinition spell,
            ICombatUnit primaryTarget,
            AuraCatalog auraCatalog)
        {
            // 1) Validate — Reuse vorhandener Pipeline.
            CastResult validation = SpellCaster.Validate(caster, spell, primaryTarget);
            if (validation != CastResult.Success)
            {
                return new SpellExecutionResult { Result = validation };
            }

            // 2) Mana abziehen.
            int manaCost = SpellUtils.CalculateManaCost(spell, caster.Level, caster.MaxMana);
            if (manaCost > 0)
            {
                caster.SetMana(caster.Mana - manaCost);
            }

            // 3) HP-Kosten (z. B. Life Tap). Caster greift sich selbst an — kein Attacker übergeben,
            // damit keine Threat erzeugt wird.
            int healthCost = spell.HealthCost;
            if (spell.HealthCostPct > 0 && caster.MaxHealth > 0)
            {
                healthCost += (caster.MaxHealth * spell.HealthCostPct) / 100;
            }
            if (healthCost > 0)
            {
                caster.TakeDamage(healthCost, null);
            }

            // 4) Cooldown + GCD — analog zu SpellCaster.CheckResources nur für Player.
            if (caster.IsPlayer && caster.Cooldowns != null)
            {
                if (spell.CooldownMs > 0)
                {
                    caster.Cooldowns.StartCooldown(spell.Id, spell.CooldownMs, spell.CooldownCategory);
                }
                if ((spell.Attributes & SpellAttributes.IgnoreGcd) == 0)
                {
                    caster.Cooldowns.StartGcd();
                }
            }

            // 5) Effekt-Loop — jeder Slot wird einzeln aufgelöst.
            int effectsApplied = 0;
            for (int i = 0; i < spell.Effects.Count; i++)
            {
                SpellEffectDefinition effDef = spell.Effects[i];
                if (effDef.Type == SpellEffectType.None) { continue; }

                ICombatUnit effTarget = ResolveEffectTarget(caster, primaryTarget, effDef);
                if (effTarget == null) { continue; }

                if (ApplyEffect(caster, effTarget, spell, i, effDef, auraCatalog))
                {
                    effectsApplied++;
                }
            }

            return new SpellExecutionResult
            {
                Result = CastResult.Success,
                EffectsApplied = effectsApplied,
                ManaSpent = manaCost,
                HealthSpent = healthCost,
            };
        }

        // ---------------------------------------------------------------------
        // Target-Resolution
        // ---------------------------------------------------------------------

        /// <summary>
        /// Mappt <see cref="SpellEffectDefinition.TargetType"/> auf eine konkrete
        /// Ziel-Unit. AoE-Slots geben aktuell das Primary-Target zurück — der
        /// AoE-Sweep über alle Units in Radius landet in einer späteren Iteration
        /// (braucht den Combat-Broadphase-Index).
        /// </summary>
        static ICombatUnit ResolveEffectTarget(
            ICombatUnit caster,
            ICombatUnit primaryTarget,
            SpellEffectDefinition effDef)
        {
            switch (effDef.TargetType)
            {
                case SpellTargetType.SelfCaster:
                    return caster;

                case SpellTargetType.FriendlyUnit:
                case SpellTargetType.HostileUnit:
                case SpellTargetType.AnyUnit:
                case SpellTargetType.GameObject:
                    return primaryTarget;

                // AoE — Platzhalter: nur das Primary-Target wird getroffen, bis
                // der Broadphase-Index live ist.
                case SpellTargetType.AreaSrcFriendly:
                case SpellTargetType.AreaSrcHostile:
                    return caster;
                case SpellTargetType.AreaDstFriendly:
                case SpellTargetType.AreaDstHostile:
                    return primaryTarget ?? caster;

                case SpellTargetType.GroundPoint:
                case SpellTargetType.None:
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------------
        // Effect-Dispatch
        // ---------------------------------------------------------------------

        /// <summary>
        /// Wendet einen einzelnen Effekt-Slot auf das Ziel an. Gibt true zurück,
        /// wenn der Effekt eine sichtbare Wirkung erzielt hat (Damage gelandet,
        /// Heal applied, Aura gestackt, …).
        /// </summary>
        static bool ApplyEffect(
            ICombatUnit caster,
            ICombatUnit target,
            SpellDefinition spell,
            int effectIndex,
            SpellEffectDefinition effDef,
            AuraCatalog auraCatalog)
        {
            IUnitStats casterStats = caster.Stats;
            IUnitStats targetStats = target.Stats;

            switch (effDef.Type)
            {
                case SpellEffectType.SchoolDamage:
                case SpellEffectType.WeaponDamage:
                {
                    if (casterStats == null || targetStats == null) { return false; }
                    DamageInfo dmg = CombatFormulas.CalculateSpellDamage(casterStats, targetStats, spell, effectIndex);
                    if (dmg.FinalDamage > 0)
                    {
                        target.TakeDamage(dmg.FinalDamage, caster);
                        return true;
                    }
                    return false;
                }

                case SpellEffectType.Heal:
                case SpellEffectType.HealPct:
                {
                    if (casterStats == null || targetStats == null) { return false; }
                    HealInfo heal = CombatFormulas.CalculateHeal(casterStats, targetStats, spell, effectIndex);
                    if (heal.FinalHeal > 0)
                    {
                        target.Heal(heal.FinalHeal, caster);
                        return true;
                    }
                    return false;
                }

                case SpellEffectType.RestoreMana:
                {
                    int amount = Mathf.Max(0, effDef.Data1);
                    if (amount == 0) { return false; }
                    int newMana = Mathf.Min(target.MaxMana, target.Mana + amount);
                    target.SetMana(newMana);
                    return newMana > target.Mana || amount > 0;
                }

                case SpellEffectType.ApplyAura:
                {
                    if (target.Auras == null || auraCatalog == null) { return false; }
                    return target.Auras.ApplyAuraFromSpell(caster, spell, effectIndex, auraCatalog);
                }

                case SpellEffectType.TriggerSpell:
                {
                    // Triggered Spells umgehen GCD und Mana (Source-Verhalten).
                    // Hier nur Platzhalter — der Catalog-Lookup landet, sobald
                    // SpellCatalog von außen referenziert wird.
                    return false;
                }

                // Bewegungs-/Steuerungs-Effekte landen in einer späteren Iteration
                // (brauchen Pathing- und Knockback-Logik).
                case SpellEffectType.KnockBack:
                case SpellEffectType.Charge:
                case SpellEffectType.Teleport:
                case SpellEffectType.TeleportForward:
                case SpellEffectType.PullTo:
                case SpellEffectType.Threat:
                case SpellEffectType.InterruptCast:
                case SpellEffectType.Dispel:
                case SpellEffectType.SummonNpc:
                case SpellEffectType.MeleeAtk:
                case SpellEffectType.RangedAtk:
                case SpellEffectType.ScriptEffect:
                case SpellEffectType.None:
                default:
                    return false;
            }
        }
    }
}
