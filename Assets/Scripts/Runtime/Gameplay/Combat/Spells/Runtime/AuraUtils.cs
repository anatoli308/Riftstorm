namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Static-Helper-Klasse für Aura-Erzeugung und Aura-Klassifizierung.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>source_server/Server/src/Combat/AuraSystem.cpp::AuraUtils</c>.
    /// </remarks>
    public static class AuraUtils
    {
        /// <summary>
        /// Baut eine konkrete <see cref="Aura"/> aus der statischen
        /// <see cref="AuraDefinition"/> + Quell-Spell + Caster-Kontext.
        /// </summary>
        /// <param name="caster">Caster (für Level-Scaling + Guid). Darf null sein.</param>
        /// <param name="sourceSpell">Quell-Spell (für Caster-Level / SpellId-Tracking).</param>
        /// <param name="effectIndex">Effect-Slot-Index am Quell-Spell.</param>
        /// <param name="auraDef">Die statische Aura-Definition aus <c>auras.json</c>.</param>
        public static Aura CreateAuraFromDefinition(
            ICombatUnit caster,
            SpellDefinition sourceSpell,
            int effectIndex,
            AuraDefinition auraDef)
        {
            Aura aura = new();
            if (auraDef == null || string.IsNullOrEmpty(auraDef.Id))
            {
                return aura;
            }

            aura.AuraId = auraDef.Id;
            aura.SourceSpellId = sourceSpell != null ? sourceSpell.Id : string.Empty;
            aura.CasterGuid = caster != null ? caster.Guid : 0UL;
            aura.EffectIndex = effectIndex;

            aura.MaxDurationMs = auraDef.DurationMs;
            aura.ElapsedMs = 0;

            aura.MaxStacks = auraDef.MaxStacks > 0 ? auraDef.MaxStacks : 1;
            aura.Stacks = 1;

            aura.DispelType = auraDef.DispelType;
            aura.Mechanic = auraDef.Mechanic;

            if (auraDef.Positive)
            {
                aura.Flags |= AuraFlags.Positive;
            }

            // Effekt-Slot aus AuraDefinition aufbauen.
            AuraEffect effect = new()
            {
                Type = auraDef.Type,
                MiscValue = auraDef.Data2,
                PeriodicIntervalMs = auraDef.IntervalMs,
                PeriodicTimer = 0,
            };

            // Wert-Berechnung: per-Level-Scaling kommt im Source aus
            // SpellUtils::calculateEffectValue. Wir nutzen Data1 als Basis und
            // ergänzen das Caster-Level-Scaling 1:1 (siehe SpellUtils).
            int casterLevel = caster != null ? caster.Level : 1;
            effect.BaseValue = SpellUtils.CalculateAuraBaseValue(auraDef, casterLevel);
            effect.PerStackValue = auraDef.Data3;

            aura.Effects.Add(effect);

            return aura;
        }

        /// <summary>
        /// Gibt zurück, ob ein <see cref="AuraType"/> per Default ein Buff ist.
        /// </summary>
        public static bool IsPositiveAuraType(AuraType type)
        {
            switch (type)
            {
                case AuraType.PeriodicHeal:
                case AuraType.PeriodicRestoreMana:
                case AuraType.AbsorbDamage:
                    return true;

                case AuraType.PeriodicDamage:
                case AuraType.PeriodicBurnMana:
                case AuraType.PeriodicMeleeDamage:
                case AuraType.Stun:
                case AuraType.Root:
                case AuraType.Silence:
                    return false;

                // Mod-Stats hängen vom Vorzeichen ab — Default optimistisch.
                default:
                    return true;
            }
        }

        /// <summary>True, wenn der Typ eine harte Crowd-Control-Mechanik ist.</summary>
        public static bool IsCrowdControl(AuraType type)
        {
            return type == AuraType.Stun
                || type == AuraType.Root
                || type == AuraType.Silence;
        }
    }
}
