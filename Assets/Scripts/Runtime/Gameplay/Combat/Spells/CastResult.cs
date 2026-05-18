namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Ergebnis einer Cast-Validierung. Wird vom server-authoritativen
    /// Spell-Caster zurückgegeben, bevor ein Cast tatsächlich startet.
    /// </summary>
    /// <remarks>
    /// 1:1 aus <c>source_server/Server/src/Combat/SpellCaster.h::CastResult</c>,
    /// inkl. der Lücken in der Nummerierung (Gruppierung nach Fehlerklasse).
    /// </remarks>
    public enum CastResult : byte
    {
        /// <summary>Cast darf starten.</summary>
        Success = 0,

        // Caster-State
        /// <summary>Caster ist tot.</summary>
        CasterDead = 1,
        /// <summary>Caster ist gestunnt.</summary>
        CasterStunned = 2,
        /// <summary>Caster ist gesilenced (gilt für Spells).</summary>
        CasterSilenced = 3,
        /// <summary>Caster bewegt sich (gilt für channeled / cast-time Spells).</summary>
        CasterMoving = 4,
        /// <summary>Caster castet bereits etwas anderes.</summary>
        CasterCasting = 5,

        // Spell
        /// <summary>Spell-Id existiert nicht im Katalog.</summary>
        UnknownSpell = 10,
        /// <summary>Caster kennt diesen Spell nicht (nicht im Loadout).</summary>
        SpellNotLearned = 11,
        /// <summary>Spell ist aktuell deaktiviert (Balancing / Patch).</summary>
        SpellDisabled = 12,

        // Resources
        /// <summary>Nicht genug Mana.</summary>
        NotEnoughMana = 20,
        /// <summary>Nicht genug HP (für HP-Cost-Spells).</summary>
        NotEnoughHealth = 21,
        /// <summary>Spell ist auf Cooldown.</summary>
        OnCooldown = 22,
        /// <summary>Global Cooldown läuft noch.</summary>
        OnGlobalCooldown = 23,
        /// <summary>Reagent fehlt im Inventar.</summary>
        MissingReagent = 24,

        // Target
        /// <summary>Kein Ziel ausgewählt.</summary>
        NoTarget = 30,
        /// <summary>Ziel-Typ passt nicht zum Spell.</summary>
        InvalidTarget = 31,
        /// <summary>Ziel ist tot.</summary>
        TargetDead = 32,
        /// <summary>Ziel ist immun gegen den Spell.</summary>
        TargetImmune = 33,
        /// <summary>Friendly-only-Spell auf Feind versucht.</summary>
        TargetFriendly = 34,
        /// <summary>Hostile-only-Spell auf Verbündeten versucht.</summary>
        TargetHostile = 35,
        /// <summary>Spell kann nicht auf den Caster selbst angewandt werden.</summary>
        TargetSelf = 36,

        // Range
        /// <summary>Ziel zu weit weg.</summary>
        OutOfRange = 40,
        /// <summary>Ziel zu nah (Minimum-Range nicht erreicht).</summary>
        TooClose = 41,
        /// <summary>Keine Line-of-Sight zum Ziel.</summary>
        LineOfSight = 42,

        // Equipment
        /// <summary>Falsche oder fehlende Waffe ausgerüstet.</summary>
        WrongEquipment = 50,

        // Area / Context
        /// <summary>Spell in diesem Gebiet nicht erlaubt (z. B. Stadt).</summary>
        AreaUnavailable = 60,
        /// <summary>Spell verlangt Combat-State, Caster ist out-of-combat.</summary>
        CombatRequired = 61,
        /// <summary>Spell verlangt Out-of-Combat-State.</summary>
        OutOfCombat = 62,

        /// <summary>Unerwarteter Fehler.</summary>
        InternalError = 255,
    }
}
