using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// Channel / Farbe einer Console-Zeile. Mirror der Source-<c>ConsoleColors</c>:
    /// Standard = weiss, Error = rot, Warning = gelb, System = grau (z.B. autoexec),
    /// Command = cyan (User-Echo "[CMD]: ..."), Chat = hellblau (spaeterer Server-Broadcast).
    /// </summary>
    public enum ConsoleChannel
    {
        /// <summary>Normale Info-Ausgabe (weiss).</summary>
        Standard = 0,
        /// <summary>Fehler (rot).</summary>
        Error = 1,
        /// <summary>Warnung (gelb).</summary>
        Warning = 2,
        /// <summary>System-Meldung wie "autoexec.conf executed" (grau).</summary>
        System = 3,
        /// <summary>Echo eines vom User eingegebenen Commands (cyan, Source: "[CMD]: ...").</summary>
        Command = 4,
        /// <summary>Server-Chat-Broadcast (hellblau) - kommt in Phase 2 ueber NGO rein.</summary>
        Chat = 5,
    }

    /// <summary>
    /// Eine unveraenderliche Console-Zeile.
    /// </summary>
    public readonly struct ConsoleLine
    {
        /// <summary>Roher Anzeige-Text (ohne Channel-Praefix).</summary>
        public readonly string Text;
        /// <summary>Kanal / Farbe.</summary>
        public readonly ConsoleChannel Channel;
        /// <summary>Zeitpunkt der Erstellung (UTC).</summary>
        public readonly DateTime TimestampUtc;

        public ConsoleLine(string text, ConsoleChannel channel, DateTime timestampUtc)
        {
            Text = text ?? string.Empty;
            Channel = channel;
            TimestampUtc = timestampUtc;
        }
    }

    /// <summary>
    /// Prozessweiter, lock-freier FIFO-Puffer fuer Console-Zeilen. Wird von
    /// <see cref="ConsoleHUD"/> abonniert (Append-Event), kann aber von jedem
    /// Subsystem ohne UI-Abhaengigkeit befuellt werden (Bsp.: Command-Handler,
    /// Network-Listener, Error-Sink). Limitiert auf <see cref="ConsoleConfig.logMaxLines"/>
    /// Zeilen (FIFO-Trim).
    /// </summary>
    /// <remarks>
    /// Bewusst statisch + nicht ueber ServiceLocator: die Console muss auch
    /// vor dem ersten <c>ApplicationEntryPoint.Awake</c> noch Logs schlucken
    /// koennen (z.B. fruehe Bootstrapping-Fehler). Singleton-State ist hier
    /// die einzige Pragmatik.
    /// </remarks>
    public static class ConsoleLog
    {
        private static readonly LinkedList<ConsoleLine> s_Lines = new();
        private static int s_MaxLines = 200;

        /// <summary>Wird nach jedem Append gefeuert.</summary>
        public static event Action<ConsoleLine> LineAppended;

        /// <summary>Wird gefeuert, wenn der Backlog geleert wurde.</summary>
        public static event Action Cleared;

        /// <summary>
        /// Wird gefeuert, wenn der User im <see cref="ConsoleHUD"/> einen Command
        /// eingibt und mit Enter / Enter-Button abschickt. Konsument ist der
        /// <c>ConsoleManager</c> (Phase 2), der die Zeile parst und ausfuehrt.
        /// </summary>
        public static event Action<string> CommandSubmitted;

        /// <summary>
        /// Wird von <see cref="ConsoleHUD"/> aufgerufen, wenn der User Enter drueckt
        /// oder den Enter-Button klickt. Echo + Event-Fan-out passiert hier zentral.
        /// </summary>
        public static void SubmitCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }
            CommandEcho(command);
            CommandSubmitted?.Invoke(command);
        }

        /// <summary>Aktuelle Snapshot-Kopie aller Zeilen (oldest first).</summary>
        public static IReadOnlyCollection<ConsoleLine> Snapshot() => new List<ConsoleLine>(s_Lines);

        /// <summary>Anzahl der aktuell gepufferten Zeilen.</summary>
        public static int Count => s_Lines.Count;

        /// <summary>Setzt das FIFO-Limit. Kuerzt sofort, falls aktuell zu viele Zeilen.</summary>
        public static void SetMaxLines(int max)
        {
            s_MaxLines = max > 0 ? max : 1;
            Trim();
        }

        /// <summary>Fuegt eine Zeile mit <see cref="ConsoleChannel.Standard"/> an.</summary>
        public static void Add(string text) => Add(text, ConsoleChannel.Standard);

        /// <summary>Fuegt eine Zeile mit explizitem Channel an.</summary>
        public static void Add(string text, ConsoleChannel channel)
        {
            ConsoleLine line = new(text, channel, DateTime.UtcNow);
            s_Lines.AddLast(line);
            Trim();
            LineAppended?.Invoke(line);
        }

        /// <summary>Convenience: rote Error-Zeile.</summary>
        public static void Error(string text) => Add(text, ConsoleChannel.Error);

        /// <summary>Convenience: gelbe Warning-Zeile.</summary>
        public static void Warning(string text) => Add(text, ConsoleChannel.Warning);

        /// <summary>Convenience: graue System-Zeile.</summary>
        public static void System(string text) => Add(text, ConsoleChannel.System);

        /// <summary>Convenience: cyane Command-Echo-Zeile ("[CMD]: ...").</summary>
        public static void CommandEcho(string command) => Add($"[CMD]: {command}", ConsoleChannel.Command);

        /// <summary>Leert den Backlog komplett.</summary>
        public static void Clear()
        {
            s_Lines.Clear();
            Cleared?.Invoke();
        }

        private static void Trim()
        {
            while (s_Lines.Count > s_MaxLines)
            {
                s_Lines.RemoveFirst();
            }
        }
    }
}
