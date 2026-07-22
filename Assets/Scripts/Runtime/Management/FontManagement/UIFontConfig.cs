namespace Riftstorm.Management.FontManagement
{
    /// <summary>
    /// Daten-Modell fuer die UI-Font-Zuordnung aus
    /// <c>StreamingAssets/interface/ui_fonts.json</c>. Reine POCO — wird per
    /// Newtonsoft.Json deserialisiert. Werte sind Font-Asset-Namen (ohne
    /// Extension, ohne Pfad), die der <see cref="FontManager"/> matcht.
    /// </summary>
    /// <remarks>
    /// Bewusst KEIN ScriptableObject: das Projekt bevorzugt JSON in
    /// StreamingAssets fuer Daten-Konfiguration (siehe copilot-instructions).
    /// Default-Werte greifen, wenn das JSON fehlt oder Felder leer sind.
    /// </remarks>
    public sealed class UIFontConfig
    {
        /// <summary>Display-/Titel-Font (Login-Screen, Hauptueberschriften).</summary>
        public string title = "Friz Quadrata Bold";

        /// <summary>Heading-Font (Spieler-/Target-Namen, Section-Header).</summary>
        public string heading = "Friz Quadrata Regular";

        /// <summary>Body-Font (Eingabefelder, Beschreibungen, Fliesstext).</summary>
        public string body = "Fontin-Regular";

        /// <summary>Small-Font (Statuszeilen, Tooltips, kleine Labels).</summary>
        public string small = "trebuc";

        /// <summary>Keybind-Font (Tastenkuerzel auf Action-Slots).</summary>
        public string keybind = "trebuc";

        /// <summary>Numeric-Font (HP/Mana/XP-Werte auf den Bars).</summary>
        public string numeric = "Helvetica 400";

        /// <summary>Dialog-Font (Confirm-Boxen, Story-Texte).</summary>
        public string dialog = "Palatino Linotype Regular";
    }
}
