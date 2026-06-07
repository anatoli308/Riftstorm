using System;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// Datengetriebene Konfiguration fuer das ingame Server-Chat-Fenster
    /// (<see cref="ConsoleHUD"/>). Quelle: <c>StreamingAssets/interface/console_config.json</c>.
    /// Mirror der Source-<c>ConsoleWindow.cpp</c>: 9-Slice-Frame, Close-Button (Escape),
    /// Enter-Button (Return), Scroll-Up/Down-Buttons, ScrollBar, PromptBox (Log + Input).
    /// </summary>
    /// <remarks>
    /// Alle Texture-Keys sind extensionslose Pfade unter <c>Assets/Art</c>
    /// (siehe <see cref="Management.TextureManagement.TextureManager"/>).
    /// Layoutwerte sind in Pixeln. Fehlt das JSON oder einzelne Keys, gelten
    /// die Defaults hier.
    /// </remarks>
    [Serializable]
    public sealed class ConsoleConfig
    {
        // ---------------------------------------------------------------------
        // Anchor &amp; Panel-Geometrie (Source: bottom-left of screen)
        // ---------------------------------------------------------------------

        /// <summary>Abstand der Konsole zum linken Bildschirmrand in Pixeln.</summary>
        public float anchorLeft = 12f;
        /// <summary>Abstand der Konsole zum unteren Bildschirmrand in Pixeln.</summary>
        public float anchorBottom = 12f;
        /// <summary>Gesamtbreite des Panels in Pixeln.</summary>
        public float panelWidth = 520f;
        /// <summary>Gesamthoehe des Panels in Pixeln.</summary>
        public float panelHeight = 220f;

        // ---------------------------------------------------------------------
        // 9-Slice-Frame-Texturen (mirror ConsoleWindow.cpp Konstruktor)
        // ---------------------------------------------------------------------

        public string cornerTopLeftTexture = "interface/console/console_top_left";
        public string cornerTopRightTexture = "interface/console/console_top_right";
        public string cornerBottomLeftTexture = "interface/console/console_bottom_left";
        public string cornerBottomRightTexture = "interface/console/console_bottom_right";
        public string edgeTopTexture = "interface/console/console_top_across";
        public string edgeBottomTexture = "interface/console/console_bottom_across";
        public string edgeLeftTexture = "interface/console/console_left_up";
        public string edgeRightTexture = "interface/console/console_right_up";
        public string centerTexture = "interface/console/console_center";

        /// <summary>Breite einer Eck-Textur in Pixeln (top-left/right, bottom-left/right).</summary>
        public float cornerWidth = 32f;
        /// <summary>Hoehe einer Eck-Textur in Pixeln.</summary>
        public float cornerHeight = 32f;
        /// <summary>Dicke der Edge-Streifen (top/bottom Hoehe, left/right Breite).</summary>
        public float edgeThickness = 16f;

        // ---------------------------------------------------------------------
        // Close-Button (3-State, top-right, Escape)
        // ---------------------------------------------------------------------

        public string closeButtonIdleTexture = "interface/console/console_button_close_idle";
        public string closeButtonHoverTexture = "interface/console/console_button_close_hover";
        public string closeButtonPressTexture = "interface/console/console_button_close_press";
        public float closeButtonSize = 26f;
        /// <summary>Versatz vom rechten Panel-Rand (positive Werte ziehen den Button nach links innen).</summary>
        public float closeButtonInsetRight = 26f;
        /// <summary>Versatz vom oberen Panel-Rand.</summary>
        public float closeButtonInsetTop = 8f;

        // ---------------------------------------------------------------------
        // Enter-Button (3-State, bottom-right, Return)
        // ---------------------------------------------------------------------

        public string enterButtonIdleTexture = "interface/console/console_enter_button_idle";
        public string enterButtonHoverTexture = "interface/console/console_enter_button_hover";
        public string enterButtonPressTexture = "interface/console/console_enter_button_press";
        public float enterButtonWidth = 64f;
        public float enterButtonHeight = 22f;
        public float enterButtonInsetRight = 18f;
        public float enterButtonInsetBottom = 12f;

        // ---------------------------------------------------------------------
        // Scroll-Buttons + ScrollBar (rechte Seite)
        // ---------------------------------------------------------------------

        public string scrollUpIdleTexture = "interface/console/console_scroll_up_idle";
        public string scrollUpHoverTexture = "interface/console/console_scroll_up_hover";
        public string scrollUpPressTexture = "interface/console/console_scroll_up_press";
        public string scrollDownIdleTexture = "interface/console/console_scroll_down_idle";
        public string scrollDownHoverTexture = "interface/console/console_scroll_down_hover";
        public string scrollDownPressTexture = "interface/console/console_scroll_down_press";
        public string scrollBarTexture = "interface/console/console_scroll_bar";
        public float scrollButtonSize = 18f;
        public float scrollBarInsetRight = 26f;
        public float scrollBarInsetTop = 36f;
        public float scrollBarInsetBottom = 56f;

        // ---------------------------------------------------------------------
        // PromptBox-Layout (Log + Input-Field)
        // ---------------------------------------------------------------------

        public float inputInsetLeft = 16f;
        public float inputInsetRight = 92f;
        public float inputInsetBottom = 12f;
        public float inputHeight = 22f;

        public float logInsetLeft = 16f;
        public float logInsetRight = 48f;
        public float logInsetTop = 36f;
        public float logInsetBottom = 44f;

        public float logFontSize = 12f;
        public float inputFontSize = 12f;

        /// <summary>Maximale Anzahl Zeilen im Backlog (FIFO-Trim).</summary>
        public int logMaxLines = 200;

        /// <summary>Wenn <c>true</c>, ist die Konsole beim Scene-Load sofort offen.</summary>
        public bool openOnStart = false;
    }
}
