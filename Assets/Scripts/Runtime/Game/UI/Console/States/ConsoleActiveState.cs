using System.Collections.Generic;
using Riftstorm.Game.UI.Console.Commands;
using Tolik.Riftstorm.Runtime.Core;

namespace Riftstorm.Game.UI.Console.States
{
    /// <summary>
    /// Aktiver State des <see cref="ConsoleManager"/>: der Chat-Input ist fokussiert,
    /// eingehende <see cref="ConsoleLog.CommandSubmitted"/>-Events werden geparst
    /// und an die registrierten <see cref="IConsoleCommand"/>-Handler dispatcht.
    /// </summary>
    /// <remarks>
    /// Parser-Regeln (KISS):
    /// <list type="bullet">
    ///   <item>Eingaben ohne fuehrenden <c>/</c> werden (vorerst) wie Plain-Chat ignoriert.</item>
    ///   <item>Erstes Token (nach dem Slash) ist der case-insensitive Command-Name.</item>
    ///   <item>Weitere Tokens werden durch Whitespace gesplittet und als Argumente uebergeben.</item>
    /// </list>
    /// </remarks>
    public sealed class ConsoleActiveState : State<ConsoleManager>
    {
        private static readonly char[] k_TokenSeparators = new[] { ' ', '\t' };

        private readonly Dictionary<string, IConsoleCommand> m_Commands = new();

        /// <inheritdoc/>
        public override void Enter()
        {
            // No-Op: Registry wird einmalig vom Manager via RegisterCommand befuellt.
        }

        /// <inheritdoc/>
        public override void Exit()
        {
            // No-Op: Registry bleibt erhalten, der State wird wiederverwendet.
        }

        /// <summary>
        /// Registriert einen Handler unter seinem <see cref="IConsoleCommand.Name"/>
        /// (case-insensitive). Doppelregistrierung ueberschreibt den vorherigen
        /// Eintrag — letzter Aufruf gewinnt (Mod-Hook-Reihenfolge).
        /// </summary>
        public void RegisterCommand(IConsoleCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Name))
            {
                return;
            }
            m_Commands[command.Name.ToLowerInvariant()] = command;
        }

        /// <summary>
        /// Parst und dispatcht einen vom <see cref="ConsoleHUD"/> abgeschickten Submit.
        /// Unbekannte Commands erzeugen eine Error-Zeile im Backlog.
        /// </summary>
        public void HandleCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }
            string trimmed = raw.Trim();
            if (trimmed[0] != '/')
            {
                // Reiner Chat-Text — Phase 2 (Server-Broadcast) noch nicht angebunden.
                return;
            }
            string body = trimmed.Substring(1);
            if (string.IsNullOrWhiteSpace(body))
            {
                ConsoleLog.Add("Empty command.", ConsoleChannel.Error);
                return;
            }

            string[] tokens = body.Split(k_TokenSeparators, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }

            string name = tokens[0].ToLowerInvariant();
            if (!m_Commands.TryGetValue(name, out IConsoleCommand command))
            {
                ConsoleLog.Add($"Unknown command: /{name}", ConsoleChannel.Error);
                return;
            }

            string[] args = tokens.Length > 1 ? new string[tokens.Length - 1] : System.Array.Empty<string>();
            for (int i = 1; i < tokens.Length; i++)
            {
                args[i - 1] = tokens[i];
            }
            command.Execute(args);
        }
    }
}
