using System.Threading;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat.CombatStates
{
    /// <summary>
    /// Server-autoritativer Cooldown-State. Wird vom Manager mit der aktuellen
    /// Waffe konfiguriert; läuft <c>WeaponDefinition.AttackCooldown</c> ab, ohne
    /// per-Frame-Polling — der Übergang zurück in den Idle-State erfolgt über
    /// ein einmaliges <see cref="Awaitable.WaitForSecondsAsync"/>.
    /// </summary>
    public sealed class PlayerCombatAttackingState : PlayerCombatState
    {
        private CancellationTokenSource m_Cts;
        private float m_PendingCooldown;

        /// <summary>
        /// Wird vom <see cref="PlayerCombat.BeginAttack"/> vor <see cref="ChangeState"/>
        /// aufgerufen, damit <see cref="Enter"/> weiß, wie lange er warten soll.
        /// </summary>
        internal void ConfigureFromWeapon(WeaponDefinition weapon)
        {
            m_PendingCooldown = Mathf.Max(0.05f, weapon.AttackCooldown);
        }

        /// <inheritdoc/>
        public override void Enter()
        {
            m_Cts = new CancellationTokenSource();
            _ = ScheduleEndAsync(m_PendingCooldown, m_Cts.Token);
        }

        /// <inheritdoc/>
        public override void Exit()
        {
            // Cancel laufenden Timer (z. B. bei Tod-Interrupt) und Ressourcen freigeben.
            if (m_Cts != null)
            {
                m_Cts.Cancel();
                m_Cts.Dispose();
                m_Cts = null;
            }
        }

        /// <inheritdoc/>
        public override void OnDeath()
        {
            Manager.ChangeState(Manager.DeadState);
        }

        // Eine laufende Attacke kann nicht durch eine neue ersetzt werden — Anfrage wird verworfen.
        // (Combo/Queue kommt in einer späteren Phase.)

        private async Awaitable ScheduleEndAsync(float seconds, CancellationToken token)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(seconds, token);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            // Manager könnte währenddessen zerstört worden sein (Despawn / Scene-Wechsel).
            if (Manager == null || !Manager.IsSpawned)
            {
                return;
            }

            // Doppelten Auto-Transition vermeiden: nur reagieren, wenn wir noch der aktive State sind.
            if (Manager.IsCurrentState(this))
            {
                Manager.ChangeState(Manager.IdleState);
            }
        }
    }
}
