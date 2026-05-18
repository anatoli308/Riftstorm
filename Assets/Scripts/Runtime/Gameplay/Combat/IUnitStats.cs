namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Lese-Schnittstelle für Einheit-Stats. Wird vom <see cref="CombatFormulas"/>
    /// genutzt, damit die Formel-Schicht (Gameplay-Assembly) nichts über die
    /// konkrete NetworkBehaviour-Implementierung (Game-Assembly) wissen muss.
    /// </summary>
    public interface IUnitStats
    {
        /// <summary>Aktuelle Hit-Points (server-autoritativ).</summary>
        int CurrentHp { get; }

        /// <summary>Maximale Hit-Points.</summary>
        int MaxHp { get; }

        /// <summary>Stärke-Attribut (skaliert Melee-Grundschaden).</summary>
        int Strength { get; }

        /// <summary>Rüstungswert (siehe <c>CombatFormulas.ApplyArmorReduction</c>).</summary>
        int Armor { get; }

        /// <summary>Charakter-Level (skaliert Hit-Chance, Crit, Resist).</summary>
        int Level { get; }

        /// <summary>
        /// XZ-Radius der Trefferhuelle in Worldunits. Wird vom Server zur
        /// Range-Pruefung addiert (<c>weapon.Range + HitRadius</c>) und vom
        /// owner-seitigen Selection-Ring als visuelle Hitbox uebernommen.
        /// </summary>
        float HitRadius { get; }

        // ---------------------------------------------------------------------
        // Magic-/Spell-Stats (Phase-3-Erweiterung).
        // Default-Implementierungen liefern 0, damit bestehende Test-/Mock-
        // Implementationen nicht angefasst werden muessen. Konkrete Einheiten
        // (z.B. UnitStats) ueberschreiben das.
        // ---------------------------------------------------------------------

        /// <summary>Intelligenz — skaliert magische Grundschaeden und Heals.</summary>
        int Intelligence => 0;

        /// <summary>Willenskraft — skaliert Heals und Mana-Regeneration.</summary>
        int Willpower => 0;

        /// <summary>Waffenschaden — Grundschaden fuer WeaponDamage-Effekte
        /// wenn kein konkretes <c>WeaponDefinition</c>-Asset vorliegt.</summary>
        int WeaponDamage => 0;

        /// <summary>Crit-Chance in Prozent (additiv auf <c>BaseCritChance</c>).</summary>
        int CritChance => 0;

        /// <summary>Dodge-Chance in Prozent (additiv auf <c>BaseDodgeChance</c>).</summary>
        int DodgeChance => 0;

        /// <summary>Parry-Chance in Prozent (additiv auf Basis 0).</summary>
        int ParryChance => 0;

        /// <summary>Block-Chance in Prozent (additiv auf Basis 0).</summary>
        int BlockChance => 0;

        /// <summary>Resistenz gegen Fire-Schule.</summary>
        int ResistFire => 0;

        /// <summary>Resistenz gegen Frost-Schule.</summary>
        int ResistFrost => 0;

        /// <summary>Resistenz gegen Arcane-Schule.</summary>
        int ResistArcane => 0;

        /// <summary>Resistenz gegen Nature-Schule.</summary>
        int ResistNature => 0;

        /// <summary>Resistenz gegen Shadow-Schule.</summary>
        int ResistShadow => 0;

        /// <summary>Resistenz gegen Holy-Schule.</summary>
        int ResistHoly => 0;
    }
}
