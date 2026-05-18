namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Schadens-/Resistenzschule eines Spells. 1:1 aus
    /// <c>source_server/Shared/SpellDefines.h::School</c>.
    /// </summary>
    /// <remarks>
    /// Wird sowohl auf <see cref="SpellDefinition"/> (Cast-School, für Resists)
    /// als auch auf einzelne Effects (z. B. periodischer DoT-Tick) angewandt.
    /// </remarks>
    public enum SpellSchool
    {
        /// <summary>Default / kein School-Modifier.</summary>
        None = 0,
        /// <summary>Physisch — wird i. d. R. von Armor reduziert.</summary>
        Physical = 1,
        /// <summary>Feuer.</summary>
        Fire = 2,
        /// <summary>Frost / Eis.</summary>
        Frost = 3,
        /// <summary>Arkan.</summary>
        Arcane = 4,
        /// <summary>Natur / Erde / Gift.</summary>
        Nature = 5,
        /// <summary>Schatten.</summary>
        Shadow = 6,
        /// <summary>Heilig.</summary>
        Holy = 7,
    }
}
