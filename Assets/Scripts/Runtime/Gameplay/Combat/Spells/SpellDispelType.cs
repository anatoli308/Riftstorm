namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Dispel-Kategorie einer Aura. Bestimmt, mit welchem Dispel-Spell
    /// sie entfernt werden kann.
    /// </summary>
    /// <remarks>1:1 aus <c>SpellDefines.h::DispelType</c>.</remarks>
    public enum SpellDispelType
    {
        /// <summary>Nicht entfernbar.</summary>
        None = 0,
        /// <summary>Magie (typischer Caster-Buff / -Debuff).</summary>
        Magic = 1,
        /// <summary>Fluch.</summary>
        Curse = 2,
        /// <summary>Krankheit.</summary>
        Disease = 3,
        /// <summary>Gift.</summary>
        Poison = 4,
        /// <summary>Physisch (Bleed, Stagger).</summary>
        Physical = 5,
    }
}
