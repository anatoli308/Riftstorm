using System.Threading;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat.CombatStates
{
    /// <summary>
    /// Server-autoritativer Cast-State. Wartet <c>SpellTemplate.CastTime</c> ab
    /// (Awaitable, kein Polling) und delegiert dann an
    /// <see cref="PlayerCombat.ServerCompleteCast"/>, das die eigentliche
    /// <see cref="SpellExecutor.Execute"/>-Pipeline anstößt und das
    /// Visual-Fanout an alle Clients verschickt. Verlassen wird der State über
    /// einen regulären Abschluss zurück nach Idle, ein Tod-Interrupt
    /// (<see cref="OnDeath"/>) oder einen externen <see cref="Exit"/>-Cancel
    /// (z. B. expliziter Cast-Cancel, kommt in einer späteren Phase).
    /// </summary>
    /// <remarks>
    /// Bewusst minimal gehalten: Resource-Abzug + Cooldown-Start laufen am
    /// Cast-Ende über <see cref="SpellExecutor"/> mit. Pre-Cast-Resource-Lock
    /// und CastBar-ClientRpc folgen, sobald HUD/UI das fordert.
    /// </remarks>
    public sealed class PlayerCombatCastingState : PlayerCombatState
    {
        private CancellationTokenSource m_Cts;
        private int m_PendingSpellEntry;
        private SpellTemplate m_PendingSpell;
        private ulong m_PendingTargetNetId;
        private ICombatUnit m_PendingPrimaryTarget;
        private CastDestination m_PendingDestination;

        /// <summary>
        /// Aktuell laufender Cast-Spell. <c>null</c>, sobald der State verlassen
        /// wird. Wird von <see cref="PlayerCombat.CurrentCastSpell"/> exponiert,
        /// damit das Movement-Gate das <see cref="SpellAttributes.CanMoveWhileCasting"/>
        /// Flag pruefen kann, bevor ein Move-Input den Cast abbricht.
        /// </summary>
        internal SpellTemplate CurrentSpell => m_PendingSpell;

        /// <summary>
        /// Wird von <see cref="PlayerCombat.BeginCast"/> vor <see cref="ChangeState"/>
        /// gerufen, damit <see cref="Enter"/> alle nötigen Parameter zur Hand hat.
        /// </summary>
        internal void ConfigureFromCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget, CastDestination destination)
        {
            m_PendingSpellEntry = spellEntry;
            m_PendingSpell = spell;
            m_PendingTargetNetId = targetNetId;
            m_PendingPrimaryTarget = primaryTarget;
            m_PendingDestination = destination;
        }

        /// <inheritdoc/>
        public override void Enter()
        {
            m_Cts = new CancellationTokenSource();
            _ = RunCastSequenceAsync(
                m_PendingSpellEntry,
                m_PendingSpell,
                m_PendingTargetNetId,
                m_PendingPrimaryTarget,
                m_PendingDestination,
                m_Cts.Token);
        }

        /// <inheritdoc/>
        public override void Exit()
        {
            // Cancel laufenden Cast-Timer (Death-Interrupt, Cast-Cancel) und Ressourcen freigeben.
            if (m_Cts != null)
            {
                m_Cts.Cancel();
                m_Cts.Dispose();
                m_Cts = null;
            }
            // Pending-Referenzen freigeben, damit der State keine alten Spell-/Target-Refs hält.
            m_PendingSpell = null;
            m_PendingPrimaryTarget = null;
            m_PendingSpellEntry = 0;
            m_PendingTargetNetId = 0UL;
            m_PendingDestination = CastDestination.None;
        }

        /// <inheritdoc/>
        public override void OnDeath()
        {
            // Tod-Interrupt: Cast wird durch Exit() abgebrochen, sobald ChangeState greift.
            Manager.ChangeState(Manager.DeadState);
        }

        // Auto-Attack-Requests während eines Casts werden verworfen — bewusst kein
        // Queue/Combo-Verhalten in v1. Idle-Übergang nach Cast-Ende reicht.

        private async Awaitable RunCastSequenceAsync(
            int spellEntry,
            SpellTemplate spell,
            ulong targetNetId,
            ICombatUnit primaryTarget,
            CastDestination destination,
            CancellationToken token)
        {
            // SpellTemplate.CastTime ist in Millisekunden (vgl. JSON-Schema).
            float castSeconds = spell != null ? Mathf.Max(0f, spell.CastTime / 1000f) : 0f;

            try
            {
                if (castSeconds > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(castSeconds, token);
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

            // Nur fertigstellen, wenn wir noch der aktive State sind und auf dem Server laufen.
            if (Manager.IsServer && Manager.IsCurrentState(this))
            {
                Manager.ServerCompleteCast(spellEntry, spell, targetNetId, primaryTarget, destination);
            }

            if (Manager.IsCurrentState(this))
            {
                Manager.ChangeState(Manager.IdleState);
            }
        }
    }
}
