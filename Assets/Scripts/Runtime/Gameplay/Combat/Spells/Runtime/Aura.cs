using System;
using System.Collections.Generic;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Aura-Flags (Bitmask) für Sonderverhalten zur Laufzeit.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>AuraSystem.h::AuraConfig::Flags</c>.
    /// </remarks>
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
        /// <summary>Überlebt den Tod (bleibt nach Respawn).</summary>
        Persistent = 1 << 3,
        /// <summary>Kann nicht dispelled werden.</summary>
        CannotDispel = 1 << 4,
        /// <summary>Wurde von einem Item appliziert (z. B. Trinket-Buff).</summary>
        FromItem = 1 << 5,
    }

    /// <summary>
    /// Eine aktive Aura (Buff/Debuff) auf einer <see cref="ICombatUnit"/>.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>AuraSystem.h::Aura</c>. Beinhaltet Identifikation,
    /// Restdauer, Stacks und bis zu <see cref="SpellDefinition.MaxEffects"/>
    /// laufende <see cref="AuraEffect"/>s.
    /// </remarks>
    public sealed class Aura
    {
        /// <summary>Schlüssel der Quell-<see cref="AuraDefinition"/> (z. B. <c>"burn"</c>).</summary>
        public string AuraId = string.Empty;
        /// <summary>Schlüssel des auslösenden <see cref="SpellDefinition"/> (z. B. <c>"fireball"</c>).</summary>
        public string SourceSpellId = string.Empty;
        /// <summary>Guid des Casters, der die Aura angewandt hat. 0 = ohne Quelle.</summary>
        public ulong CasterGuid;
        /// <summary>Effekt-Slot-Index am Quell-Spell (0..<see cref="SpellDefinition.MaxEffects"/>-1).</summary>
        public int EffectIndex;

        /// <summary>Gesamtdauer in Millisekunden. 0 = permanent.</summary>
        public int MaxDurationMs;
        /// <summary>Bereits verstrichene Zeit in Millisekunden.</summary>
        public int ElapsedMs;

        /// <summary>Aktuelle Stack-Anzahl.</summary>
        public int Stacks = 1;
        /// <summary>Maximale Stack-Anzahl.</summary>
        public int MaxStacks = 1;

        /// <summary>Verhaltens-Flags der Aura.</summary>
        public AuraFlags Flags;
        /// <summary>Dispel-Kategorie (zur Auflösung bei <see cref="SpellEffectType.Dispel"/>).</summary>
        public SpellDispelType DispelType;
        /// <summary>Crowd-Control-Mechanik (für Mechanic-Immunity-Checks).</summary>
        public SpellMechanic Mechanic;

        /// <summary>Laufende Effekt-Slots (gemäß <see cref="AuraDefinition"/>).</summary>
        public List<AuraEffect> Effects = new();

        /// <summary>True, wenn die Aura ein Buff ist.</summary>
        public bool IsPositive => (Flags & AuraFlags.Positive) != 0;

        /// <summary>True, wenn die Aura abgelaufen ist (Duration erreicht).</summary>
        public bool IsExpired => MaxDurationMs > 0 && ElapsedMs >= MaxDurationMs;

        /// <summary>
        /// Verbleibende Dauer in Millisekunden. -1 für permanente Auren
        /// (entspricht <c>maxDurationMs &lt;= 0</c> aus dem Source).
        /// </summary>
        public int RemainingMs => MaxDurationMs > 0 ? System.Math.Max(0, MaxDurationMs - ElapsedMs) : -1;

        /// <summary>
        /// Skalierter Effekt-Wert für Slot <paramref name="effectIdx"/>:
        /// <c>BaseValue + PerStackValue * (Stacks - 1)</c>.
        /// </summary>
        public int GetEffectValue(int effectIdx)
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
