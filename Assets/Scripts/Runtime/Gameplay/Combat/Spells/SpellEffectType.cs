namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Wirkung eines einzelnen Spell-Effects. Ein Spell hat bis zu
    /// <see cref="SpellDefinition.MaxEffects"/> Slots, jeder mit eigener
    /// <see cref="SpellEffectType"/>, Target-Type und Datenfeldern.
    /// </summary>
    /// <remarks>
    /// MOBA/Survivor-getrimmter Subset aus
    /// <c>source_server/Shared/SpellDefines.h::Effects</c>. Klassisches MMO-Erbe
    /// (Gem-Sockets, Crafting, Gossip, Lockpicking, Duel, Learn-Spell, Loot, Inspect,
    /// Resurrect) wurde absichtlich weggelassen — kann später ohne Breaking Change
    /// ergänzt werden.
    /// </remarks>
    public enum SpellEffectType
    {
        /// <summary>Kein Effect / leerer Slot.</summary>
        None = 0,

        /// <summary>Direktschaden, skaliert über <see cref="SpellSchool"/>.</summary>
        SchoolDamage = 1,
        /// <summary>Direkte Heilung um einen festen Betrag.</summary>
        Heal = 2,
        /// <summary>Heilung in Prozent des Max-HP-Pools.</summary>
        HealPct = 3,
        /// <summary>Wendet eine <see cref="AuraDefinition"/> auf das Ziel an.</summary>
        ApplyAura = 4,
        /// <summary>Damage skaliert über die ausgerüstete Waffe (Slam, Mortal Strike).</summary>
        WeaponDamage = 5,
        /// <summary>Stellt Mana wieder her (fester Betrag).</summary>
        RestoreMana = 6,
        /// <summary>Knockback in Blickrichtung des Casters.</summary>
        KnockBack = 7,
        /// <summary>Charge — Caster bewegt sich schnell zum Ziel und löst on-hit aus.</summary>
        Charge = 8,
        /// <summary>Teleport zu absoluter Position (Daten = Position-Slot).</summary>
        Teleport = 9,
        /// <summary>Teleport in Blickrichtung um <c>Data1</c> Units (Blink).</summary>
        TeleportForward = 10,
        /// <summary>Zieht das Ziel zum Caster (Anti-Charge).</summary>
        PullTo = 11,
        /// <summary>Triggert einen anderen Spell (Cascade / Combo).</summary>
        TriggerSpell = 12,
        /// <summary>Entfernt Auren von Ziel; Schema-Auswahl über Data1.</summary>
        Dispel = 13,
        /// <summary>Beschwört einen NPC (Pet, Totem) — Data1 = npc_template id.</summary>
        SummonNpc = 14,
        /// <summary>Schlägt mit Mainhand zu (Auto-Attack auf Knopfdruck).</summary>
        MeleeAtk = 15,
        /// <summary>Feuert mit Ranged-Waffe.</summary>
        RangedAtk = 16,
        /// <summary>Setzt / verändert Threat (Taunt, Detaunt).</summary>
        Threat = 17,
        /// <summary>Unterbricht einen laufenden Cast.</summary>
        InterruptCast = 18,
        /// <summary>Script-Hook für Gameplay-spezifische Logik.</summary>
        ScriptEffect = 19,
    }
}
