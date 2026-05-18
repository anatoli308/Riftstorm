using System;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Daten-getriebene Spell-Flags. Bitmask, damit ein Spell mehrere
    /// orthogonale Attribute kombinieren kann (z. B. <c>Channeled | IgnoreLOS</c>).
    /// </summary>
    /// <remarks>
    /// Subset aus <c>SpellDefines.h::Attributes</c>. Felder, die nur im
    /// klassischen MMO Sinn ergeben (Inspect / Duel / Anim-Lock-Start /
    /// Per-Cast-Limits etc.) sind weggelassen — ergänzbar ohne Breaking
    /// Change, da Bitmask.
    /// </remarks>
    [Flags]
    public enum SpellAttributes
    {
        /// <summary>Keine Flags.</summary>
        None = 0,

        /// <summary>Cast triggert kein Global Cooldown (z. B. Items, Procs).</summary>
        IgnoreGcd = 1 << 0,
        /// <summary>Setzt Cooldowns / Casts in Bewegung nicht zurück (Instant-Triggered).</summary>
        Triggered = 1 << 1,
        /// <summary>Passive Aura — wird permanent angewandt, nicht "gecastet".</summary>
        Passive = 1 << 2,
        /// <summary>Channeled — Cast hält über <see cref="SpellDefinition.DurationMs"/> an.</summary>
        Channeled = 1 << 3,

        /// <summary>Cast / Effekte ignorieren Line-of-Sight.</summary>
        IgnoreLos = 1 << 10,
        /// <summary>Schaden ignoriert Armor.</summary>
        IgnoreArmor = 1 << 11,
        /// <summary>Schaden ignoriert School-Resistenzen.</summary>
        IgnoreResistances = 1 << 12,
        /// <summary>Trifft auch unverwundbare Ziele.</summary>
        IgnoreInvulnerability = 1 << 13,
        /// <summary>Kann nicht kritisch treffen.</summary>
        CantCrit = 1 << 14,
        /// <summary>Erzeugt keine Threat / Aggro.</summary>
        NoThreat = 1 << 15,

        /// <summary>Caster kann sich selbst nicht targeten.</summary>
        CantTargetSelf = 1 << 20,
        /// <summary>Erlaubt das Targeten toter Ziele (z. B. Resurrect).</summary>
        CanTargetDead = 1 << 21,
        /// <summary>Castbar nur außerhalb von Combat.</summary>
        NotInCombat = 1 << 22,
        /// <summary>Wirkt auf einen Bodenpunkt statt auf eine Unit.</summary>
        TargetsGround = 1 << 23,

        /// <summary>Nur eine Instanz pro Caster gleichzeitig.</summary>
        OnePerCaster = 1 << 30,
        /// <summary>Nur eine Instanz pro Target gleichzeitig.</summary>
        OnePerTarget = 1 << 31,
    }
}
