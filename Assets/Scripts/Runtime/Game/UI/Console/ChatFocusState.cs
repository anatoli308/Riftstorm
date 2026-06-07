using System;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// Prozessweiter Gate-Flag, der signalisiert, ob der Spieler aktuell im
    /// <see cref="ConsoleHUD"/>-Eingabefeld tippt. Wird vom
    /// <see cref="Input.PlayerInputController"/> abgefragt, damit
    /// Spell-Hotkeys (Zahlentasten 1..0), Attack, NextTarget, ClearTarget und
    /// MoveCommand <b>nicht</b> feuern, waehrend der Chat-Input fokussiert ist
    /// (sonst loest jedes "1" im Chatfenster den Action-Bar-Slot 0 aus).
    /// </summary>
    /// <remarks>
    /// Statisch + ohne ServiceLocator gehalten — analog <see cref="ConsoleLog"/>:
    /// das Gate muss schon vor dem ersten <c>ApplicationEntryPoint.Awake</c>
    /// bzw. ohne UI-Instanz konsistent lesbar sein (Tests, Mocks).
    /// </remarks>
    public static class ChatFocusState
    {
        /// <summary>
        /// <c>true</c>, solange der Chat-Input-TextField fokussiert ist. Der
        /// <see cref="ConsoleHUD"/> setzt das Flag in <c>SetInputActive</c>.
        /// </summary>
        public static bool IsTyping { get; private set; }

        /// <summary>
        /// Wird gefeuert, wenn sich <see cref="IsTyping"/> aendert. Konsumenten
        /// koennen hier Cursor/UI-State umschalten (z. B. Maus-Cursor-Modus).
        /// </summary>
        public static event Action<bool> Changed;

        /// <summary>
        /// Setzt das Gate. Idempotent — feuert <see cref="Changed"/> nur bei
        /// tatsaechlicher Aenderung.
        /// </summary>
        public static void SetTyping(bool typing)
        {
            if (IsTyping == typing)
            {
                return;
            }
            IsTyping = typing;
            Changed?.Invoke(typing);
        }
    }
}
