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

        /// <summary>Ranged-Waffenschaden — analog zu <see cref="WeaponDamage"/>,
        /// aber fuer Fernkampf-Effekte (Bow/Crossbow/Gun). Im Spell-Pfad
        /// konsumiert von <see cref="CombatFormulas.CalculateSpellDamage"/>
        /// wenn <c>useRangedWeapon=true</c>.</summary>
        int RangedWeaponDamage => 0;

        /// <summary>Basis-Schaden der aktuell ausgeruesteten Melee-Waffe
        /// (<c>weapon.BaseDamage</c>). 0, wenn keine Melee-Waffe equipped
        /// oder keine <c>WeaponDefinition</c> aufgeloest werden konnte.
        /// Wird im <see cref="CombatFormulas.CalculateSpellDamage"/>-Pfad
        /// fuer <c>WeaponDamage</c>-Spells (z.B. Sinister Strike) zur
        /// echten Waffenskalierung konsumiert.</summary>
        int BaseWeaponDamage => 0;

        /// <summary>Basis-Schaden der aktuell ausgeruesteten Ranged-Waffe
        /// (Bow/Crossbow/Gun). 0, wenn keine Ranged-Waffe equipped ist.
        /// Pendant zu <see cref="BaseWeaponDamage"/> fuer Ranged-Spells
        /// (z.B. Aimed Shot).</summary>
        int BaseRangedWeaponDamage => 0;

        /// <summary>Melee-Crit-Chance in Prozent (additiv auf <c>BaseCritChance</c>),
        /// konsumiert von <see cref="CombatFormulas.RollMeleeHit"/>.</summary>
        int MeleeCritChance => 0;

        /// <summary>Ranged-Crit-Chance in Prozent. Reserviert fuer einen kuenftigen
        /// separaten Ranged-Hit-Pfad; aktuell laufen Ranged-Skills ueber den
        /// Spell-Pfad und nutzen daher <see cref="SpellCritChance"/>.</summary>
        int RangedCritChance => 0;

        /// <summary>Spell-Crit-Chance in Prozent (additiv auf <c>BaseCritChance</c>),
        /// konsumiert von <see cref="CombatFormulas.RollSpellHit"/> und
        /// <see cref="CombatFormulas.CalculateSpellHeal"/>.</summary>
        int SpellCritChance => 0;

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

        // ---------------------------------------------------------------------
        // Aura-Modifier-Hooks (Phase-4-Erweiterung).
        // Werden vom <see cref="CombatFormulas"/> multiplikativ auf den
        // Endschaden / End-Heal angewendet. Default 0 → kein Modifier.
        // Konkrete Implementationen (z.B. UnitStats) aggregieren ihre
        // AuraManager-Werte fuer <c>AuraType.ModifyDamageDealtPct</c> etc.
        // ---------------------------------------------------------------------

        /// <summary>Summe aller <c>ModifyDamageDealtPct</c>-Auren auf dem Caster.</summary>
        int DamageDealtPctMod => 0;

        /// <summary>Summe aller <c>ModifyDamageReceivedPct</c>-Auren auf dem Opfer.</summary>
        int DamageReceivedPctMod => 0;

        /// <summary>Summe aller <c>ModifyHealingDealtPct</c>-Auren auf dem Heiler.</summary>
        int HealingDealtPctMod => 0;

        /// <summary>Summe aller <c>ModifyHealingRecvPct</c>-Auren auf dem Heilziel.</summary>
        int HealingReceivedPctMod => 0;
    }
}
