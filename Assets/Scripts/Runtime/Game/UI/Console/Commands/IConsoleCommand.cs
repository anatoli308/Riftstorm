namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// Vertrag fuer einen vom <see cref="ConsoleManager"/> ausfuehrbaren Chat-Command.
    /// Implementierungen werden im <see cref="ConsoleActiveState"/> in einer Name-Map
    /// registriert und bekommen die bereits gesplitteten Argumente (ohne den
    /// fuehrenden Slash und ohne den Command-Namen).
    /// </summary>
    /// <remarks>
    /// Keine Source-Parity: das Original (FLARE/Stone&amp;River) hat keinerlei
    /// Chat-Commands. Das ist Riftstorm-spezifische Operator-/Dev-Infrastruktur.
    /// Antworten gehen ueber <see cref="ConsoleLog.Add(string, ConsoleChannel)"/>
    /// zurueck in den Console-Backlog.
    /// </remarks>
    public interface IConsoleCommand
    {
        /// <summary>
        /// Eindeutiger Command-Name in Kleinbuchstaben (ohne fuehrenden Slash),
        /// z.B. <c>"weapon"</c>. Wird vom <see cref="ConsoleActiveState"/> beim
        /// Parser-Dispatch case-insensitive verglichen.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Kurze Hilfetextzeile fuer <c>/help</c> oder bei falschem Aufruf.
        /// </summary>
        string Usage { get; }

        /// <summary>
        /// Fuehrt den Command mit den uebergebenen Argumenten aus. Antworten
        /// werden vom Handler selbst via <see cref="ConsoleLog.Add(string, ConsoleChannel)"/>
        /// in den Backlog gepostet.
        /// </summary>
        /// <param name="args">Tokenisierte Argumente (ohne Command-Namen). Nie <c>null</c>, ggf. leer.</param>
        void Execute(string[] args);
    }
}
