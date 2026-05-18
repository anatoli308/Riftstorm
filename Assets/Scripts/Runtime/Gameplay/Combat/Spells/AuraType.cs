namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Wirkungstyp einer <see cref="AuraDefinition"/>. Bestimmt, wie das
    /// <c>AuraSystem</c> die Aura pro Tick / pro Stat-Anfrage anwendet.
    /// </summary>
    /// <remarks>
    /// Gekürzter Subset aus <c>SpellDefines.h::AuraType</c>. Auren, die im klassischen
    /// MMO eher Quest- / Crafting-Charakter haben, sind weggelassen.
    /// </remarks>
    public enum AuraType
    {
        /// <summary>Kein Effekt / Placeholder.</summary>
        None = 0,

        /// <summary>Periodischer Schaden (DoT). Tick-Interval aus <see cref="AuraDefinition.IntervalMs"/>.</summary>
        PeriodicDamage = 1,
        /// <summary>Periodische Heilung (HoT).</summary>
        PeriodicHeal = 2,
        /// <summary>Periodische Mana-Regeneration.</summary>
        PeriodicRestoreMana = 3,
        /// <summary>Periodische Melee-Auto-Hits (z. B. brennende Aura, die in der Nähe schadet).</summary>
        PeriodicMeleeDamage = 4,
        /// <summary>Brennt Mana und fügt Schaden in Höhe x % davon zu.</summary>
        PeriodicBurnMana = 5,

        /// <summary>Absorbiert eingehenden Schaden bis zum Pool-Limit (Shield).</summary>
        AbsorbDamage = 10,

        /// <summary>Erhöht / senkt einen Stat um einen flachen Wert (Data1 = StatId, Data2 = Amount).</summary>
        ModifyStat = 20,
        /// <summary>Erhöht / senkt einen Stat prozentual.</summary>
        ModifyStatPct = 21,
        /// <summary>Verändert Bewegungsgeschwindigkeit prozentual (positiv = Sprint, negativ = Slow).</summary>
        ModifyMoveSpeedPct = 22,
        /// <summary>Verändert Melee-Attack-Speed prozentual.</summary>
        ModifyMeleeSpeedPct = 23,
        /// <summary>Verändert Ranged-Attack-Speed prozentual.</summary>
        ModifyRangedSpeedPct = 24,
        /// <summary>Verändert ausgeteilten Schaden prozentual (Buff: +x %, Debuff: -x %).</summary>
        ModifyDamageDealtPct = 25,
        /// <summary>Verändert empfangenen Schaden prozentual (Vulnerability / Schadenreduktion).</summary>
        ModifyDamageReceivedPct = 26,
        /// <summary>Verändert ausgeteilte Heilung prozentual.</summary>
        ModifyHealingDealtPct = 27,
        /// <summary>Verändert empfangene Heilung prozentual.</summary>
        ModifyHealingRecvPct = 28,
        /// <summary>Verändert Resistenz gegen <see cref="SpellSchool"/> (Data1 = School).</summary>
        ModifyResistance = 29,

        /// <summary>Macht immun gegen Mechanik X (Data1 = <see cref="SpellMechanic"/>).</summary>
        MechanicImmunity = 40,
        /// <summary>Macht immun gegen School X.</summary>
        SchoolImmunity = 41,
        /// <summary>Wendet eine Crowd-Control-Mechanik an (Data1 = <see cref="SpellMechanic"/>).</summary>
        InflictMechanic = 42,

        /// <summary>Stun (kann nicht handeln, nicht laufen).</summary>
        Stun = 50,
        /// <summary>Root (kann handeln, aber nicht laufen).</summary>
        Root = 51,
        /// <summary>Silence (kann nicht zaubern).</summary>
        Silence = 52,

        /// <summary>Tauscht das Modell des Ziels (Polymorph).</summary>
        Model = 60,

        /// <summary>Proc-Aura: triggert einen Spell bei einem bestimmten Event.</summary>
        Proc = 70,
    }
}
