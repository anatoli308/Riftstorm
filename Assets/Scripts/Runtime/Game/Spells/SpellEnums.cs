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
    /// Konkreter Mechanic-Subtyp einer <see cref="AuraType.InflictMechanic"/>-Aura.
    /// Steckt in <c>effectN_data2</c> (= <see cref="AuraEffect.MiscValue"/>).
    /// Werte aus den vorhandenen Spell-Templates abgeleitet (Ice Blast=4 "freezes",
    /// Chains of Ice=7 "reduces movement speed", Ice Shard=8 "stuns"); kompatibel
    /// mit dem source_server-Mechanic-Enum.
    /// </summary>
    public enum Mechanic
    {
        None = 0,
        Charm = 1,
        Disorient = 2,
        Disarm = 3,
        /// <summary>Frozen / immobilisiert. Wirkt wie <see cref="AuraType.Root"/>.</summary>
        Frozen = 4,
        Fleeing = 5,
        Grip = 6,
        /// <summary>Snare / Slow. <c>BaseValue</c> (= <c>data3</c>) ist die Speed-Aenderung in %.</summary>
        Snared = 7,
        /// <summary>Stunned. Wirkt wie <see cref="AuraType.Stun"/>.</summary>
        Stunned = 8,
        Sleep = 9,
        Shackle = 10,
        Knockback = 11,
        /// <summary>Silenced. Wirkt wie <see cref="AuraType.Silence"/>.</summary>
        Silenced = 12,
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
    }
}
