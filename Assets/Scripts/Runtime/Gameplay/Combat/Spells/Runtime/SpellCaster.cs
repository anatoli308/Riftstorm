using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Static-Service für die server-authoritative Cast-Validierung. Nimmt
    /// Caster + Spell + Ziel entgegen und gibt einen exakten <see cref="CastResult"/>
    /// zurück (Success oder konkreter Failure-Code).
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>source_server/Server/src/Combat/SpellCaster.h/.cpp</c>.
    /// Reine Validierung — die eigentliche Ausführung (Projektil, Hit-Resolution,
    /// Mana-Abzug, GCD/CD-Start) übernimmt der Caller im Server-Frame.
    /// </remarks>
    public static class SpellCaster
    {
        /// <summary>
        /// Validiert einen Cast komplett (State, Resources, Equipment, Target, Range).
        /// </summary>
        /// <param name="caster">Caster-Unit (darf nicht null sein).</param>
        /// <param name="spell">Spell-Definition aus dem <c>SpellCatalog</c>.</param>
        /// <param name="target">Ziel-Unit oder null für Self-/AoE-Casts.</param>
        public static CastResult Validate(ICombatUnit caster, SpellDefinition spell, ICombatUnit target)
        {
            if (caster == null || spell == null)
            {
                return CastResult.InternalError;
            }

            CastResult result = CheckCasterState(caster, spell);
            if (result != CastResult.Success) { return result; }

            result = CheckResources(caster, spell);
            if (result != CastResult.Success) { return result; }

            result = CheckEquipment(caster, spell);
            if (result != CastResult.Success) { return result; }

            // Self-Casts skippen Target- + Range-Check.
            if (SpellUtils.IsSelfOnly(spell))
            {
                return CastResult.Success;
            }

            result = CheckTarget(caster, spell, target);
            if (result != CastResult.Success) { return result; }

            if (target != null && !ReferenceEquals(target, caster))
            {
                result = CheckRange(caster, spell, target);
                if (result != CastResult.Success) { return result; }
            }

            return CastResult.Success;
        }

        // =====================================================================
        // Sub-Checks
        // =====================================================================

        static CastResult CheckCasterState(ICombatUnit caster, SpellDefinition spell)
        {
            if (caster.IsDead) { return CastResult.CasterDead; }
            if (caster.IsStunned) { return CastResult.CasterStunned; }

            // Silence trifft nur magische Schools (Physical-Spells sind shouts/strikes).
            if (spell.School != SpellSchool.Physical && caster.IsSilenced)
            {
                return CastResult.CasterSilenced;
            }
            return CastResult.Success;
        }

        static CastResult CheckResources(ICombatUnit caster, SpellDefinition spell)
        {
            int manaCost = SpellUtils.CalculateManaCost(spell, caster.Level, caster.MaxMana);
            if (manaCost > 0 && caster.Mana < manaCost)
            {
                return CastResult.NotEnoughMana;
            }

            int healthCost = spell.HealthCost;
            if (spell.HealthCostPct > 0 && caster.MaxHealth > 0)
            {
                healthCost += (caster.MaxHealth * spell.HealthCostPct) / 100;
            }
            if (healthCost > 0 && caster.Health <= healthCost)
            {
                return CastResult.NotEnoughHealth;
            }

            // Cooldowns + GCD nur für Spieler — Mobs umgehen die Map aus Performance-Gründen
            // und tracken Cooldowns über AI-State (1:1 zum Source).
            if (caster.IsPlayer && caster.Cooldowns != null)
            {
                if (caster.Cooldowns.IsOnCooldown(spell.Id))
                {
                    return CastResult.OnCooldown;
                }
                if (!string.IsNullOrEmpty(spell.CooldownCategory)
                    && caster.Cooldowns.IsCategoryOnCooldown(spell.CooldownCategory))
                {
                    return CastResult.OnCooldown;
                }
                if ((spell.Attributes & SpellAttributes.IgnoreGcd) == 0
                    && caster.Cooldowns.IsOnGcd())
                {
                    return CastResult.OnGlobalCooldown;
                }
            }
            return CastResult.Success;
        }

        static CastResult CheckEquipment(ICombatUnit caster, SpellDefinition spell)
        {
            // Equipment-Slots hängen am späteren Inventory-System. Placeholder
            // hält den Validation-Pfad stabil; bis dahin gilt jedes Equipment als ok.
            _ = caster;
            _ = spell;
            return CastResult.Success;
        }

        static CastResult CheckTarget(ICombatUnit caster, SpellDefinition spell, ICombatUnit target)
        {
            if (SpellUtils.RequiresTarget(spell) && target == null)
            {
                return CastResult.NoTarget;
            }
            if (target == null)
            {
                return CastResult.Success;
            }

            if (target.IsDead && (spell.Attributes & SpellAttributes.CanTargetDead) == 0)
            {
                return CastResult.TargetDead;
            }

            if (ReferenceEquals(target, caster)
                && (spell.Attributes & SpellAttributes.CantTargetSelf) != 0)
            {
                return CastResult.TargetSelf;
            }

            bool canFriendly = SpellUtils.CanTargetFriendly(spell);
            bool canHostile = SpellUtils.CanTargetHostile(spell);
            bool isFriendly = AreFriendly(caster, target);
            bool isHostile = AreHostile(caster, target);

            if (canHostile && !canFriendly && isFriendly && !ReferenceEquals(target, caster))
            {
                return CastResult.TargetFriendly;
            }
            if (canFriendly && !canHostile && isHostile)
            {
                return CastResult.TargetHostile;
            }
            return CastResult.Success;
        }

        static CastResult CheckRange(ICombatUnit caster, SpellDefinition spell, ICombatUnit target)
        {
            if (spell.Range <= 0f)
            {
                return CastResult.Success;
            }
            float dist = GetDistance(caster, target);
            if (dist > spell.Range)
            {
                return CastResult.OutOfRange;
            }
            if (spell.RangeMin > 0f && dist < spell.RangeMin)
            {
                return CastResult.TooClose;
            }
            return CastResult.Success;
        }

        // =====================================================================
        // Helper
        // =====================================================================

        /// <summary>Horizontale 3D-Distanz zwischen zwei Units.</summary>
        public static float GetDistance(ICombatUnit a, ICombatUnit b)
        {
            if (a == null || b == null) { return float.PositiveInfinity; }
            return Vector3.Distance(a.Position, b.Position);
        }

        /// <summary>True, wenn beide Units gegnerischen Factions angehören.</summary>
        public static bool AreHostile(ICombatUnit a, ICombatUnit b)
        {
            if (a == null || b == null) { return false; }
            if (ReferenceEquals(a, b)) { return false; }
            return a.FactionId != b.FactionId;
        }

        /// <summary>True, wenn beide Units derselben Faction angehören.</summary>
        public static bool AreFriendly(ICombatUnit a, ICombatUnit b)
        {
            if (a == null || b == null) { return false; }
            if (ReferenceEquals(a, b)) { return true; }
            return a.FactionId == b.FactionId;
        }

        /// <summary>User-facing Text für UI / Combat-Log.</summary>
        public static string GetCastResultString(CastResult result) => result switch
        {
            CastResult.Success            => "Bereit",
            CastResult.CasterDead         => "Du bist tot",
            CastResult.CasterStunned      => "Du bist betäubt",
            CastResult.CasterSilenced     => "Du bist stumm",
            CastResult.CasterMoving       => "Nicht in Bewegung",
            CastResult.CasterCasting      => "Bereits am Casten",
            CastResult.UnknownSpell       => "Unbekannter Spell",
            CastResult.SpellNotLearned    => "Spell nicht erlernt",
            CastResult.SpellDisabled      => "Spell deaktiviert",
            CastResult.NotEnoughMana      => "Nicht genug Mana",
            CastResult.NotEnoughHealth    => "Nicht genug Leben",
            CastResult.OnCooldown         => "Noch nicht bereit",
            CastResult.OnGlobalCooldown   => "Noch nicht bereit",
            CastResult.MissingReagent     => "Reagenz fehlt",
            CastResult.NoTarget           => "Kein Ziel",
            CastResult.InvalidTarget      => "Ungültiges Ziel",
            CastResult.TargetDead         => "Ziel ist tot",
            CastResult.TargetImmune       => "Ziel ist immun",
            CastResult.TargetFriendly     => "Verbündetes Ziel",
            CastResult.TargetHostile      => "Feindliches Ziel",
            CastResult.TargetSelf         => "Nicht auf dich selbst",
            CastResult.OutOfRange         => "Außer Reichweite",
            CastResult.TooClose           => "Zu nah",
            CastResult.LineOfSight        => "Sichtlinie blockiert",
            CastResult.WrongEquipment     => "Falsche Ausrüstung",
            CastResult.AreaUnavailable    => "Hier nicht möglich",
            CastResult.CombatRequired     => "Nur im Kampf",
            CastResult.OutOfCombat        => "Nur außerhalb des Kampfes",
            _                             => "Fehler",
        };
    }
}
