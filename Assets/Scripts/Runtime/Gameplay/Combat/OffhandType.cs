namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Kategorie eines Offhand-Items. Bestimmt das visuelle FLARE-Atlas-Slotting
    /// (alle Werte teilen sich denselben "offhand"-Layer auf dem FlareCharacter)
    /// und liefert Gameplay-Hinweise für Block-Mechanik bzw. Casting-Modifier.
    /// </summary>
    /// <remarks>
    /// <see cref="Torch"/>, <see cref="Quiver"/> und <see cref="Orb"/> sind für Modder
    /// reserviert — es existieren noch keine FLARE-Atlanten im Projekt.
    /// </remarks>
    public enum OffhandType
    {
        /// <summary>Leerer Slot.</summary>
        None = 0,

        /// <summary>Kleiner Schild — schneller Block, geringere Reduktion.</summary>
        Buckler = 1,

        /// <summary>Großer Schild — langsamer Block, hohe Reduktion.</summary>
        Shield = 2,

        /// <summary>Fackel — Lichtquelle, reserviert für Modder.</summary>
        Torch = 3,

        /// <summary>Köcher — Bonus für Ranged, reserviert für Modder.</summary>
        Quiver = 4,

        /// <summary>Magie-Orb — Spell-Bonus, reserviert für Modder.</summary>
        Orb = 5,
    }
}
