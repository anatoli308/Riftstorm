using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Verwaltet alle aktiven <see cref="Aura"/>n einer einzelnen <see cref="ICombatUnit"/>.
    /// Server-authoritative: nur der dedicated Server treibt <see cref="Update"/>.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>source_server/Server/src/Combat/AuraSystem.h::AuraManager</c>.
    /// Reine C#-Klasse (kein MonoBehaviour). Die besitzende Unit hält eine Instanz
    /// als Feld und ruft <see cref="Update"/> aus ihrem Server-Tick auf.
    /// </remarks>
    public sealed class AuraManager
    {
        /// <summary>Max-Buffs (positive Auren).</summary>
        public const int MaxBuffs = 32;
        /// <summary>Max-Debuffs (negative Auren).</summary>
        public const int MaxDebuffs = 16;
        /// <summary>Default-Tick-Intervall für DoT/HoT ohne explizites Interval.</summary>
        public const int DefaultPeriodicIntervalMs = 3000;

        readonly List<Aura> m_Auras = new();
        ICombatUnit m_Owner;
        bool m_Dirty;

        /// <summary>Setzt den Owner (genau einmal nach Konstruktion).</summary>
        public void SetOwner(ICombatUnit owner) => m_Owner = owner;
        /// <summary>Aktuell besitzende Unit (für Effekt-Anwendung).</summary>
        public ICombatUnit Owner => m_Owner;

        /// <summary>Alle aktiven Auren (read-only).</summary>
        public IReadOnlyList<Aura> All => m_Auras;
        /// <summary>Anzahl aktiver Auren (Buff + Debuff).</summary>
        public int Count => m_Auras.Count;

        /// <summary>True, wenn seit dem letzten Broadcast eine Aura geändert wurde.</summary>
        public bool IsDirty => m_Dirty;
        /// <summary>Markiert die Aura-Liste als "muss an Clients gepusht werden".</summary>
        public void MarkDirty() => m_Dirty = true;
        /// <summary>Resettet das Dirty-Flag (nach erfolgtem Broadcast).</summary>
        public void ClearDirty() => m_Dirty = false;

        // =====================================================================
        // Application
        // =====================================================================

        /// <summary>
        /// Wendet eine Aura aus einem Spell-Effect an. Liest die
        /// <see cref="AuraDefinition"/> aus dem geladenen <c>AuraCatalog</c>.
        /// </summary>
        public bool ApplyAuraFromSpell(
            ICombatUnit caster,
            SpellDefinition spell,
            int effectIndex,
            AuraCatalog auraCatalog)
        {
            if (spell == null || auraCatalog == null || m_Owner == null)
            {
                return false;
            }
            if ((uint)effectIndex >= (uint)spell.Effects.Count)
            {
                return false;
            }

            SpellEffectDefinition effDef = spell.Effects[effectIndex];
            if (string.IsNullOrEmpty(effDef.AuraId))
            {
                return false;
            }
            if (!auraCatalog.TryGet(effDef.AuraId, out AuraDefinition auraDef))
            {
                Debug.LogWarning($"[AuraManager] Unbekannte AuraId '{effDef.AuraId}' (Spell '{spell.Id}', Effect #{effectIndex}).");
                return false;
            }

            Aura aura = AuraUtils.CreateAuraFromDefinition(caster, spell, effectIndex, auraDef);
            return ApplyAura(aura);
        }

        /// <summary>
        /// Wendet eine fertig gebaute Aura direkt an (für Script- / Item-Effekte).
        /// Refresht / stackt bestehende Auren desselben Typs.
        /// </summary>
        public bool ApplyAura(Aura newAura)
        {
            if (m_Owner == null || newAura == null || string.IsNullOrEmpty(newAura.AuraId))
            {
                return false;
            }

            if (!CanApplyAura(newAura))
            {
                return false;
            }

            Aura existing = FindStackableAura(newAura.AuraId, newAura.CasterGuid);
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

        /// <summary>
        /// Entfernt eine Aura nach Id. <paramref name="casterGuid"/>=0 entfernt
        /// alle Instanzen unabhängig vom Caster.
        /// </summary>
        public void RemoveAura(string auraId, ulong casterGuid = 0UL)
        {
            int removed = m_Auras.RemoveAll(a =>
                a.AuraId == auraId && (casterGuid == 0UL || a.CasterGuid == casterGuid));
            if (removed > 0)
            {
                MarkDirty();
            }
        }

        /// <summary>Entfernt alle Auren eines bestimmten Casters.</summary>
        public void RemoveAurasFromCaster(ulong casterGuid)
        {
            int removed = m_Auras.RemoveAll(a => a.CasterGuid == casterGuid);
            if (removed > 0)
            {
                MarkDirty();
            }
        }

        /// <summary>Entfernt alle Auren mit mindestens einem Effekt des angegebenen Typs.</summary>
        public void RemoveAurasByType(AuraType type)
        {
            int removed = m_Auras.RemoveAll(a => HasEffectOfType(a, type));
            if (removed > 0)
            {
                MarkDirty();
            }
        }

        /// <summary>
        /// Entfernt alle dispellbaren Auren der angeforderten Polarität.
        /// (<paramref name="positive"/>=true → Buff dispel, false → Debuff dispel).
        /// </summary>
        public void RemoveDispellableAuras(bool positive)
        {
            int removed = m_Auras.RemoveAll(a =>
                a.IsPositive == positive
                && (a.Flags & AuraFlags.CannotDispel) == 0
                && a.DispelType != SpellDispelType.None);
            if (removed > 0)
            {
                MarkDirty();
            }
        }

        /// <summary>
        /// Leert alle Auren (z. B. bei Tod). <paramref name="includePersistent"/>=false
        /// behält <see cref="AuraFlags.Persistent"/>-Auren.
        /// </summary>
        public void ClearAll(bool includePersistent = false)
        {
            int removed;
            if (includePersistent)
            {
                removed = m_Auras.Count;
                m_Auras.Clear();
            }
            else
            {
                removed = m_Auras.RemoveAll(a => (a.Flags & AuraFlags.Persistent) == 0);
            }
            if (removed > 0)
            {
                MarkDirty();
            }
        }

        // =====================================================================
        // Queries
        // =====================================================================

        /// <summary>True, wenn die Unit eine Aura mit dieser Id trägt.</summary>
        public bool HasAura(string auraId)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.AuraId == auraId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn eine Aura desselben Casters mit dieser Id existiert.</summary>
        public bool HasAura(string auraId, ulong casterGuid)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.AuraId == auraId && a.CasterGuid == casterGuid)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn mindestens eine Aura einen Effekt-Slot dieses Typs trägt.</summary>
        public bool HasAuraType(AuraType type)
        {
            foreach (Aura a in m_Auras)
            {
                if (HasEffectOfType(a, type))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Erste Aura mit passender Id oder null.</summary>
        public Aura GetAura(string auraId)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.AuraId == auraId)
                {
                    return a;
                }
            }
            return null;
        }

        /// <summary>Anzahl aktiver Buffs.</summary>
        public int BuffCount
        {
            get
            {
                int n = 0;
                foreach (Aura a in m_Auras)
                {
                    if (a.IsPositive) { n++; }
                }
                return n;
            }
        }

        /// <summary>Anzahl aktiver Debuffs.</summary>
        public int DebuffCount => m_Auras.Count - BuffCount;

        // =====================================================================
        // Modifiers
        // =====================================================================

        /// <summary>
        /// Summe aller Effekt-Werte über aktive Auren mit passendem Effect-Type.
        /// Wird vom Combat-Pipeline-Code für Schadens-/Heilungs-Modifier konsumiert.
        /// </summary>
        public int GetAuraModifier(AuraType type)
        {
            int total = 0;
            foreach (Aura a in m_Auras)
            {
                for (int i = 0; i < a.Effects.Count; i++)
                {
                    if (a.Effects[i].Type == type)
                    {
                        total += a.GetEffectValue(i);
                    }
                }
            }
            return total;
        }

        /// <summary>Stat-Delta aus allen <see cref="AuraType.ModifyStat"/>-Auren für eine StatId.</summary>
        public int GetStatModifier(int statId)
        {
            int total = 0;
            foreach (Aura a in m_Auras)
            {
                for (int i = 0; i < a.Effects.Count; i++)
                {
                    AuraEffect e = a.Effects[i];
                    if (e.Type == AuraType.ModifyStat && e.MiscValue == statId)
                    {
                        total += a.GetEffectValue(i);
                    }
                }
            }
            return total;
        }

        /// <summary>True, wenn ein <see cref="AuraType.Stun"/>-Effekt aktiv ist.</summary>
        public bool IsStunned => HasAuraType(AuraType.Stun);
        /// <summary>True, wenn ein <see cref="AuraType.Silence"/>-Effekt aktiv ist.</summary>
        public bool IsSilenced => HasAuraType(AuraType.Silence);
        /// <summary>True, wenn ein <see cref="AuraType.Root"/>-Effekt aktiv ist.</summary>
        public bool IsRooted => HasAuraType(AuraType.Root);

        // =====================================================================
        // Update (Server-Tick)
        // =====================================================================

        /// <summary>
        /// Treibt alle Auren um <paramref name="deltaTimeMs"/> Millisekunden weiter.
        /// Verarbeitet periodische Ticks und entfernt abgelaufene Auren.
        /// </summary>
        public void Update(int deltaTimeMs)
        {
            if (m_Auras.Count == 0)
            {
                return;
            }

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

        Aura FindStackableAura(string auraId, ulong casterGuid)
        {
            foreach (Aura a in m_Auras)
            {
                if (a.AuraId != auraId)
                {
                    continue;
                }
                // Single-Stack-Auren (MaxStacks=1) refreshen unabhängig vom Caster,
                // gestackte Auren (MaxStacks>1) per Caster (1:1 zum Source-Verhalten).
                if (a.MaxStacks <= 1 || a.CasterGuid == casterGuid)
                {
                    return a;
                }
            }
            return null;
        }

        static bool HasEffectOfType(Aura aura, AuraType type)
        {
            foreach (AuraEffect e in aura.Effects)
            {
                if (e.Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        void ProcessPeriodicEffects(Aura aura, int deltaTimeMs)
        {
            if (m_Owner == null)
            {
                return;
            }
            for (int i = 0; i < aura.Effects.Count; i++)
            {
                AuraEffect e = aura.Effects[i];
                if (e.PeriodicIntervalMs <= 0)
                {
                    continue;
                }
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
            int value = aura.GetEffectValue(effectIdx);
            AuraType type = aura.Effects[effectIdx].Type;
            switch (type)
            {
                case AuraType.PeriodicDamage:
                    m_Owner.TakeDamage(value, null);
                    break;
                case AuraType.PeriodicHeal:
                    m_Owner.Heal(value, null);
                    break;
                case AuraType.PeriodicBurnMana:
                    m_Owner.SetMana(Mathf.Max(0, m_Owner.Mana - value));
                    break;
                case AuraType.PeriodicRestoreMana:
                    m_Owner.SetMana(Mathf.Min(m_Owner.MaxMana, m_Owner.Mana + value));
                    break;
                default:
                    // PeriodicMeleeDamage, Proc, etc. — folgen in späteren Phasen.
                    break;
            }
        }
    }
}
