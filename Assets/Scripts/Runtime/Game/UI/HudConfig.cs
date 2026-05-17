using System;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Datengetriebene HUD-Konfiguration fuer Player- und Target-Frame.
    /// Quelle: <c>StreamingAssets/interface/hud_config.json</c>. Werte koennen
    /// zur Laufzeit per JSON-Edit getweakt werden, ohne die MonoBehaviour-Felder
    /// im Inspector zu beruehren. Felder mit Wert <c>0</c> bzw. leerem String
    /// gelten als "nicht gesetzt" und fallen auf die SerializeField-Defaults zurueck.
    /// </summary>
    [Serializable]
    public sealed class HudConfig
    {
        // ---------------------------------------------------------------------
        // Layout (geteilt zwischen Player- und Target-Frame; symmetrisch)
        // ---------------------------------------------------------------------

        /// <summary>Gesamtbreite des Frame-Sprites in Pixeln.</summary>
        public float frameWidth;
        /// <summary>Gesamthoehe des Frame-Sprites in Pixeln.</summary>
        public float frameHeight;
        /// <summary>Durchmesser des runden Portraits.</summary>
        public float portraitSize;
        /// <summary>Abstand des Portraits vom linken/rechten Frame-Rand.</summary>
        public float portraitInset;
        /// <summary>Abstand des Portraits vom oberen Frame-Rand.</summary>
        public float portraitTop;
        /// <summary>Durchmesser der Level-Badge.</summary>
        public float levelBadgeSize;
        /// <summary>Default-Bar-Inset (Fallback fuer HP+Mana).</summary>
        public float barInset;
        /// <summary>Default-Bar-Breite (Fallback fuer HP+Mana).</summary>
        public float barWidth;
        /// <summary>Inset der HP-Bar zum portraitseitigen Frame-Rand. Faellt auf <see cref="barInset"/> zurueck.</summary>
        public float hpBarInset;
        /// <summary>Breite der HP-Bar. Faellt auf <see cref="barWidth"/> zurueck.</summary>
        public float hpBarWidth;
        /// <summary>Inset der Mana-Bar zum portraitseitigen Frame-Rand. Faellt auf <see cref="barInset"/> zurueck.</summary>
        public float manaBarInset;
        /// <summary>Breite der Mana-Bar. Faellt auf <see cref="barWidth"/> zurueck.</summary>
        public float manaBarWidth;
        /// <summary>Hoehe der HP-Bar.</summary>
        public float hpBarHeight;
        /// <summary>Hoehe der Mana-Bar.</summary>
        public float manaBarHeight;
        /// <summary>Oberer Beginn der HP-Bar.</summary>
        public float hpTop;
        /// <summary>Oberer Beginn der Mana-Bar.</summary>
        public float manaTop;
        /// <summary>Oberer Beginn des Name-Labels (absolute Y). Darf negativ sein (Label oberhalb des Frames). Null/nicht gesetzt = SerializeField-Default.</summary>
        public float? nameTop;

        // ---------------------------------------------------------------------
        // Anchor (Screen-Position der Frames)
        // ---------------------------------------------------------------------

        /// <summary>Y-Position beider Frames vom oberen Bildschirmrand.</summary>
        public float anchorTop;
        /// <summary>X-Position des Player-Frames (vom linken Bildschirmrand).</summary>
        public float playerAnchorLeft;
        /// <summary>X-Position des Target-Frames (vom linken Bildschirmrand).</summary>
        public float targetAnchorLeft;

        // ---------------------------------------------------------------------
        // Texturen (Pfade relativ zu Application.streamingAssetsPath).
        // Leer = SerializeField-Referenz aus dem Inspector wird genutzt.
        // ---------------------------------------------------------------------

        public string frameTexture;
        public string frameTextureReverse;
        public string hpFillTexture;
        public string hpFillTextureReverse;
        public string manaFillTexture;
        public string manaFillTextureReverse;
        public string levelBadgeTexture;
        /// <summary>Optionale Rahmen-/Border-Overlay-Textur (z. B. <c>interface/unit_frame_bronze</c>). Wird ausschliesslich beim Target-Frame ueber das gesamte Frame gelegt, um Mob-Rarity zu kennzeichnen.</summary>
        public string targetBorderTexture;
    }
}
