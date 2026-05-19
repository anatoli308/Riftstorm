using System;
using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Verwaltet alle aktiven <see cref="Aura"/>n einer einzelnen
    /// <see cref="ICombatUnit"/>. Server-authoritative.
    /// </summary>
    /// <remarks>
    /// Auren werden direkt aus <see cref="SpellTemplate"/>-Effekt-Slots
    /// instanziiert (kein separater Aura-Katalog mehr). Effekt-Slot-Konvention
    /// fuer <see cref="SpellEffect.ApplyAura"/>: <c>Data1 = AuraType</c>,
    /// <c>Data2 = misc/stat mask</c>, <c>Data3 = base value</c>. Duration,
    /// Stacks, Tick-Interval kommen vom Spell-Template selbst.
    /// </remarks>
    public sealed class AuraManager
    {
        /// <summary>Max-Buffs (positive Auren).</summary>
        public const int MaxBuffs = 32;
        /// <summary>Max-Debuffs (negative Auren).</summary>
        public const int MaxDebuffs = 16;

        readonly List<Aura> m_Auras = new();
        ICombatUnit m_Owner;
        bool m_Dirty;

        /// <summary>Setzt den Owner (genau einmal nach Konstruktion).</summary>
        public void SetOwner(ICombatUnit owner) => m_Owner = owner;
        /// <summary>Aktuell besitzende Unit.</summary>
        public ICombatUnit Owner => m_Owner;

        /// <summary>Alle aktiven Auren (read-only).</summary>
        public IReadOnlyList<Aura> All => m_Auras;
        /// <summary>Anzahl aktiver Auren.</summary>
        public int Count => m_Auras.Count;

        /// <summary>True, wenn seit dem letzten Broadcast eine Aura geaendert wurde.</summary>
        public bool IsDirty => m_Dirty;
        /// <summary>Markiert die Aura-Liste als dirty.</summary>
        public void MarkDirty()
        {
            m_Dirty = true;
            try
            {
                OnChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
        /// <summary>Resettet das Dirty-Flag.</summary>
        public void ClearDirty() => m_Dirty = false;

        /// <summary>
        /// Server-seitiges Event: feuert genau dann, wenn sich die Aura-Liste
        /// strukturell aendert (Apply / Refresh / Stack / Remove / Expire).
        /// Wird von <see cref="UnitStats"/> abonniert, um einen Aura-Snapshot
        /// an alle Clients zu broadcasten. Periodische Tick-Updates loesen
        /// das Event NICHT aus.
        /// </summary>
        public event Action OnChanged;

        // =====================================================================
        // Application
        // =====================================================================

        /// <summary>
        /// Wendet eine Aura aus einem Spell-Effect-Slot an. Slot muss
        /// <see cref="SpellEffect.ApplyAura"/> oder <see cref="SpellEffect.ApplyAreaAura"/>
        /// sein.
        /// </summary>
        public bool ApplyAuraFromSpell(ICombatUnit caster, SpellTemplate spell, int oneBasedEffectIndex)
        {
            if (spell == null || m_Owner == null)
            {
                return false;
            }
            SpellTemplateEffect eff = spell.GetEffect(oneBasedEffectIndex);
            if (!eff.IsActive || !IsApplyAuraEffect(eff.Effect))
            {
                return false;
            }

            AuraType auraType = (AuraType)eff.Data1;
            Aura aura = new()
            {
                SourceSpellEntry = spell.Entry,
                CasterGuid = caster != null ? caster.Guid : 0UL,
                EffectIndex = oneBasedEffectIndex,
                MaxDurationMs = spell.Duration,
                MaxStacks = spell.StackAmount > 0 ? spell.StackAmount : 1,
                DispelType = spell.Dispel,
            };
            if (eff.Positive)
            {
                aura.Flags |= AuraFlags.Positive;
            }
            if ((spell.Attributes & SpellAttributes.Passive) != 0)
            {
                aura.Flags |= AuraFlags.Passive;
            }
            if ((spell.Attributes & SpellAttributes.PersistsThroughDeath) != 0)
            {
                aura.Flags |= AuraFlags.Persistent;
            }
            aura.Effects.Add(new AuraEffect
            {
                Effect = eff.Effect,
                AuraType = auraType,
                BaseValue = eff.Data3,
                MiscValue = eff.Data2,
                PerStackValue = 0,
                PeriodicIntervalMs = spell.Interval,
            });

            return ApplyAura(aura);
        }

        /// <summary>
        /// Wendet eine fertig gebaute Aura an. Refresht / stackt bestehende
        /// Auren gleicher Quelle.
        /// </summary>
        public bool ApplyAura(Aura newAura)
        {
            if (m_Owner == null || newAura == null || newAura.SourceSpellEntry <= 0)
            {
                return false;
            }
            if (!CanApplyAura(newAura))
            {
                return false;
            }

            Aura existing = FindStackableAura(newAura.SourceSpellEntry, newAura.CasterGuid);
            if (existing != null)
            {
                existing.ElapsedMs = 0;
                if (existing.Stacks < existing.MaxStacks)
                {
                    existing.Stacks++;
                }
                MarkDirty();
                return true;
            }

            m_Auras.Add(newAura);
            MarkDirty();
            return true;
        }

        /// <summary>Entfernt Auren nach Quell-Spell-Entry.</summary>
        public void RemoveAura(int sourceSpellEntry, ulong casterGuid = 0UL)
        {
            int removed = m_Auras.RemoveAll(a =>
                a.SourceSpellEntry == sourceSpellEntry
                && (casterGuid == 0UL || a.CasterGuid == casterGuid));
            if (removed > 0) { MarkDirty(); }
        }

        /// <summary>Entfernt alle Auren eines bestimmten Casters.</summary>
        public void RemoveAurasFromCaster(ulong casterGuid)
        {
            int removed = m_Auras.RemoveAll(a => a.CasterGuid == casterGuid);
            if (removed > 0) { MarkDirty(); }
        }

        /// <summary>Leert alle Auren (z. B. bei Tod).</summary>
        public void ClearAll(bool includePersistent = false)
        {
            int before = m_Auras.Count;
            if (includePersistent)
            {
                m_Auras.Clear();
            }
            else
            {
                m_Auras.RemoveAll(a => (a.Flags & AuraFlags.Persistent) == 0);
            }
            if (m_Auras.Count != before) { MarkDirty(); }
        }

        // =====================================================================
        // Queries
        // =====================================================================

        /// <summary>True, wenn die Unit eine Aura aus dem genannten Spell traegt.</summary>
        public bool HasAura(int sourceSpellEntry)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.SourceSpellEntry == sourceSpellEntry) { return true; }
            }
            return false;
        }

        /// <summary>True, wenn mindestens eine Aura einen Effekt-Subtyp dieses <see cref="AuraType"/> traegt.</summary>
        public bool HasAuraType(AuraType auraType)
        {
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    if (e.AuraType == auraType) { return true; }
                }
            }
            return false;
        }

        /// <summary>Erste Aura mit passender Quell-Spell-Entry oder null.</summary>
        public Aura GetAura(int sourceSpellEntry)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.SourceSpellEntry == sourceSpellEntry) { return a; }
            }
            return null;
        }

        /// <summary>Anzahl aktiver Buffs.</summary>
        public int BuffCount
        {
            get
            {
                int n = 0;
                foreach (Aura a in m_Auras) { if (a.IsPositive) { n++; } }
                return n;
            }
        }

        /// <summary>Anzahl aktiver Debuffs.</summary>
        public int DebuffCount => m_Auras.Count - BuffCount;

        /// <summary>
        /// True, wenn eine Stun-Aura aktiv ist. Beruecksichtigt direkte
        /// <see cref="AuraType.Stun"/>-Auren UND <see cref="AuraType.InflictMechanic"/>
        /// mit <see cref="Mechanic.Stunned"/> (Source-DB-Form).
        /// </summary>
        public bool IsStunned => HasAuraType(AuraType.Stun) || HasMechanic(Mechanic.Stunned);
        /// <summary>
        /// True, wenn eine Silence-Aura aktiv ist. Beruecksichtigt direkte
        /// <see cref="AuraType.Silence"/>-Auren UND <see cref="AuraType.InflictMechanic"/>
        /// mit <see cref="Mechanic.Silenced"/>.
        /// </summary>
        public bool IsSilenced => HasAuraType(AuraType.Silence) || HasMechanic(Mechanic.Silenced);
        /// <summary>
        /// True, wenn eine Root-Aura aktiv ist. Beruecksichtigt direkte
        /// <see cref="AuraType.Root"/>-Auren UND <see cref="AuraType.InflictMechanic"/>
        /// mit <see cref="Mechanic.Frozen"/>.
        /// </summary>
        public bool IsRooted => HasAuraType(AuraType.Root) || HasMechanic(Mechanic.Frozen);

        /// <summary>
        /// True, wenn mindestens eine <see cref="AuraType.InflictMechanic"/>-Aura
        /// mit dem angegebenen <paramref name="mechanic"/>-Wert in
        /// <see cref="AuraEffect.MiscValue"/> aktiv ist.
        /// </summary>
        public bool HasMechanic(Mechanic mechanic)
        {
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    if (e.AuraType == AuraType.InflictMechanic && e.MiscValue == (long)mechanic)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        /// <summary>True, wenn Stun ODER Root aktiv ist (Bewegung verboten).</summary>
        public bool IsImmobilized => IsStunned || IsRooted;

        /// <summary>
        /// Aggregierter Move-Speed-Multiplikator aus allen aktiven
        /// <see cref="AuraType.ModifyMoveSpeedPct"/>-Effekten sowie
        /// <see cref="AuraType.InflictMechanic"/>-Auren mit
        /// <see cref="Mechanic.Snared"/> (BaseValue = Speed-Delta in %).
        /// Additive Summation (SoF-/WoW-Style):
        /// <c>multiplier = 1 + sum(BaseValue + PerStackValue*(Stacks-1)) / 100</c>.
        /// Negative <c>BaseValue</c> = Snare/Slow, positive = Haste. Clamped auf
        /// <c>[0, 5]</c> &#8212; 0 ergibt zwar einen vollstaendigen Stop (gleichwertig zu
        /// Root), 5x ist die obere Schranke gegen kaputte Datenwerte.
        /// </summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                long sumPct = 0;
                foreach (Aura a in m_Auras)
                {
                    foreach (AuraEffect e in a.Effects)
                    {
                        bool isMoveSpeedMod = e.AuraType == AuraType.ModifyMoveSpeedPct
                            || (e.AuraType == AuraType.InflictMechanic
                                && e.MiscValue == (long)Mechanic.Snared);
                        if (isMoveSpeedMod)
                        {
                            sumPct += e.BaseValue + e.PerStackValue * (a.Stacks - 1);
                        }
                    }
                }
                float mult = 1f + sumPct / 100f;
                if (mult < 0f) { mult = 0f; }
                if (mult > 5f) { mult = 5f; }
                return mult;
            }
        }

        // =====================================================================
        // Update (Server-Tick)
        // =====================================================================

        /// <summary>Treibt alle Auren um <paramref name="deltaTimeMs"/> ms weiter.</summary>
        public void Update(int deltaTimeMs)
        {
            if (m_Auras.Count == 0) { return; }

            bool anyExpired = false;
            for (int i = 0; i < m_Auras.Count; i++)
            {
                Aura a = m_Auras[i];
                if (a.MaxDurationMs > 0)
                {
                    a.ElapsedMs += deltaTimeMs;
                    if (a.IsExpired)
                    {
                        anyExpired = true;
                        continue;
                    }
                }
                ProcessPeriodicEffects(a, deltaTimeMs);
            }

            if (anyExpired)
            {
                m_Auras.RemoveAll(a => a.IsExpired);
                MarkDirty();
            }
        }

        // =====================================================================
        // Internals
        // =====================================================================

        bool CanApplyAura(Aura aura)
        {
            int buffs = BuffCount;
            int debuffs = m_Auras.Count - buffs;
            return aura.IsPositive ? buffs < MaxBuffs : debuffs < MaxDebuffs;
        }

        Aura FindStackableAura(int sourceSpellEntry, ulong casterGuid)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.SourceSpellEntry != sourceSpellEntry) { continue; }
                if (a.MaxStacks <= 1 || a.CasterGuid == casterGuid)
                {
                    return a;
                }
            }
            return null;
        }

        static bool IsApplyAuraEffect(SpellEffect effect)
            => effect == SpellEffect.ApplyAura || effect == SpellEffect.ApplyAreaAura;

        void ProcessPeriodicEffects(Aura aura, int deltaTimeMs)
        {
            if (m_Owner == null) { return; }
            for (int i = 0; i < aura.Effects.Count; i++)
            {
                AuraEffect e = aura.Effects[i];
                if (e.PeriodicIntervalMs <= 0) { continue; }

                e.PeriodicTimer += deltaTimeMs;
                while (e.PeriodicTimer >= e.PeriodicIntervalMs)
                {
                    e.PeriodicTimer -= e.PeriodicIntervalMs;
                    ApplyPeriodicTick(aura, i);
                }
            }
        }

        void ApplyPeriodicTick(Aura aura, int effectIdx)
        {
            AuraEffect e = aura.Effects[effectIdx];
            long value = aura.GetEffectValue(effectIdx);
            if (value <= 0) { return; }

            switch (e.AuraType)
            {
                case AuraType.PeriodicDamage:
                case AuraType.PeriodicMeleeDamage:
                    m_Owner.TakeDamage((int)value, null);
                    break;
                case AuraType.PeriodicHeal:
                    m_Owner.Heal((int)value, null);
                    break;
                case AuraType.PeriodicHealPct:
                    m_Owner.Heal((int)(m_Owner.MaxHealth * value / 100), null);
                    break;
                case AuraType.PeriodicRestoreMana:
                    m_Owner.SetMana(Mathf.Min(m_Owner.MaxMana, m_Owner.Mana + (int)value));
                    break;
                case AuraType.PeriodicRestoreManaPct:
                    m_Owner.SetMana(Mathf.Min(m_Owner.MaxMana, m_Owner.Mana + (int)(m_Owner.MaxMana * value / 100)));
                    break;
                case AuraType.PeriodicBurnMana:
                    m_Owner.SetMana(Mathf.Max(0, m_Owner.Mana - (int)value));
                    break;
            }
        }
    }
}
