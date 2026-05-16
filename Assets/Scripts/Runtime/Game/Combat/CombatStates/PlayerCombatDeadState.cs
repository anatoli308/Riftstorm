using Riftstorm.Gameplay.Combat;

namespace Riftstorm.Game.Combat.CombatStates
{
    /// <summary>
    /// Absorbierender State: solange der Spieler tot ist, werden alle Inputs
    /// und Schadensanfragen ignoriert. Verlassen wird der State ausschließlich
    /// über <see cref="OnRespawn"/>.
    /// </summary>
    public sealed class PlayerCombatDeadState : PlayerCombatState
    {
        /// <inheritdoc/>
        public override void OnAttackRequested(WeaponDefinition weapon)
        {
            // Tote schlagen nicht — Anfrage verwerfen.
        }

        /// <inheritdoc/>
        public override void OnDeath()
        {
            // Bereits tot — nichts zu tun.
        }

        /// <inheritdoc/>
        public override void OnRespawn()
        {
            Manager.ChangeState(Manager.IdleState);
        }
    }
}
