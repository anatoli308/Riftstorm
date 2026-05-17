using System;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Datengetriebene HUD-Konfiguration fuer Player- und Target-Frame.
    /// Quelle: <c>StreamingAssets/interface/hud_config.json</c>. Die Felder
    /// tragen sinnvolle Defaults — fehlt das JSON oder einzelne Keys, gelten
    /// diese Defaults. Es gibt keinen zweiten Default-Pfad mehr in den
    /// MonoBehaviour-Komponenten; <c>HudConfig</c> ist die alleinige
    /// Quelle der Wahrheit fuer Layout, Schrift und Texturen.
    /// </summary>
    [Serializable]
    public sealed class HudConfig
    {
        // ---------------------------------------------------------------------
        // Layout (geteilt zwischen Player- und Target-Frame; symmetrisch)
        // ---------------------------------------------------------------------

        /// <summary>Gesamtbreite des Frame-Sprites in Pixeln.</summary>
        public float frameWidth = 360f;
        /// <summary>Gesamthoehe des Frame-Sprites in Pixeln.</summary>
        public float frameHeight = 96f;
        /// <summary>Durchmesser des runden Portraits.</summary>
        public float portraitSize = 84f;
        /// <summary>Abstand des Portraits vom linken/rechten Frame-Rand.</summary>
        public float portraitInset = 6f;
        /// <summary>Abstand des Portraits vom oberen Frame-Rand.</summary>
        public float portraitTop = 6f;
        /// <summary>Durchmesser der Level-Badge.</summary>
        public float levelBadgeSize = 28f;
        /// <summary>Horizontaler Versatz der Level-Badge vom Portrait-Rand, als Anteil der Badge-Groesse (0.15 = 15% nach aussen).</summary>
        public float levelBadgeOffsetXRatio = 0.15f;
        /// <summary>Vertikaler Versatz der Level-Badge ueber die Portrait-Unterkante hinaus, als Anteil der Badge-Groesse (0.5 = halb ueberlappt).</summary>
        public float levelBadgeOffsetYRatio = 0.5f;
        /// <summary>Inset der HP-Bar zum portraitseitigen Frame-Rand.</summary>
        public float hpBarInset = 92f;
        /// <summary>Breite der HP-Bar.</summary>
        public float hpBarWidth = 256f;
        /// <summary>Inset der Mana-Bar zum portraitseitigen Frame-Rand.</summary>
        public float manaBarInset = 100f;
        /// <summary>Breite der Mana-Bar.</summary>
        public float manaBarWidth = 240f;
        /// <summary>Hoehe der HP-Bar.</summary>
        public float hpBarHeight = 20f;
        /// <summary>Hoehe der Mana-Bar.</summary>
        public float manaBarHeight = 16f;
        /// <summary>Oberer Beginn der HP-Bar.</summary>
        public float hpTop = 38f;
        /// <summary>Oberer Beginn der Mana-Bar.</summary>
        public float manaTop = 62f;
        /// <summary>Oberer Beginn des Name-Labels (absolute Y). Darf negativ sein (Label oberhalb des Frames).</summary>
        public float nameTop = 18f;
        /// <summary>Schriftgroesse des Name-Labels in Pixeln.</summary>
        public float nameFontSize = 13f;

        // ---------------------------------------------------------------------
        // Anchor (Screen-Position der Frames)
        // ---------------------------------------------------------------------

        /// <summary>Y-Position beider Frames vom oberen Bildschirmrand.</summary>
        public float anchorTop = 16f;
        /// <summary>X-Position des Player-Frames (vom linken Bildschirmrand).</summary>
        public float playerAnchorLeft = 16f;
        /// <summary>X-Position des Target-Frames (vom linken Bildschirmrand).</summary>
        public float targetAnchorLeft = 388f;

        // ---------------------------------------------------------------------
        // Texturen (Pfade relativ zu Application.streamingAssetsPath)
        // ---------------------------------------------------------------------

        public string frameTexture = "interface/unit_frame";
        public string frameTextureReverse = "interface/unit_frame_reverse";
        public string hpFillTexture = "interface/unit_frame_hp";
        public string hpFillTextureReverse = "interface/unit_frame_hp_reverse";
        public string manaFillTexture = "interface/unit_frame_mp";
        public string manaFillTextureReverse = "interface/unit_frame_mp_reverse";
        public string levelBadgeTexture = "interface/unit_frame_level_bg";
        /// <summary>Optionale Rarity-Ring-Textur (z. B. <c>interface/unit_frame_bronze</c>). Wird beim Target-Frame ausschliesslich um den Portrait-Kreis gelegt (skalierter Overlay). Player-Frame ignoriert dieses Feld.</summary>
        public string targetBorderTexture = "interface/unit_frame_bronze";
        /// <summary>Skalierungsfaktor des Target-Border-Rings relativ zum Portrait-Durchmesser (1.35 = Ring ist 35% groesser).</summary>
        public float targetBorderScale = 1.35f;
        /// <summary>Vertikaler Pixel-Offset des Target-Border-Rings (positiv = nach unten).</summary>
        public float targetBorderYOffset = 6f;
    }
}
