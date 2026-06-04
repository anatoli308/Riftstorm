using System;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Spell-Schule (Damage-Type) aus <c>spell_template.cast_school</c> und
    /// <c>spell_template.magic_roll_school</c>. 1:1 aus
    /// <c>source_server/Shared/SpellDefines.h</c> (<c>enum class School</c>).
    /// Wird per Newtonsoft aus dem int-Wert deserialisiert.
    /// </summary>
    public enum SpellSchool
    {
        Physical = 0,
        Fire = 1,
        Frost = 2,
        Arcane = 3,
        Nature = 4,
        Shadow = 5,
        Holy = 6,
    }

    /// <summary>
    /// Effekt-Typ eines Spell-Slots (<c>spell_template.effect1..3</c>). 1:1 aus
    /// <c>source_server/Shared/SpellDefines.h</c> (<c>enum class Effects</c>).
    /// </summary>
    /// <remarks>
    /// Werte unter <c>Reserved</c>-Kommentar in Source sind in der aktuellen
    /// <c>game.db</c> ungenutzt, werden aber zur Schema-Stabilitaet hier
    /// trotzdem mitgefuehrt.
    /// </remarks>
    public enum SpellEffect
    {
        None = 0,
        SchoolDamage = 1,
        Teleport = 2,
        ApplyAura = 3,
        ApplyAreaAura = 4,
        ManaDrain = 5,
        Heal = 6,
        Resurrect = 7,
        HealthDrain = 8,
        SummonNpc = 10,
        RestoreMana = 11,
        CreateItem = 12,
        Dispel = 13,
        WeaponDamage = 14,
        ManaBurn = 17,
        Threat = 18,
        InterruptCast = 22,
        SummonObject = 23,
        TriggerSpell = 24,
        KnockBack = 25,
        ScriptEffect = 26,
        HealPct = 27,
        RestoreManaPct = 28,
        TeleportForward = 29,
        MeleeAtk = 30,
        RangedAtk = 31,
        LootEffect = 32,
        Kill = 33,
        Gossip = 34,
        Inspect = 35,
        ApplyGemSocket = 36,
        Charge = 37,
        Duel = 38,
        SlideFrom = 39,
        ApplyOrbEnchant = 40,
        LearnSpell = 41,
        NearestWp = 42,
        PullTo = 43,
        DestroyGems = 44,
        CombineItem = 45,
        ExtractOrb = 46,
        ApplyOrbEnchantArcane = 47,
    }

    /// <summary>
    /// Target-Typ eines Spell-Effekts (<c>spell_template.effect1_targetType</c>).
    /// 1:1 aus <c>source_server/Shared/SpellDefines.h</c> (<c>enum class TargetType</c>).
    /// </summary>
    public enum SpellTargetType
    {
        None = 0,
        UnitCaster = 1,
        UnitFriendly = 2,
        UnitAreaSrcFriendly = 3,
        UnitAreaDstFriendly = 4,
        UnitAreaDstFriendlyFromDst = 5,
        UnitAreaDstHostileFromDst = 6,
        TargetGameObject = 13,
        UnitHostile = 14,
        UnitAreaSrcHostile = 15,
        UnitAny = 17,
        UnitAreaDstHostile = 19,
        TargetItem = 20,
    }

    /// <summary>
    /// Aura-Subtyp wenn <see cref="SpellEffect.ApplyAura"/> gesetzt ist
    /// (steckt in <c>effectN_data1</c>). 1:1 aus
    /// <c>source_server/Shared/SpellDefines.h</c> (<c>enum class AuraType</c>).
    /// </summary>
    public enum AuraType
    {
        None = 0,
        PeriodicDamage = 1,
        PeriodicHeal = 2,
        InflictMechanic = 3,
        ModifyStat = 4,
        ModifyStatPct = 5,
        AbsorbDamage = 6,
        PeriodicMeleeDamage = 7,
        PeriodicHealPct = 8,
        PeriodicTriggerSpell = 9,
        PeriodicRestoreMana = 10,
        ModifyMoveSpeedPct = 11,
        MechanicImmunity = 12,
        SchoolImmunity = 13,
        ModifyDamageDealtPct = 14,
        ModifyDamageReceivedPct = 15,
        ModifyMeleeSpeedPct = 16,
        ModifyRangedSpeedPct = 17,
        ModifyResistance = 18,
        Model = 19,
        PeriodicBurnMana = 20,
        Proc = 21,
        ModifyHealingDealtPct = 22,
        ModifyHealingRecvPct = 23,
        PeriodicRestoreManaPct = 24,
        RepopOntopOfSelf = 26,
        Stun = 30,
        Root = 31,
        Silence = 32,
    }

    /// <summary>
    /// Trigger-Bedingungen einer <see cref="AuraType.Proc"/>-Aura (steckt in
    /// <c>effectN_data2</c> = <see cref="AuraEffect.MiscValue"/>). 1:1 aus
    /// <c>source_server/Shared/SpellDefines.h</c> (<c>enum ProcFlags : uint32_t</c>).
    /// Bitfeld — eine Proc-Aura kann auf mehrere Ereignisse reagieren.
    /// </summary>
    [Flags]
    public enum ProcFlags
    {
        /// <summary>Kein Trigger.</summary>
        None = 0,
        /// <summary>Der Aura-Traeger hat Schaden erlitten.</summary>
        HolderTookDamage = 1 << 0,
        /// <summary>Der Aura-Traeger hat Schaden ausgeteilt.</summary>
        HolderDealtDamage = 1 << 1,
        /// <summary>
        /// Der Aura-Traeger war gegen einen Angriff immun (School-/Mechanic-
        /// Immunity hat den Treffer geschluckt). Treiber von Focused Evasion:
        /// der naechste physische Angriff wird geblockt und verbraucht die Ladung.
        /// </summary>
        HolderWasImmune = 1 << 2,
        /// <summary>Der Aura-Traeger ist einem Angriff ausgewichen (Dodge).</summary>
        HolderDodged = 1 << 3,
    }

    /// <summary>
    /// Aktion, die beim Ausloesen einer <see cref="AuraType.Proc"/>-Aura
    /// ausgefuehrt wird (steckt in <c>effectN_data3</c>). 1:1 aus
    /// <c>source_server/Shared/SpellDefines.h</c> (<c>enum ProcType : uint8_t</c>).
    /// </summary>
    public enum ProcType
    {
        /// <summary>Keine Aktion.</summary>
        None = 0,
        /// <summary>Verbraucht eine Ladung der Aura (entfernt sie bei 0 Ladungen).</summary>
        RemoveCharge = 1,
    }

    /// <summary>
    /// Konkreter Mechanic-Subtyp einer <see cref="AuraType.InflictMechanic"/>-Aura.
    /// Steckt in <c>effectN_data2</c> (= <see cref="AuraEffect.MiscValue"/>).
    /// 1:1 aus <c>source_server/Shared/SpellDefines.h</c> (<c>enum class Mechanics</c>).
    /// </summary>
    /// <remarks>
    /// Im Source-Original ist das Enum ein 32-Bit-Flagfeld (<c>1u &lt;&lt; n</c>);
    /// die DB speichert pro Aura aber nur den Index (1..13) als <c>MiscValue</c>,
    /// daher sind hier die Indizes definiert. Beispiele aus den Templates:
    /// Ice Blast=4 (Root), Chains of Ice=7 (Snare), Ice Shard=8 (Stun).
    /// </remarks>
    public enum Mechanic
    {
        None = 0,
        Confused = 1,
        Pacify = 2,
        Fear = 3,
        /// <summary>Root / immobilisiert. Wirkt wie <see cref="AuraType.Root"/>.</summary>
        Root = 4,
        /// <summary>Silenced. Wirkt wie <see cref="AuraType.Silence"/>.</summary>
        Silence = 5,
        Sleep = 6,
        /// <summary>Snare / Slow. <c>BaseValue</c> (= <c>data3</c>) ist die Speed-Aenderung in %.</summary>
        Snare = 7,
        /// <summary>Stunned. Wirkt wie <see cref="AuraType.Stun"/>.</summary>
        Stun = 8,
        /// <summary>
        /// Incapacitated (Deep Freeze / Blindside). Verhindert Bewegung,
        /// Angriffe und Casts. Bricht typischerweise auf erlittenen Schaden
        /// (via <see cref="SpellTemplate.AuraInterruptFlags"/>
        /// = <see cref="AuraInterruptFlag.OnDamageTaken"/>).
        /// </summary>
        Incapacitated = 9,
        Disrupt = 10,
        /// <summary>
        /// Polymorph (Bind Spirit / Sheep). Verhindert Bewegung, Angriffe und
        /// Casts; das Ziel ist effektiv "aus dem Spiel". Bricht auf erlittenen
        /// Schaden (<see cref="AuraInterruptFlag.OnDamageTaken"/>).
        /// </summary>
        Polymorph = 11,
        Charging = 12,
        Stealth = 13,
    }

    /// <summary>
    /// Bitmaske aus <c>spell_template.aura_interrupt_flags</c>. Steuert, bei
    /// welchen Ereignissen eine aktive Aura vorzeitig entfernt wird.
    /// </summary>
    /// <remarks>
    /// Aus den DB-Templates abgeleitet — bestaetigt: Wert <c>32</c> = Aura
    /// bricht, sobald der Traeger Schaden erleidet (Bind Spirit "until struck
    /// by damage", Blindside / Deep Freeze "any damage will remove the effect").
    /// Weitere Flags werden ergaenzt, sobald sie in Templates auftauchen.
    /// </remarks>
    [Flags]
    public enum AuraInterruptFlag
    {
        None = 0,
        /// <summary>Aura wird entfernt, sobald der Traeger Schaden erleidet.</summary>
        OnDamageTaken = 1 << 5,
    }

    /// <summary>
    /// Dispel-Kategorie eines Spells / einer Aura (<c>spell_template.dispel</c>).
    /// 1:1 aus <c>source_server/Shared/SpellDefines.h</c> (<c>enum class DispelType</c>).
    /// </summary>
    public enum DispelType
    {
        None = 0,
        Magic = 1,
        Curse = 2,
        Disease = 3,
        Poison = 4,
        Physical = 5,
    }

    /// <summary>
    /// Bitmaske der Spell-Attribute (<c>spell_template.attributes</c>, 64 Bit).
    /// 1:1 aus <c>source_server/Shared/SpellDefines.h</c> (<c>enum class Attributes</c>).
    /// Wird als <c>long</c> deserialisiert (DB-Wert kann groesser als int32 sein).
    /// </summary>
    [Flags]
    public enum SpellAttributes : long
    {
        None = 0,
        AutoApproach = 1L << 0,
        CantTargetSelf = 1L << 1,
        CanTargetDead = 1L << 2,
        CantCrit = 1L << 3,
        IgnoreArmor = 1L << 4,
        IgnoreInvulnerability = 1L << 5,
        IgnoreLos = 1L << 6,
        IgnoreResistances = 1L << 7,
        NotInArena = 1L << 8,
        NotInDungeon = 1L << 9,
        NoHealBonus = 1L << 10,
        NoSpellBonus = 1L << 11,
        NoThreat = 1L << 12,
        NoAggro = 1L << 13,
        ImpossibleBlock = 1L << 14,
        ImpossibleDodge = 1L << 15,
        ImpossibleMiss = 1L << 16,
        ImpossibleParry = 1L << 17,
        Passive = 1L << 18,
        DontStopCastingSound = 1L << 19,
        OnePerCaster = 1L << 20,
        OnePerTarget = 1L << 21,
        NotInCombat = 1L << 22,
        SameStackForAllCasters = 1L << 24,
        Triggered = 1L << 25,
        TargetNotInCombat = 1L << 27,
        TargetPlayersOnly = 1L << 29,
        AnimLockStart = 1L << 30,
        NoGoLock = 1L << 31,
        TargetsGround = 1L << 33,
        MouseoverTargeting = 1L << 34,
        TargetsItem = 1L << 35,
        IgnoreStun = 1L << 36,
        IgnoreIncapacitated = 1L << 37,
        IgnoreSleep = 1L << 38,
        IgnoreConfused = 1L << 39,
        IgnoreFear = 1L << 40,
        IgnorePolymorph = 1L << 41,
        PersistsThroughDeath = 1L << 42,

        // -- Riftstorm-Erweiterung (nicht in source_server/SpellDefines.h) --

        /// <summary>
        /// Caster darf sich waehrend dieses Casts bewegen, ohne ihn abzubrechen.
        /// Spiegelt WoW-Talente wie Scorch-while-moving. Bewusst hoch gelegt,
        /// um nicht mit zukuenftigen Source-Flags zu kollidieren.
        /// </summary>
        CanMoveWhileCasting = 1L << 50,

        /// <summary>
        /// Gerichteter Skillshot (FLARE-Stil): das Projektil fliegt geradlinig
        /// in Cursor-/Blickrichtung und trifft das erste valide Ziel auf seiner
        /// Bahn, statt ein vorab gewaehltes Unit-Ziel zu verfolgen (Homing).
        /// Erfordert <see cref="SpellTemplate.Speed"/> &gt; 0. Bewusst hoch
        /// gelegt, um nicht mit zukuenftigen Source-Flags zu kollidieren.
        /// </summary>
        Skillshot = 1L << 51,
    }
}
