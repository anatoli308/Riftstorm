using System;
using System.Collections.Generic;
using Riftstorm.Game.Combat;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Aura-Flags (Bitmask) fuer Sonderverhalten zur Laufzeit.
    /// </summary>
    [Flags]
    public enum AuraFlags
    {
        /// <summary>Keine Flags.</summary>
        None = 0,
        /// <summary>True = Buff. False = Debuff.</summary>
        Positive = 1 << 0,
        /// <summary>Nicht in der UI anzeigen.</summary>
        Hidden = 1 << 1,
        /// <summary>Passive Aura (kein Icon, kein Cast).</summary>
        Passive = 1 << 2,
        /// <summary>Ueberlebt den Tod (bleibt nach Respawn).</summary>
        Persistent = 1 << 3,
        /// <summary>Kann nicht dispelled werden.</summary>
        CannotDispel = 1 << 4,
        /// <summary>Wurde von einem Item appliziert (z. B. Trinket-Buff).</summary>
        FromItem = 1 << 5,
    }

    /// <summary>
    /// Ein einzelner laufender Effekt-Slot innerhalb einer aktiven <see cref="Aura"/>.
    /// </summary>
    public sealed class AuraEffect
    {
        /// <summary>Spell-Effekt-Klasse (ApplyAura / ApplyAreaAura).</summary>
        public SpellEffect Effect;

        /// <summary>Konkreter Aura-Subtyp (z. B. PeriodicDamage, Stun, ModifyStat).</summary>
        public AuraType AuraType;

        /// <summary>Basis-Wert (Schaden pro Tick, Stat-Delta usw.).</summary>
        public long BaseValue;

        /// <summary>Zusatzwert pro Stack ueber 1 hinaus.</summary>
        public long PerStackValue;

        /// <summary>Zusatz-Daten (Stat-Id, School-Bitmask usw.).</summary>
        public long MiscValue;

        /// <summary>Tick-Interval in Millisekunden. 0 = nicht-periodisch.</summary>
        public int PeriodicIntervalMs;

        /// <summary>Akkumulierter Tick-Timer in Millisekunden.</summary>
        public int PeriodicTimer;
    }

    /// <summary>
    /// Eine aktive Aura (Buff/Debuff) auf einer <see cref="Riftstorm.Gameplay.Combat.ICombatUnit"/>.
    /// </summary>
    public sealed class Aura
    {
        /// <summary>Entry des ausloesenden <see cref="SpellTemplate"/>.</summary>
        public int SourceSpellEntry;

        /// <summary>Guid des Casters, der die Aura angewandt hat. 0 = ohne Quelle.</summary>
        public ulong CasterGuid;

        /// <summary>
        /// Laufzeit-Cache der gecasteten Unit fuer Periodic-Ticks. Vermeidet
        /// teure NetworkObject-/Component-Lookups bei jedem Tick.
        /// </summary>
        public UnitStats CachedCaster;

        /// <summary>Effekt-Slot-Index am Quell-Spell (1..3).</summary>
        public int EffectIndex;

        /// <summary>Gesamtdauer in Millisekunden. 0 = permanent.</summary>
        public int MaxDurationMs;
        /// <summary>Bereits verstrichene Zeit in Millisekunden.</summary>
        public int ElapsedMs;

        /// <summary>Aktuelle Stack-Anzahl.</summary>
        public int Stacks = 1;
        /// <summary>Maximale Stack-Anzahl.</summary>
        public int MaxStacks = 1;

        /// <summary>Verhaltens-Flags.</summary>
        public AuraFlags Flags;

        /// <summary>Dispel-Kategorie.</summary>
        public DispelType DispelType;

        /// <summary>
        /// Bitmaske aus <see cref="SpellTemplate.AuraInterruptFlags"/> &#8212;
        /// steuert, bei welchen Events (z. B. Schaden) die Aura abbricht.
        /// </summary>
        public AuraInterruptFlag InterruptFlags;

        /// <summary>Laufende Effekt-Slots.</summary>
        public List<AuraEffect> Effects = new();

        /// <summary>True, wenn die Aura ein Buff ist.</summary>
        public bool IsPositive => (Flags & AuraFlags.Positive) != 0;

        /// <summary>True, wenn die Aura abgelaufen ist.</summary>
        public bool IsExpired => MaxDurationMs > 0 && ElapsedMs >= MaxDurationMs;

        /// <summary>Verbleibende Dauer in ms. -1 fuer permanente Auren.</summary>
        public int RemainingMs => MaxDurationMs > 0 ? Math.Max(0, MaxDurationMs - ElapsedMs) : -1;

        /// <summary>Skalierter Effekt-Wert: <c>BaseValue + PerStackValue * (Stacks - 1)</c>.</summary>
        public long GetEffectValue(int effectIdx)
        {
            if ((uint)effectIdx >= (uint)Effects.Count)
            {
                return 0;
            }
            AuraEffect e = Effects[effectIdx];
            return e.BaseValue + e.PerStackValue * (Stacks - 1);
        }
    }
}
