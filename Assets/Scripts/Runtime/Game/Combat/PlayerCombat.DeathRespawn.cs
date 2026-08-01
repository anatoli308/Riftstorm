using System.Threading;
using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
using Riftstorm.Game.Items;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Tolik.Riftstorm.Runtime.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    public sealed partial class PlayerCombat
    {
        // -------------------------------------------------------------------------
        // Server-Death-Pipeline
        // -------------------------------------------------------------------------

        private void OnServerDied()
        {
            if (!IsServer)
            {
                return;
            }

            // 0) Falls der Spieler gerade gecastet hat, dem Owner-HUD den
            //    Cast-Abbruch melden, BEVOR die State-Transition den
            //    CastingState verl&#228;sst (sonst h&#228;tte das HUD keine
            //    Chance mehr, die CastBar zu schlie&#223;en).
            if (m_CurrentState == CastingState)
            {
                EndCastClientRpc(false);
            }

            // 1) Visual-Fanout auf alle Peers (inkl. Host), bevor die State-Transition
            //    irgendwelche Side-Effects auslöst.
            PlayDeathClientRpc();

            // 2) State-Hook: jeder Combat-State entscheidet selbst, ob er nach DeadState
            //    wechselt (Idle/Attacking → DeadState; DeadState ignoriert es).
            m_CurrentState?.OnDeath();

            // 3) Respawn nach Verzögerung planen (Awaitable, kein Polling).
            //    Vorherigen Timer abbrechen, falls einer aktiv ist.
            CancelRespawnTimer();
            m_RespawnCts = new CancellationTokenSource();
            _ = ScheduleRespawnAsync(m_RespawnCts.Token);
        }

        /// <summary>
        /// Server-only: hebt einen bereits betretenen Dead-State ohne Teleport
        /// oder Full-Reset auf. Wird von spellbasierten Wiederbelebungen genutzt.
        /// </summary>
        public void ServerHandleExternalRevive()
        {
            if (!IsServer)
            {
                return;
            }

            CancelRespawnTimer();
            m_CurrentState?.OnRespawn();
            PlayRespawnClientRpc();
        }

        [ClientRpc]
        private void PlayDeathClientRpc(ClientRpcParams _ = default)
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayDie();
            }
        }

        /// <summary>
        /// Client-side handler für das <see cref="UnitStats.ClientDamageReceived"/>-Event.
        /// Wird auf jedem Peer (Server + alle Clients) aufgerufen, sobald UnitStats den
        /// Damage-Fanout für diesen Spieler auslöst. Spielt nur dann eine Hit-Reaction,
        /// wenn tatsächlich Schaden geflossen ist — Misses/Dodges/Resists triggern keine
        /// Anim (dafür wäre ein separates Reaction-Event vorgesehen).
        /// </summary>
        private void OnClientDamageReceived(int amount, HitResult result)
        {
            if (amount <= 0)
            {
                return;
            }
            if (m_Visuals != null)
            {
                m_Visuals.PlayHit();
            }
        }

        /// <summary>
        /// Server-only: wartet <see cref="m_RespawnDelaySeconds"/> Sekunden und führt dann
        /// einen vollständigen Respawn aus (HP/Mana reset, Teleport zum Spawn, State zurück
        /// auf Idle, Visual-Fanout). Bricht sauber ab, wenn das Objekt despawnt.
        /// </summary>
        private async Awaitable ScheduleRespawnAsync(CancellationToken token)
        {
            try
            {
                float delay = Mathf.Max(0f, m_RespawnDelaySeconds);
                if (delay > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(delay, token);
                }
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (!IsServer || !IsSpawned)
            {
                return;
            }

            ServerRespawn();
        }

        /// <summary>
        /// Server-only: führt den eigentlichen Respawn aus. Reihenfolge ist wichtig —
        /// erst Stats zurücksetzen, dann teleportieren, dann State-Hook, dann Visual-Fanout,
        /// damit Clients in einem konsistenten Zustand das Reset sehen.
        /// </summary>
        private void ServerRespawn()
        {
            if (!IsServer)
            {
                return;
            }

            // 1) Stats zurücksetzen (replizierte NetworkVariables → alle Clients sehen Full-HP).
            if (m_Stats != null)
            {
                m_Stats.ServerResetHp();
                m_Stats.ServerResetMana();
            }

            // 2) Harte Reposition zum initialen Spawn-Punkt (mit Owner-Prediction-Reset).
            if (m_Movement != null)
            {
                m_Movement.ServerTeleportTo(m_ServerSpawnPosition);
            }
            else
            {
                transform.position = m_ServerSpawnPosition;
            }

            // 3) State-Hook: DeadState → IdleState (andere States ignorieren OnRespawn).
            m_CurrentState?.OnRespawn();

            // 4) Visual-Fanout: alle Peers (inkl. Host) verlassen den Dead-Visual-Latch.
            PlayRespawnClientRpc();
        }

        /// <summary>
        /// Bricht einen laufenden Respawn-Timer sauber ab und gibt seine Ressourcen frei.
        /// </summary>
        private void CancelRespawnTimer()
        {
            if (m_RespawnCts == null)
            {
                return;
            }

            m_RespawnCts.Cancel();
            m_RespawnCts.Dispose();
            m_RespawnCts = null;
        }

        [ClientRpc]
        private void PlayRespawnClientRpc(ClientRpcParams _ = default)
        {
            if (m_Visuals != null)
            {
                m_Visuals.ResetForRespawn();
            }
        }

    }
}
