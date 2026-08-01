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
        /// Server-Hook: speist den <see cref="ThreatManager"/> mit jedem auf
        /// uns angewendeten Schaden und löst bei Neutral/Friendly-NPCs die
        /// Retaliation aus (Idle → Combat). Source-Pendant: NpcAI::onDamaged
        /// + ThreatManager::addThreat.
        /// </summary>
        private void HandleServerDamaged(UnitStats attacker, int damage, HitResult result)
        {
            if (attacker == null || damage <= 0 || attacker == m_Stats)
            {
                return;
            }
            m_Threat.AddThreat(attacker.NetworkObjectId, damage);
            MirrorThreatToSource(attacker.NetworkObjectId);

            // Retaliation: Neutral/Friendly wechseln aus Idle in Combat,
            // sobald sie zum ersten Mal Schaden bekommen. Hostile-NPCs sind
            // entweder schon in Combat (durch Aggro-Scan) oder folgen hier
            // ebenfalls dem Wechsel — letzteres ist günstig für Pull-aus-
            // Aggro-Range-Szenarien (z. B. Bogenschuss von ausserhalb).
            if (m_State == NpcAIState.Idle && !m_ServerDead)
            {
                m_State = NpcAIState.Combat;
            }
        }

        // -------------------------------------------------------------------
        // Death
        // -------------------------------------------------------------------

        private void HandleServerDied()
        {
            m_ServerDead = true;
            m_CurrentTarget = null;
            ClearThreatAndNotify();
            ResetSpellRuntimeTimers();
            m_State = NpcAIState.Dead;
            PlayDieClientRpc();
        }

        /// <summary>
        /// Server-only: hebt den Dead-State eines NPCs ohne Home-Teleport auf.
        /// Wird von spellbasierten Wiederbelebungen genutzt.
        /// </summary>
        public void ServerHandleExternalRevive()
        {
            if (!IsServer)
            {
                return;
            }

            m_ServerDead = false;
            m_CurrentTarget = null;
            ClearThreatAndNotify();
            ResetSpellRuntimeTimers();
            m_State = NpcAIState.Idle;
            m_ServerPosition.Value = transform.position;
            m_ServerMoving.Value = false;
            m_ImpulseSecondsRemaining = 0f;
            m_ImpulseVelocity = Vector3.zero;
            PlayRespawnClientRpc();
        }

        private void ClearThreatAndNotify()
        {
            if (!IsServer)
            {
                m_Threat.Clear();
                return;
            }

            m_Threat.CopyUnitIds(m_ThreatUnitIdsBuffer);
            m_Threat.Clear();

            for (int i = 0; i < m_ThreatUnitIdsBuffer.Count; i++)
            {
                SendThreatToSource(m_ThreatUnitIdsBuffer[i], 0);
            }

            m_ThreatUnitIdsBuffer.Clear();
        }

        private void MirrorThreatToSource(ulong sourceGuid)
        {
            SendThreatToSource(sourceGuid, m_Threat.GetThreat(sourceGuid));
        }

        private void SendThreatToSource(ulong sourceGuid, int threatValue)
        {
            if (!IsServer || sourceGuid == 0UL || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(sourceGuid, out NetworkObject sourceObject)
                || sourceObject == null)
            {
                return;
            }
            if (!sourceObject.IsPlayerObject)
            {
                return;
            }

            m_TargetClientIds[0] = sourceObject.OwnerClientId;
            ClientRpcParams clientRpcParams = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = m_TargetClientIds,
                },
            };
            SyncLocalThreatClientRpc(threatValue, clientRpcParams);
        }

        // -------------------------------------------------------------------
        // Externe Bewegung (Teleport / Impulse) &#8212; vom SpellExecutor genutzt
        // -------------------------------------------------------------------

        /// <summary>
        /// Server-only: harte Reposition der NPC-Unit. Synchronisiert ueber
        /// <see cref="m_ServerPosition"/>. Bricht zusaetzlich einen laufenden
        /// Impulse ab, weil ein Teleport semantisch dominanter ist (z. B.
        /// Blink-to-Caster ueberschreibt einen vorherigen KnockBack).
        /// </summary>
        public void ServerTeleportTo(Vector3 position)
        {
            if (!IsServer)
            {
                return;
            }
            transform.position = position;
            m_ServerPosition.Value = position;
            m_ImpulseSecondsRemaining = 0f;
            m_ImpulseVelocity = Vector3.zero;
        }

        /// <summary>
        /// Server-only: forcierte Bewegung ueber <paramref name="durationSec"/>
        /// Sekunden mit <c>direction.normalized * meters / durationSec</c> m/s.
        /// Waehrend der Dauer pausiert die AI-Bewegung in <see cref="TickServer"/>;
        /// Combat-Logik (Threat, Spell-Cooldowns) laeuft weiter und greift wieder,
        /// sobald die Dauer abgelaufen ist.
        /// </summary>
        public void ServerApplyImpulse(Vector3 direction, float meters, float durationSec)
        {
            if (!IsServer)
            {
                return;
            }
            if (durationSec <= 0f || meters == 0f)
            {
                return;
            }
            Vector3 dir = direction;
            dir.y = 0f;
            float sqr = dir.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                return;
            }
            dir /= Mathf.Sqrt(sqr);
            m_ImpulseVelocity = dir * (meters / durationSec);
            m_ImpulseSecondsRemaining = durationSec;
        }

        /// <summary>
        /// Server-only: registriert einen einmaligen Callback fuer das Ende des
        /// aktuell laufenden Impulses (Impact/Landung). Ohne aktiven Impuls ein
        /// No-Op. Wird vom <see cref="SpellExecutor"/> fuer Impact-getimtes CC
        /// (Charge-Stun) genutzt.
        /// </summary>
        /// <param name="onComplete">Beim Impuls-Ende ausgefuehrte Aktion.</param>
        public void RegisterImpulseCompletion(Action onComplete)
        {
            if (!IsServer || onComplete == null || !IsImpulseActive)
            {
                return;
            }
            m_OnImpulseComplete += onComplete;
        }

    }
}
