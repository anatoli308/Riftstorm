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

        // ---------------------------------------------------------------------
        // Cursor (Hardware-Cursor, getauscht je nach Hover-Zustand)
        // ---------------------------------------------------------------------

        /// <summary>Default-Cursor-Textur (wenn kein Gegner unter der Maus).</summary>
        public string defaultCursorTexture = "interface/cursor";
        /// <summary>Attack-Cursor-Textur (wird gesetzt, sobald ein anderer Spieler / eine andere Einheit gehovert wird).</summary>
        public string attackCursorTexture = "interface/cursor_attack";
        /// <summary>Cursor-Hotspot X in Pixeln (relativ zur Cursor-Textur, 0 = links).</summary>
        public float cursorHotspotX = 0f;
        /// <summary>Cursor-Hotspot Y in Pixeln (relativ zur Cursor-Textur, 0 = oben).</summary>
        public float cursorHotspotY = 0f;
        /// <summary>Skalierungsfaktor der Cursor-Textur (1 = Originalgroesse, 0.5 = halbe Groesse). Hotspot wird automatisch mitskaliert.</summary>
        public float cursorScale = 0.5f;

        // ---------------------------------------------------------------------
        // Selection-Indicator (Boden-Decal um die gelockte Einheit)
        // ---------------------------------------------------------------------

        /// <summary>Textur fuer den Selection-Indicator am Boden (League-of-Legends-Style Ring). Wird ueber dem alten LineRenderer-Kreis im <c>HitboxIndicator</c> als flaches Quad gerendert. Leerer String oder fehlende Textur \u2192 Fallback auf den Vektor-Ring.</summary>
        public string selectionIndicatorTexture = "interface/unit_selected";
        /// <summary>Skalierungsfaktor des Selection-Indicator-Quads relativ zum Durchmesser (2 * HitRadius). 1 = exakt am Hitradius, 1.2 = 20% groesser.</summary>
        public float selectionIndicatorScale = 1f;

        // ---------------------------------------------------------------------
        // Action Bar (unten + rechte vertikale Bars)
        // ---------------------------------------------------------------------

        /// <summary>Hintergrund-Textur der Action-Bar (horizontal gezeichnete Holz-/Stein-Platte mit 12 eingebrannten Slot-Wells). Leer \u2192 Fallback auf prozedurale Slot-Tiles.</summary>
        public string actionBarBaseTexture = "interface/actionbar_base";
        /// <summary>Fuelltextur der XP-Bar unter der Action-Bar (horizontaler Streifen).</summary>
        public string actionBarXpFillTexture = "interface/xp_bar";
        /// <summary>Breite der unteren Action-Bar in Pixeln. Sollte dem Seitenverhaeltnis der <see cref="actionBarBaseTexture"/> entsprechen.</summary>
        public float actionBarBottomWidth = 720f;
        /// <summary>Hoehe der unteren Action-Bar in Pixeln.</summary>
        public float actionBarBottomHeight = 80f;
        /// <summary>Kantenlaenge eines Slot-Quadrats in der unteren Action-Bar.</summary>
        public float actionBarBottomSlotSize = 44f;
        /// <summary>Horizontaler Innen-Inset der Slot-Reihe zur Action-Bar-Basis (links + rechts). Entfernt die ausgefranste Pinsel-Kante aus dem Slot-Bereich.</summary>
        public float actionBarBottomSlotInsetX = 36f;
        /// <summary>Vertikaler Offset der Slot-Reihe zum oberen Rand der Action-Bar-Basis.</summary>
        public float actionBarBottomSlotInsetY = 12f;
        /// <summary>Hoehe der XP-Bar unter der Action-Bar.</summary>
        public float actionBarBottomXpHeight = 12f;
        /// <summary>Vertikaler Spalt zwischen Action-Bar-Unterkante und XP-Bar-Oberkante (negativ \u2192 XP-Bar ueberlappt die Basis). \u00DCberholt von <see cref="actionBarBottomXpInsetBottom"/> seit XP-Bar Overlay innerhalb der Basis ist; bleibt fuer Rueckwaertskompatibilitaet erhalten.</summary>
        public float actionBarBottomXpGap = -2f;
        /// <summary>Pixel-Abstand der XP-Bar-Unterkante zur Unterkante der Action-Bar-Basis (Overlay-Position INNERHALB der Basis-Textur).</summary>
        public float actionBarBottomXpInsetBottom = 6f;
        /// <summary>Pixel-Abstand der unteren Action-Bar zum unteren Bildschirmrand.</summary>
        public float actionBarBottomMargin = 16f;
        /// <summary>Breite einer rechten vertikalen Bar (kurze Achse \u2014 nach 90deg-Rotation der Basis-Textur).</summary>
        public float actionBarRightWidth = 64f;
        /// <summary>Hoehe einer rechten vertikalen Bar (lange Achse).</summary>
        public float actionBarRightHeight = 560f;
        /// <summary>Kantenlaenge eines Slot-Quadrats in der rechten Action-Bar.</summary>
        public float actionBarRightSlotSize = 40f;
        /// <summary>Pixel-Abstand der ersten rechten Action-Bar zum rechten Bildschirmrand.</summary>
        public float actionBarRightMargin = 16f;
        /// <summary>Horizontaler Spalt zwischen erster und zweiter rechter Action-Bar.</summary>
        public float actionBarRightSpacing = 8f;
        /// <summary>Anzahl der rechten vertikalen Action-Bars (1 oder 2).</summary>
        public int actionBarRightCount = 1;
        /// <summary>Rotationswinkel der Basis-Textur fuer rechte vertikale Action-Bars in Grad. -90 = oberes Textur-Ende zeigt nach unten, +90 = oberes Textur-Ende zeigt nach oben.</summary>
        public float actionBarRightRotationDegrees = -90f;
    }
}
