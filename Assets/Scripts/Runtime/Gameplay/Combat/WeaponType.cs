namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Waffenkategorie. Bestimmt, welche Attack-Animation gespielt wird
    /// (siehe <see cref="WeaponDefinition.PickAttackAnim"/>).
    /// </summary>
    /// <remarks>
    /// Reihenfolge und Werte spiegeln <c>ItemDefines::WeaponType</c> aus dem Original
    /// (siehe <c>referenzen/04-animationen-combat.md</c>). <see cref="None"/> entspricht
    /// "unbewaffnet" und schlägt mit der Faust zu.
    /// </remarks>
    public enum WeaponType
    {
        /// <summary>Keine Waffe / unbewaffnet → Swing.</summary>
        None = 0,
        /// <summary>Schwert → Swing.</summary>
        Sword = 1,
        /// <summary>Axt → Swing.</summary>
        Axe = 2,
        /// <summary>Stumpfwaffe → Swing.</summary>
        Mace = 3,
        /// <summary>Dolch → Swing.</summary>
        Dagger = 4,
        /// <summary>Zauberstab (gross, zweihändig) → Swing (Spells via Cast separat).</summary>
        Staff = 5,
        /// <summary>Bogen → Shoot.</summary>
        Bow = 6,
        /// <summary>Armbrust → Shoot.</summary>
        Crossbow = 7,
        /// <summary>Zauberstab (klein) → Swing (Spells via Cast separat).</summary>
        Wand = 8,
        /// <summary>Schusswaffe → Shoot.</summary>
        Gun = 9,
        /// <summary>Stangenwaffe → Swing.</summary>
        Polearm = 10,
        /// <summary>Faust → Swing.</summary>
        Fist = 11,
    }
}
