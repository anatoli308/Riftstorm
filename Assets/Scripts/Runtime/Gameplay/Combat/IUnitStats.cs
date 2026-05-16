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
    }
}
