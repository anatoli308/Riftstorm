using System;
using System.Collections.Generic;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Npc;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    public sealed partial class UnitStats
    {
        /// <inheritdoc/>
        public void ApplyDamage(in DamageInfo info)
        {
            // Convenience-Overload ohne Attacker — fuer Quellen ohne Caster-Ref
            // (Environment-Damage, Self-Damage, alte Aufrufpfade). Threat wird
            // dann nicht aufgebaut.
            ApplyDamage(null, in info);
        }

        /// <summary>
        /// Variante mit Attacker-Ref. Wird vom Caster-Pfad (Spell/Melee)
        /// genutzt, damit der <see cref="ThreatManager"/>
        /// die Quelle des Schadens zuordnen und Threat aufbauen kann.
        /// </summary>
        /// <param name="attacker">
        /// Angreifer-Unit oder <c>null</c> (Environment / DoT-ohne-Caster).
        /// </param>
        /// <param name="info">Vom <see cref="CombatFormulas"/> vorbereiteter Schaden.</param>
        public void ApplyDamage(UnitStats attacker, in DamageInfo info)
        {
            if (!IsServer)
            {
                return;
            }
            if (IsDead || info.FinalDamage <= 0)
            {
                // Trotzdem den Client-Fanout schicken, damit Miss/Dodge/Block-FX laufen können.
                BroadcastDamageClientRpc(info.FinalDamage, (byte)info.HitResult);
                return;
            }

            // Zentrales SchoolImmunity-Gate: greift fuer JEDEN Schadenspfad
            // (Melee, Spell, DoT-Tick, Environment) — damit auch periodische
            // Auren gegen eine Immunitaet (z. B. Divine Protection) verpuffen.
            // <see cref="DamageInfo.School"/> traegt den Schul-Index; Melee/
            // Waffenschaden nutzt den Default 0 (Physical).
            if (m_Auras != null && m_Auras.HasSchoolImmunity((SpellSchool)info.School))
            {
                m_Auras.TriggerImmuneProc();
                BroadcastDamageClientRpc(0, (byte)HitResult.Immune);
                return;
            }

            // AbsorbDamage-Auren (Bubble, Power Word Shield) ziehen Schaden ab,
            // bevor er auf die HP geht. Schild wird live im AuraManager
            // gedraint und entfernt sich, sobald komplett verbraucht.
            int incomingDamage = info.FinalDamage;
            int absorbed = 0;
            if (m_Auras != null && incomingDamage > 0)
            {
                absorbed = m_Auras.ConsumeAbsorbShield(incomingDamage);
                if (absorbed > 0)
                {
                    incomingDamage -= absorbed;
                }
            }
            if (incomingDamage <= 0)
            {
                // Komplett absorbiert — kein HP-Verlust, aber FX/Threat trotzdem
                // mit dem absorbierten Betrag fanouten (Standard-MOBA-Verhalten).
                BroadcastDamageClientRpc(0, (byte)info.HitResult);
                return;
            }

            int previousHp = m_CurrentHp.Value;
            int newHp = Mathf.Max(0, previousHp - incomingDamage);
            m_CurrentHp.Value = newHp;

            // "Until struck by damage"-Auren (Bind Spirit, Deep Freeze,
            // Blindside) brechen sobald ueberhaupt Schaden landet — vor
            // dem Death-Pfad, damit auch toedlicher Hit konsistent das
            // CC entfernt (relevant fuer Replays/Statesync).
            if (m_Auras != null)
            {
                m_Auras.NotifyDamageTaken();
            }

            BroadcastDamageClientRpc(incomingDamage, (byte)info.HitResult);

            try
            {
                OnServerDamaged?.Invoke(attacker, incomingDamage, info.HitResult);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            if (newHp == 0)
            {
                try
                {
                    OnServerDied?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }

                TryTriggerReincarnationRevive();
            }
        }

        // -------------------------------------------------------------------------
        // Konfiguration (vor Netcode-Spawn)
        // -------------------------------------------------------------------------

    }
}
