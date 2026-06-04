using System;
using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Unity.Netcode;
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
    /// <c>Data2 = base value</c> (Per-Tick-Damage bei Periodic, Stat-/School-/
    /// Mechanic-Index bzw. Prozentwert bei Modifier-Auren), <c>Data3 = misc</c>
    /// (selten benutzt; bei <see cref="AuraType.ModifyStatPct"/> bspw. die
    /// Stat-Maske, wenn Data2 den Wert haelt). Duration, Stacks, Tick-Interval
    /// kommen vom Spell-Template selbst. Der finale Per-Tick-/Modifier-Wert
    /// wird beim Apply einmal ueber <see cref="SpellUtils.CalculateEffectValue"/>
    /// (inkl. <c>scale_formula</c>) berechnet und in
    /// <see cref="AuraEffect.BaseValue"/> persistiert.
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
                CachedCaster = caster as UnitStats,
                EffectIndex = oneBasedEffectIndex,
                MaxDurationMs = SpellUtils.CalculateDuration(spell, caster),
                MaxStacks = spell.StackAmount > 0 ? spell.StackAmount : 1,
                DispelType = spell.Dispel,
                InterruptFlags = (AuraInterruptFlag)spell.AuraInterruptFlags,
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
            // Aura-Werte sind DB-typabhaengig: periodische Payload-Auren
            // tragen ihren Betrag in data2, klassische Modifier/Mechanics in
            // data3. SpellUtils kapselt diese Zuordnung, damit Tooltip und
            // Runtime dieselbe Semantik verwenden.
            int baseValue = SpellUtils.CalculateEffectValue(eff, caster);
            aura.Effects.Add(new AuraEffect
            {
                Effect = eff.Effect,
                AuraType = auraType,
                BaseValue = baseValue,
                MiscValue = SpellUtils.GetAuraMiscValue(eff),
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

        /// <summary>
        /// Entfernt alle Auren, deren <see cref="Aura.InterruptFlags"/> auf
        /// erlittenen Schaden reagieren. Wird vom Damage-Pipeline-Hook
        /// (<see cref="UnitStats.ApplyDamage(UnitStats, in DamageInfo)"/>)
        /// aufgerufen, nachdem HP reduziert wurde. Implementiert das
        /// "until struck by damage"-Verhalten von Bind Spirit, Deep Freeze,
        /// Blindside &amp; Co. (DB-Flag <c>aura_interrupt_flags=32</c>).
        /// </summary>
        public void NotifyDamageTaken()
        {
            bool anyRemoved = false;
            for (int i = m_Auras.Count - 1; i >= 0; i--)
            {
                if ((m_Auras[i].InterruptFlags & AuraInterruptFlag.OnDamageTaken) != 0)
                {
                    m_Auras.RemoveAt(i);
                    anyRemoved = true;
                }
            }
            if (anyRemoved) { MarkDirty(); }
        }

        /// <summary>
        /// Entfernt dispellbare Auren des angegebenen Vorzeichens. Optional
        /// kann auf bestimmte <see cref="DispelType"/>-Kategorien gefiltert werden.
        /// </summary>
        /// <param name="removePositive">
        /// <c>true</c> &#8212; entferne Buffs vom Gegner; <c>false</c> &#8212;
        /// entferne Debuffs vom Verbuendeten/Self.
        /// </param>
        /// <param name="dispelMask">
        /// Bitmaske der erlaubten <see cref="DispelType"/>-Kategorien.
        /// 0 oder kleiner = jede Kategorie zulassen.
        /// </param>
        /// <param name="maxCount">
        /// Maximale Anzahl entfernter Auren (0 oder negativ = unbegrenzt).
        /// </param>
        /// <returns>Anzahl tatsaechlich entfernter Auren.</returns>
        public int RemoveDispellable(bool removePositive, int dispelMask = 0, int maxCount = 0)
        {
            int removed = 0;
            for (int i = m_Auras.Count - 1; i >= 0; i--)
            {
                if (maxCount > 0 && removed >= maxCount) { break; }
                Aura aura = m_Auras[i];
                if (aura.IsPositive != removePositive) { continue; }
                if ((aura.Flags & AuraFlags.CannotDispel) != 0) { continue; }
                if (!MatchesDispelMask(aura.DispelType, dispelMask)) { continue; }
                m_Auras.RemoveAt(i);
                removed++;
            }
            if (removed > 0) { MarkDirty(); }
            return removed;
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

        private static bool MatchesDispelMask(DispelType dispelType, int dispelMask)
        {
            if (dispelMask <= 0)
            {
                return true;
            }

            if (dispelType == DispelType.None)
            {
                return false;
            }

            int bit = 1 << ((int)dispelType - 1);
            return (dispelMask & bit) != 0;
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
        /// mit <see cref="Mechanic.Stun"/> (Source-DB-Form).
        /// </summary>
        public bool IsStunned => HasAuraType(AuraType.Stun)
            || HasMechanic(Mechanic.Stun)
            || HasMechanic(Mechanic.Incapacitated)
            || HasMechanic(Mechanic.Polymorph);
        /// <summary>
        /// True, wenn eine Silence-Aura aktiv ist. Beruecksichtigt direkte
        /// <see cref="AuraType.Silence"/>-Auren UND <see cref="AuraType.InflictMechanic"/>
        /// mit <see cref="Mechanic.Silence"/>.
        /// </summary>
        public bool IsSilenced => HasAuraType(AuraType.Silence)
            || HasMechanic(Mechanic.Silence)
            || HasMechanic(Mechanic.Incapacitated)
            || HasMechanic(Mechanic.Polymorph);
        /// <summary>
        /// True, wenn eine Root-Aura aktiv ist. Beruecksichtigt direkte
        /// <see cref="AuraType.Root"/>-Auren UND <see cref="AuraType.InflictMechanic"/>
        /// mit <see cref="Mechanic.Root"/>.
        /// </summary>
        public bool IsRooted => HasAuraType(AuraType.Root)
            || HasMechanic(Mechanic.Root)
            || HasMechanic(Mechanic.Incapacitated)
            || HasMechanic(Mechanic.Polymorph);

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

        /// <summary>
        /// True, wenn mindestens eine <see cref="AuraType.SchoolImmunity"/>-Aura
        /// aktiv ist, deren <see cref="AuraEffect.MiscValue"/> die angefragte
        /// <paramref name="school"/> abdeckt. Wird vom <c>SpellExecutor</c> vor
        /// dem Anwenden von Damage-Effekten geprueft, damit Spells der gleichen
        /// Schule am Ziel komplett verpuffen (z. B. Ice-Block).
        /// </summary>
        public bool HasSchoolImmunity(SpellSchool school)
        {
            long schoolMask = 1L << (int)school;
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    if (e.AuraType != AuraType.SchoolImmunity)
                    {
                        continue;
                    }

                    if (e.MiscValue == (long)school || (e.MiscValue & schoolMask) != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Loest die <see cref="ProcFlags.HolderWasImmune"/>-Procs aus: wird
        /// gerufen, nachdem eine Immunitaet (z. B. physische
        /// <see cref="AuraType.SchoolImmunity"/>) einen Angriff geschluckt hat.
        /// Jede <see cref="AuraType.Proc"/>-Aura, deren
        /// <see cref="AuraEffect.MiscValue"/> das HolderWasImmune-Bit traegt,
        /// verbraucht eine Ladung (<see cref="ProcType.RemoveCharge"/>) und wird
        /// damit entfernt — exakt die Focused-Evasion-Mechanik "evade next
        /// physical attack, then drop". Die Ladungszahl ist im Datenstand 1,
        /// daher entfernt RemoveCharge die gesamte Quell-Aura.
        /// </summary>
        public void TriggerImmuneProc()
        {
            // Erst sammeln, dann entfernen — RemoveAura mutiert m_Auras.
            List<(int spellEntry, ulong casterGuid)> consumed = null;
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    if (e.AuraType != AuraType.Proc
                        || (e.MiscValue & (long)ProcFlags.HolderWasImmune) == 0)
                    {
                        continue;
                    }
                    consumed ??= new List<(int, ulong)>();
                    consumed.Add((a.SourceSpellEntry, a.CasterGuid));
                    break;
                }
            }

            if (consumed == null) { return; }
            foreach ((int spellEntry, ulong casterGuid) in consumed)
            {
                RemoveAura(spellEntry, casterGuid);
            }
        }

        /// <summary>
        /// True, wenn mindestens eine <see cref="AuraType.MechanicImmunity"/>-Aura
        /// aktiv ist, deren <see cref="AuraEffect.MiscValue"/> die angefragte
        /// <paramref name="mechanic"/> abdeckt. Wird vor dem Anwenden einer
        /// <see cref="AuraType.InflictMechanic"/>-Aura geprueft, damit z. B. ein
        /// Mechanic-Immunity-Buff den Stun komplett verschluckt.
        /// </summary>
        public bool HasMechanicImmunity(Mechanic mechanic)
        {
            long mechanicMask = mechanic != Mechanic.None
                ? 1L << ((int)mechanic - 1)
                : 0L;
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    if (e.AuraType != AuraType.MechanicImmunity)
                    {
                        continue;
                    }

                    if (e.MiscValue == (long)mechanic || (mechanicMask != 0L && (e.MiscValue & mechanicMask) != 0))
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
        /// <see cref="Mechanic.Snare"/> (BaseValue = Speed-Delta in %).
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
                                && e.MiscValue == (long)Mechanic.Snare);
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
        // Modifier-Aggregation (Damage / Healing / Stat / Absorb)
        // =====================================================================

        /// <summary>
        /// Summiert die effektiven Werte aller aktiven Auren-Effekte vom
        /// angefragten <paramref name="auraType"/> (additiver Stack inkl.
        /// <c>PerStackValue * (Stacks-1)</c>). Wird vom Damage-/Heal-Pfad
        /// genutzt, um <see cref="AuraType.ModifyDamageDealtPct"/>,
        /// <see cref="AuraType.ModifyDamageReceivedPct"/>,
        /// <see cref="AuraType.ModifyHealingDealtPct"/>,
        /// <see cref="AuraType.ModifyHealingRecvPct"/>,
        /// <see cref="AuraType.ModifyMeleeSpeedPct"/> und
        /// <see cref="AuraType.AbsorbDamage"/> zu lesen.
        /// </summary>
        /// <remarks>
        /// Source-Aequivalent: <c>AuraSystem::getAuraModifier</c>
        /// (<c>source_server/Server/src/Combat/AuraSystem.cpp</c>).
        /// </remarks>
        public int GetAuraModifierTotal(AuraType auraType)
        {
            long sum = 0;
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    if (e.AuraType == auraType)
                    {
                        sum += e.BaseValue + e.PerStackValue * (a.Stacks - 1);
                    }
                }
            }
            if (sum > int.MaxValue) { return int.MaxValue; }
            if (sum < int.MinValue) { return int.MinValue; }
            return (int)sum;
        }

        /// <summary>
        /// Summiert <see cref="AuraType.ModifyStat"/>-/
        /// <see cref="AuraType.ModifyStatPct"/>-Effekte, deren
        /// <see cref="AuraEffect.MiscValue"/> die angefragte
        /// <paramref name="statMask"/> per Bitwise-AND abdeckt. <c>Data2</c>
        /// im Source-Format ist eine Stat-Bitmask (z. B. <c>8192</c> =
        /// Cooldown-Reduktion, <c>1</c> = Strength). Wird vom Stat-Lookup
        /// (z. B. <c>PlayerCombat.GetSpellCooldownMultiplier</c>) aufgerufen.
        /// </summary>
        /// <remarks>
        /// Source-Aequivalent: <c>AuraSystem::getStatModifier</c>
        /// (<c>source_server/Server/src/Combat/AuraSystem.cpp</c>).
        /// </remarks>
        public int GetStatModifierTotal(int statMask)
        {
            return SumStatModifier(statMask, includeFlat: true, includePct: true);
        }

        /// <summary>
        /// Summiert ausschliesslich <b>flache</b>
        /// <see cref="AuraType.ModifyStat"/>-Effekte (additive Punkte, z. B.
        /// <c>-1 Strength</c>), deren <see cref="AuraEffect.MiscValue"/> die
        /// angefragte <paramref name="statMask"/> abdeckt. Wird vom
        /// effektiven Attribut-Lookup in <see cref="UnitStats"/> getrennt von
        /// den prozentualen Modifikatoren konsumiert, damit die Reihenfolge
        /// <c>(Basis + Flat) * (1 + Pct/100)</c> sauber eingehalten wird.
        /// </summary>
        public int GetStatFlatModifierTotal(int statMask)
        {
            return SumStatModifier(statMask, includeFlat: true, includePct: false);
        }

        /// <summary>
        /// Summiert ausschliesslich <b>prozentuale</b>
        /// <see cref="AuraType.ModifyStatPct"/>-Effekte (z. B. <c>-2 %</c> auf
        /// alle Primaerattribute), deren <see cref="AuraEffect.MiscValue"/> die
        /// angefragte <paramref name="statMask"/> abdeckt. Gegenstueck zu
        /// <see cref="GetStatFlatModifierTotal"/>.
        /// </summary>
        public int GetStatPctModifierTotal(int statMask)
        {
            return SumStatModifier(statMask, includeFlat: false, includePct: true);
        }

        /// <summary>
        /// Gemeinsamer Summierungs-Kern fuer flache und/oder prozentuale
        /// Stat-Modifier-Auren. Beruecksichtigt nur Effekte, deren
        /// <see cref="AuraEffect.MiscValue"/> mindestens ein Bit der
        /// <paramref name="statMask"/> traegt, und addiert deren
        /// stack-skalierten Wert.
        /// </summary>
        /// <param name="statMask">Stat-Bitmaske nach Source-Konvention
        /// (<c>1 &lt;&lt; (StatId - 1)</c>).</param>
        /// <param name="includeFlat">True, um <see cref="AuraType.ModifyStat"/>
        /// (additive Punkte) einzurechnen.</param>
        /// <param name="includePct">True, um <see cref="AuraType.ModifyStatPct"/>
        /// (Prozentwerte) einzurechnen.</param>
        private int SumStatModifier(int statMask, bool includeFlat, bool includePct)
        {
            if (statMask == 0) { return 0; }
            long sum = 0;
            foreach (Aura a in m_Auras)
            {
                foreach (AuraEffect e in a.Effects)
                {
                    bool isStatMod = (includeFlat && e.AuraType == AuraType.ModifyStat)
                        || (includePct && e.AuraType == AuraType.ModifyStatPct);
                    if (isStatMod && (e.MiscValue & statMask) != 0)
                    {
                        sum += e.BaseValue + e.PerStackValue * (a.Stacks - 1);
                    }
                }
            }
            if (sum > int.MaxValue) { return int.MaxValue; }
            if (sum < int.MinValue) { return int.MinValue; }
            return (int)sum;
        }

        /// <summary>
        /// Verbraucht aus aktiven <see cref="AuraType.AbsorbDamage"/>-Auren
        /// (Schilden) bis zu <paramref name="incomingDamage"/> Punkte und gibt
        /// die absorbierte Menge zurueck. Aura-Effekte werden nach Verbrauch
        /// dekrementiert; vollstaendig aufgezehrte Schilde werden entfernt.
        /// Wird vom Damage-Pipeline-Hook in <see cref="UnitStats.ApplyDamage"/>
        /// vor der HP-Reduktion aufgerufen.
        /// </summary>
        public int ConsumeAbsorbShield(int incomingDamage)
        {
            if (incomingDamage <= 0 || m_Auras.Count == 0) { return 0; }
            int remaining = incomingDamage;
            int absorbed = 0;
            bool anyDepleted = false;
            for (int ai = 0; ai < m_Auras.Count; ai++)
            {
                Aura a = m_Auras[ai];
                for (int ei = 0; ei < a.Effects.Count; ei++)
                {
                    AuraEffect e = a.Effects[ei];
                    if (e.AuraType != AuraType.AbsorbDamage || e.BaseValue <= 0) { continue; }
                    long poolL = e.BaseValue + e.PerStackValue * (a.Stacks - 1);
                    if (poolL <= 0) { continue; }
                    int pool = poolL > int.MaxValue ? int.MaxValue : (int)poolL;
                    int take = Mathf.Min(remaining, pool);
                    e.BaseValue -= take;
                    absorbed += take;
                    remaining -= take;
                    if (e.BaseValue <= 0)
                    {
                        anyDepleted = true;
                    }
                    if (remaining <= 0) { break; }
                }
                if (remaining <= 0) { break; }
            }
            if (anyDepleted)
            {
                m_Auras.RemoveAll(a =>
                {
                    foreach (AuraEffect e in a.Effects)
                    {
                        if (e.AuraType == AuraType.AbsorbDamage && e.BaseValue <= 0)
                        {
                            return true;
                        }
                    }
                    return false;
                });
                MarkDirty();
            }
            return absorbed;
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
                int periodicDeltaMs = deltaTimeMs;
                if (a.MaxDurationMs > 0)
                {
                    int remainingMs = Math.Max(0, a.MaxDurationMs - a.ElapsedMs);
                    periodicDeltaMs = Math.Min(periodicDeltaMs, remainingMs);
                }

                if (periodicDeltaMs > 0)
                {
                    ProcessPeriodicEffects(a, periodicDeltaMs);
                }

                if (a.MaxDurationMs > 0)
                {
                    a.ElapsedMs += deltaTimeMs;
                    if (a.IsExpired)
                    {
                        anyExpired = true;
                    }
                }
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
            if (IsImmuneToAuraMechanic(aura))
            {
                return false;
            }
            int buffs = BuffCount;
            int debuffs = m_Auras.Count - buffs;
            return aura.IsPositive ? buffs < MaxBuffs : debuffs < MaxDebuffs;
        }

        /// <summary>
        /// True, wenn der Besitzer per <see cref="UnitStats.MechanicImmuneMask"/> gegen
        /// eine in <paramref name="aura"/> getragene Crowd-Control-Mechanik immun ist.
        /// Mappt sowohl <see cref="AuraType.InflictMechanic"/> (Mechanik in
        /// <see cref="AuraEffect.MiscValue"/>) als auch die direkten CC-Aura-Typen
        /// (<see cref="AuraType.Stun"/>/<see cref="AuraType.Root"/>/<see cref="AuraType.Silence"/>)
        /// auf das passende <see cref="Mechanic"/>-Bit. Source-Pendant: das in
        /// GameData geladene, aber nie geprueffte <c>mechanicImmuneMask</c> — hier
        /// erstmals scharfgeschaltet.
        /// </summary>
        bool IsImmuneToAuraMechanic(Aura aura)
        {
            if (aura == null || aura.IsPositive)
            {
                return false;
            }
            int immuneMask = (m_Owner as UnitStats)?.MechanicImmuneMask ?? 0;
            if (immuneMask == 0)
            {
                return false;
            }
            for (int i = 0; i < aura.Effects.Count; i++)
            {
                Mechanic mechanic = ResolveEffectMechanic(aura.Effects[i]);
                if (mechanic == Mechanic.None)
                {
                    continue;
                }
                int bit = 1 << ((int)mechanic - 1);
                if ((immuneMask & bit) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Bestimmt die Crowd-Control-Mechanik eines Aura-Effekts fuer den
        /// Immunitaets-Check. <see cref="AuraType.InflictMechanic"/> traegt den
        /// <see cref="Mechanic"/>-Index in <see cref="AuraEffect.MiscValue"/>; die
        /// dedizierten CC-Typen mappen direkt.
        /// </summary>
        static Mechanic ResolveEffectMechanic(AuraEffect effect)
        {
            switch (effect.AuraType)
            {
                case AuraType.InflictMechanic:
                    return (Mechanic)effect.MiscValue;
                case AuraType.Stun:
                    return Mechanic.Stun;
                case AuraType.Root:
                    return Mechanic.Root;
                case AuraType.Silence:
                    return Mechanic.Silence;
                default:
                    return Mechanic.None;
            }
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
                    ApplyPeriodicDamage(aura, e, (int)value);
                    break;
                case AuraType.PeriodicHeal:
                    ApplyPeriodicHeal(aura, (int)value);
                    break;
                case AuraType.PeriodicHealPct:
                    ApplyPeriodicHeal(aura, (int)(m_Owner.MaxHealth * value / 100));
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

        void ApplyPeriodicDamage(Aura aura, AuraEffect effect, int baseValue)
        {
            if (m_Owner == null || baseValue <= 0)
            {
                return;
            }

            ICombatUnit caster = ResolveAuraCaster(aura);
            m_Owner.TakeDamage(baseValue, caster);
        }

        void ApplyPeriodicHeal(Aura aura, int baseValue)
        {
            if (m_Owner == null || baseValue <= 0)
            {
                return;
            }

            ICombatUnit caster = ResolveAuraCaster(aura);
            m_Owner.Heal(baseValue, caster);
        }

        static ICombatUnit ResolveAuraCaster(Aura aura)
        {
            if (aura == null || aura.CasterGuid == 0UL)
            {
                return null;
            }

            if (aura.CachedCaster != null)
            {
                UnityEngine.Object cachedObject = aura.CachedCaster;
                if (cachedObject != null)
                {
                    return aura.CachedCaster;
                }

                aura.CachedCaster = null;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || network.SpawnManager == null)
            {
                return null;
            }

            if (!network.SpawnManager.SpawnedObjects.TryGetValue(aura.CasterGuid, out NetworkObject sourceObject)
                || sourceObject == null)
            {
                return null;
            }

            UnitStats resolved = sourceObject.GetComponent<UnitStats>()
                ?? sourceObject.GetComponentInChildren<UnitStats>();
            aura.CachedCaster = resolved;
            return resolved;
        }
    }
}
