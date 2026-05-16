using Riftstorm.Gameplay.Combat;

namespace Riftstorm.Game.Combat.CombatStates
{
    /// <summary>
    /// Default-State: bereit, eine Attacke zu starten. Reagiert auf
    /// <see cref="OnAttackRequested"/>, indem er die Cooldown-Anim-Sequenz
    /// im <see cref="PlayerCombatAttackingState"/> startet.
    /// </summary>
    public sealed class PlayerCombatIdleState : PlayerCombatState
    {
        /// <inheritdoc/>
        public override void OnAttackRequested(WeaponDefinition weapon)
        {
            // Server-autoritativ: Cooldown + Anim-Variante stehen in der Waffen-Definition.
            Manager.BeginAttack(weapon);
        }

        /// <inheritdoc/>
        public override void OnDeath()
        {
            Manager.ChangeState(Manager.DeadState);
        }
    }
}
