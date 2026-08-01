using System;
using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    public sealed partial class NpcController
    {
        /// <summary>
        /// Port von <c>NpcAI::updateIdle</c>: nur Hostile-Faction sucht aktiv
        /// nach Targets. Neutral/Friendly bleiben Idle, bis sie per
        /// Retaliation in <see cref="HandleServerDamaged"/> Threat auf den
        /// Angreifer aufbauen und damit nach Combat wechseln.
        /// </summary>
        /// <remarks>
        /// Der Aggro-Scan setzt nicht mehr direkt <see cref="m_CurrentTarget"/>,
        /// sondern speist den gefundenen Spieler in den
        /// <see cref="ThreatManager"/> ein (initialer Threat = 1). Damit läuft
        /// das Target-Picking in <see cref="UpdateCombat"/> einheitlich über
        /// die Threat-Tabelle — und späterer Schaden anderer Spieler kann den
        /// Aggro-Pull ohne Sonderpfad überholen.
        /// </remarks>
        private void UpdateIdle(float dt)
        {
            if (!IsHostileFaction(m_Faction))
            {
                return;
            }

            UnitStats target = FindAggroTarget();
            if (target != null)
            {
                m_Threat.AddThreat(target.NetworkObjectId, 1);
                MirrorThreatToSource(target.NetworkObjectId);
                m_CurrentTarget = target;
                m_State = NpcAIState.Combat;
                PlayAggroClientRpc();
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::updateCombat</c>: Leash-Check, in-Range =&gt; Attack,
        /// sonst auf Target zulaufen. Target-Verlust (tot / despawned) =&gt; Idle.
        /// </summary>
        /// <remarks>
        /// Target-Picking läuft jetzt über <see cref="ThreatManager"/>: das
        /// Top-Threat-Target wird pro Tick angefragt. Wenn die Threat-Tabelle
        /// leer wird (alle Einträge gepruned), kehrt der NPC zurück in Idle
        /// und setzt sein <see cref="m_CurrentTarget"/> zurück.
        /// </remarks>
        private void UpdateCombat(float dt)
        {
            UnitStats top = m_Threat.GetHighestThreat(NetworkManager.Singleton);
            if (top != null)
            {
                m_CurrentTarget = top;
            }
            else if (!IsValidTarget(m_CurrentTarget))
            {
                // Threat-Tabelle leer und kein gültiges Sticky-Target mehr.
                m_CurrentTarget = null;
                m_State = NpcAIState.Idle;
                return;
            }

            if (ShouldLeash())
            {
                // Source: beim Leash werden alle Threat-Einträge gedroppt,
                // damit der NPC nach dem Reset nicht sofort wieder vom
                // ursprünglichen Angreifer ge-pullt wird.
                ClearThreatAndNotify();
                m_CurrentTarget = null;
                m_State = NpcAIState.Evading;
                return;
            }

            // Riftstorm-Erweiterung gegenueber Source-Parity: solange ein
            // gueltiges Combat-Target existiert, schaut der NPC IMMER zum
            // Target — auch im Melee-Stand und waehrend eines Spell-Casts.
            // Damit verhaelt sich der NPC analog zum Spieler, der sich beim
            // Treffer/Attack-Aim zum Angreifer/Ziel ausrichtet. MoveTowardsEntity
            // ueberschreibt diesen Vektor beim Chase mit demselben Wert, der
            // Melee-Freeze (NpcAI.cpp Z.640-642) wird hier bewusst aufgeweicht,
            // damit das Sprite auch bei umrundenden Spielern dreht.
            if (IsValidTarget(m_CurrentTarget))
            {
                Vector3 toTarget = m_CurrentTarget.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    m_ServerFacingVec = toTarget;
                }
            }

            // CC-Gates: ein gestunnter NPC macht in diesem Tick gar nichts;
            // ein nur gerooteter / immobilisierter NPC darf weiter attackieren
            // und casten, kann sich aber nicht bewegen. Snare/Haste wirken
            // multiplikativ auf die Walk-Speed.
            bool stunned = m_Stats.IsStunned;
            bool immobilized = m_Stats.IsImmobilized;

            // Aktiver Cast-Time-Spell hat Vorrang: der NPC channelt, steht still
            // und attackt nicht (wie der Spieler im CastingState). Ein Stun
            // bricht den Cast ab. Solange gecastet wird, kein Move/Melee/neuer
            // Spell in diesem Tick.
            if (m_CastInProgress)
            {
                if (stunned)
                {
                    CancelActiveCast();
                }
                else
                {
                    TickActiveCast();
                    return;
                }
            }

            if (!stunned)
            {
                if (IsInMeleeRange(m_CurrentTarget))
                {
                    TryMeleeAttack(m_CurrentTarget);
                }
                else if (HasRangedWeapon && IsInRangedRange(m_CurrentTarget))
                {
                    // Ausserhalb Melee, aber in Ranged-Reichweite: stehen und
                    // schiessen (kein Kiting). Source hatte keine Ranged-AI; das
                    // ist eine Riftstorm-Erweiterung.
                    TryRangedAttack(m_CurrentTarget);
                }
                else if (!immobilized)
                {
                    float chaseSpeed = m_Stats.WalkSpeed * m_Stats.MoveSpeedMultiplier;
                    MoveTowardsEntity(m_CurrentTarget, dt, chaseSpeed);
                }

                int slotIndex = SelectSpellSlotToCast(m_CurrentTarget);
                if (slotIndex >= 0)
                {
                    BeginOrResolveSpellCast(slotIndex, m_CurrentTarget);
                }
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::updateEvading</c>: mit doppelter Speed zum
        /// <see cref="m_HomePosition"/>, bei Ankunft Full-HP-Reset und zurueck
        /// in den Idle-State. Auren-Clear ist hier (noch) nicht implementiert.
        /// </summary>
        private void UpdateEvading(float dt)
        {
            // Stun/Root verhindern auch die Rueckkehr zum Home-Punkt.
            // Snare/Haste skalieren die Evade-Speed.
            if (m_Stats.IsImmobilized)
            {
                return;
            }
            float speed = m_Stats.WalkSpeed * m_EvadeSpeedMultiplier * m_Stats.MoveSpeedMultiplier;
            MoveTowardsPoint(m_HomePosition, dt, speed);

            if (Vector3.Distance(transform.position, m_HomePosition) <= m_HomeArrivalDistance)
            {
                transform.position = m_HomePosition;
                // Source: NpcAI::updateEvading setzt HP/Mana zurueck und clearst Auren.
                // Auren-Clear folgt mit dem Buff/Debuff-Pass.
                m_Stats.ServerResetHp();
                m_Stats.ServerResetMana();
                ResetSpellRuntimeTimers();
                m_State = NpcAIState.Idle;
            }
        }

        /// <summary>
        /// Fear-Verhalten ("Fleeing in terror", z. B. <c>Satanic Madness</c>):
        /// solange eine <see cref="Mechanic.Fear"/>-Aura aktiv ist, laeuft der
        /// NPC vom Verursacher weg statt zu attackieren oder zu casten. Ein
        /// laufender Cast wird abgebrochen. Ein zusaetzlicher Stun/Root friert
        /// die Flucht ein; Snare/Haste skalieren das Fluchttempo. Der
        /// Combat-/Threat-State bleibt unangetastet, damit der NPC nach Ablauf
        /// des Fears den Kampf normal fortsetzt.
        /// </summary>
        private void UpdateFeared(float dt)
        {
            // Ein gefuerchteter NPC channelt nicht — laufenden Cast abbrechen.
            if (m_CastInProgress)
            {
                CancelActiveCast();
            }

            // Gefuerchtet, aber bewegungsunfaehig (Stun/Root): steht still.
            if (m_Stats.IsStunned || m_Stats.IsImmobilized)
            {
                return;
            }

            // Fluchtrichtung = weg vom Fear-Verursacher. Ist der Caster nicht
            // mehr aufloesbar, behaelt der NPC die zuletzt bekannte Richtung.
            if (m_Stats.TryGetFearSourcePosition(out Vector3 sourcePos))
            {
                Vector3 away = transform.position - sourcePos;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    m_FleeDirection = away.normalized;
                }
            }

            if (m_FleeDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float speed = m_Stats.WalkSpeed * m_Stats.MoveSpeedMultiplier;
            // Fluchtpunkt weit voraus, damit MoveTowardsPoint kontinuierlich
            // einen vollen Schritt pro Tick liefert (kein Stop-and-Go).
            Vector3 fleePoint = transform.position + m_FleeDirection * k_FleeLookaheadMeters;
            MoveTowardsPoint(fleePoint, dt, speed);
        }

    }
}
