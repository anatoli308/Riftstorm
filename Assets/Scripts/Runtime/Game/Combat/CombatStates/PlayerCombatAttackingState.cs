using System.Threading;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat.CombatStates
{
    /// <summary>
    /// Server-autoritativer Cooldown-State. Zweistufige Awaitable-Sequenz pro
    /// Attacke: warten bis <c>HitResolveProgress * AttackCooldown</c> → Server-
    /// Schadensauflösung — dann warten bis zum Ende des Cooldowns → Idle.
    /// Kein per-Frame-Polling (Projektregel).
    /// </summary>
    public sealed class PlayerCombatAttackingState : PlayerCombatState
    {
        private CancellationTokenSource m_Cts;
        private float m_PendingCooldown;
        private float m_PendingHitResolveProgress;
        private WeaponDefinition m_PendingWeapon;

        /// <summary>
        /// Wird vom <see cref="PlayerCombat.BeginAttack"/> vor <see cref="ChangeState"/>
        /// aufgerufen, damit <see cref="Enter"/> weiß, wie lange er warten soll
        /// und welche Waffe für die Schadensberechnung relevant ist.
        /// </summary>
        internal void ConfigureFromWeapon(WeaponDefinition weapon, float cooldown)
        {
            m_PendingWeapon = weapon;
            m_PendingCooldown = Mathf.Max(0.05f, cooldown);
            m_PendingHitResolveProgress = Mathf.Clamp01(weapon.HitResolveProgress);
        }

        /// <inheritdoc/>
        public override void Enter()
        {
            m_Cts = new CancellationTokenSource();
            _ = RunAttackCycleAsync(m_PendingWeapon, m_PendingCooldown, m_PendingHitResolveProgress, m_Cts.Token);
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

        private async Awaitable RunAttackCycleAsync(WeaponDefinition weapon, float cooldown, float hitResolveProgress, CancellationToken token)
        {
            float resolveAt = cooldown * hitResolveProgress;
            float remaining = Mathf.Max(0f, cooldown - resolveAt);

            // ---- Phase A: Windup bis zum Hit-Frame ----
            try
            {
                if (resolveAt > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(resolveAt, token);
                }
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (Manager == null || !Manager.IsSpawned)
            {
                return;
            }

            // ---- Hit-Resolve (server-only, nur wenn wir noch der aktive State sind) ----
            if (Manager.IsServer && Manager.IsCurrentState(this))
            {
                // Roadmap 5: vor dem Resolve nochmal prüfen, ob das Ziel
                // überhaupt noch gültig ist (Tod, Despawn, Wegrennen, Clear).
                // Falls nicht → Attacke abbrechen statt Cooldown weiterzuhalten.
                if (!Manager.ServerIsTargetStillValid(weapon))
                {
                    Manager.ChangeState(Manager.IdleState);
                    return;
                }
                Manager.ServerResolveMeleeHit(weapon);
            }

            // ---- Phase B: Recover bis Ende des Cooldowns ----
            try
            {
                if (remaining > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(remaining, token);
                }
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (Manager == null || !Manager.IsSpawned)
            {
                return;
            }

            if (Manager.IsCurrentState(this))
            {
                Manager.ChangeState(Manager.IdleState);
            }
        }
    }
}
