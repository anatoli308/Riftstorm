namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Ein einzelner laufender Effekt-Slot innerhalb einer aktiven <see cref="Aura"/>.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>source_server/Server/src/Combat/AuraSystem.h::AuraEffect</c>.
    /// Eine Aura kann mehrere Effekte tragen (z. B. <i>Schaden + Slow</i>),
    /// die alle dieselbe Dauer / dasselbe Stack-Limit teilen, aber individuelle
    /// Tick-Timer haben.
    /// </remarks>
    public sealed class AuraEffect
    {
        /// <summary>Wirkungstyp dieses Slots.</summary>
        public AuraType Type;

        /// <summary>Basis-Effekt-Wert (Schaden pro Tick, Stat-Delta usw.).</summary>
        public int BaseValue;

        /// <summary>Zusätzlicher Wert pro Stack über 1 hinaus.</summary>
        public int PerStackValue;

        /// <summary>
        /// Zusatz-Daten je nach <see cref="Type"/> (z. B. Stat-Id bei
        /// <see cref="AuraType.ModifyStat"/>, School bei
        /// <see cref="AuraType.ModifyResistance"/>).
        /// </summary>
        public int MiscValue;

        /// <summary>Tick-Interval in Millisekunden. 0 = nicht-periodisch.</summary>
        public int PeriodicIntervalMs;

        /// <summary>Akkumulierter Tick-Timer in Millisekunden.</summary>
        public int PeriodicTimer;
    }
}
