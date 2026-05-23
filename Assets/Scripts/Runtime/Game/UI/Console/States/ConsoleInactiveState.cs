using Tolik.Riftstorm.Runtime.Core;

namespace Riftstorm.Game.UI.Console.States
{
    /// <summary>
    /// Default-State des <see cref="ConsoleManager"/>: der Spieler tippt nicht in
    /// der Konsole. Eingehende <see cref="ConsoleLog.CommandSubmitted"/>-Events
    /// werden hier verworfen (sollte normal nicht passieren, da die HUD den Submit
    /// nur im fokussierten Input ausloest — defensive Behandlung gegen Race-Conditions
    /// zwischen Submit und FocusOut).
    /// </summary>
    public sealed class ConsoleInactiveState : State<ConsoleManager>
    {
        /// <inheritdoc/>
        public override void Enter()
        {
            // No-Op: Inactive ist der Ruhezustand.
        }

        /// <inheritdoc/>
        public override void Exit()
        {
            // No-Op.
        }

        /// <summary>
        /// Verwirft eingehende Submits im Inactive-Zustand. Wird vom Manager
        /// aufgerufen, der die Dispatch-Verantwortung an den aktiven State delegiert.
        /// </summary>
        public void HandleCommand(string _)
        {
            // Inactive: silent drop.
        }
    }
}
