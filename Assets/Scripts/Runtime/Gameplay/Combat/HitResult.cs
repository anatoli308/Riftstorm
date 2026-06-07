namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Ergebnis einer Hit-Resolution. Spiegelt das C++-Vorbild (Steam-Server)
    /// und entscheidet, wie der Folge-Schaden im <see cref="CombatFormulas"/>
    /// gewichtet wird (Crit-Multiplier, Glancing-Multiplier etc.).
    /// </summary>
    public enum HitResult : byte
    {
        /// <summary>Regulärer Treffer.</summary>
        Hit = 0,

        /// <summary>Kritischer Treffer (Schaden × <c>CombatFormulas.CritMultiplier</c>).</summary>
        Crit = 1,

        /// <summary>Angreifer hat verfehlt — kein Schaden.</summary>
        Miss = 2,

        /// <summary>Opfer ist ausgewichen — kein Schaden.</summary>
        Dodge = 3,

        /// <summary>Opfer hat pariert — kein Schaden.</summary>
        Parry = 4,

        /// <summary>Opfer hat geblockt — reduzierter Schaden.</summary>
        Block = 5,

        /// <summary>Streifschuss / Glancing — reduzierter Schaden.</summary>
        GlancingBlow = 6,

        /// <summary>Vollständig resistiert — kein Schaden.</summary>
        Resist = 7,

        /// <summary>Immun gegen diese Schadensart — kein Schaden.</summary>
        Immune = 8,

        /// <summary>Schaden vollständig durch Schild/Absorb-Buff aufgefangen.</summary>
        Absorb = 9,
    }
}
