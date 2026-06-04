using System;

namespace Riftstorm.Game.Player
{
    /// <summary>
    /// Datengetriebene Konfiguration f&#252;r das <see cref="PlayerNameTag"/>-Hover-Highlight.
    /// Quelle: <c>StreamingAssets/interface/nametag_config.json</c>. Fehlt das JSON oder
    /// einzelne Keys, gelten die hier hinterlegten Defaults &#8212; es gibt keinen zweiten
    /// Default-Pfad in den MonoBehaviour-Komponenten.
    /// </summary>
    /// <remarks>
    /// Texturen-Felder enthalten Schl&#252;ssel relativ zu <c>Application.dataPath/Art</c>
    /// ohne Extension (z. B. <c>"interface/generic_highlight_hover"</c>) und werden
    /// vom <c>TextureManager</c>-Pure-Service aufgel&#246;st.
    /// </remarks>
    [Serializable]
    public sealed class NameTagConfig
    {
        /// <summary>Master-Toggle f&#252;r das Hover-Plate. Aus = nie eine Plate zeichnen.</summary>
        public bool hoverPlateEnabled = true;

        /// <summary>
        /// Plate-Textur, die hinter fremden Nametags im Idle-Zustand (Maus nicht dr&#252;ber)
        /// gezeichnet wird. Leer lassen f&#252;r "keine Idle-Plate".
        /// </summary>
        public string idlePlateTexture = "";

        /// <summary>
        /// Plate-Textur, die hinter fremden Nametags eingeblendet wird, sobald die
        /// Maus &#252;ber dem Label-Rect liegt. Leer lassen f&#252;r "kein Hover-Plate".
        /// </summary>
        public string hoverPlateTexture = "interface/generic_highlight_hover";

        /// <summary>Horizontales Padding (GUI-Pixel) zwischen Text-Rect und Plate-Rand.</summary>
        public float platePaddingX = 10f;

        /// <summary>Vertikales Padding (GUI-Pixel) zwischen Text-Rect und Plate-Rand.</summary>
        public float platePaddingY = 3f;

        // ---- Nameplate-HP-Bar (Overhead, Player + NPC) --------------------------

        /// <summary>Master-Toggle f&#252;r die Overhead-HP-Bar unter dem Nametag. Aus = nie zeichnen.</summary>
        public bool healthBarEnabled = true;

        /// <summary>
        /// Ob die HP-Bar auch &#252;ber dem EIGENEN Spieler gezeichnet wird. Default
        /// <c>false</c>, weil der lokale Spieler bereits sein <c>PlayerFrameUI</c> hat
        /// und ein zweiter Balken &#252;ber dem Kopf nur visueller L&#228;rm w&#228;re.
        /// </summary>
        public bool healthBarShowSelf = false;

        /// <summary>
        /// Hintergrund-Plate-Textur der HP-Bar (leerer Rahmen). Key relativ zu
        /// <c>Application.dataPath/Art</c> ohne Extension. Leer = kein Hintergrund.
        /// </summary>
        public string nameplateBackgroundTexture = "interface/nameplate_bg";

        /// <summary>
        /// Fill-Textur der HP-Bar (progressiv von links nach rechts, skaliert mit
        /// dem HP-Anteil). Key relativ zu <c>Application.dataPath/Art</c> ohne
        /// Extension. Leer = kein Fill.
        /// </summary>
        public string nameplateHpTexture = "interface/nameplate_hp";

        /// <summary>Breite der HP-Bar in GUI-Pixeln.</summary>
        public float healthBarWidth = 120f;

        /// <summary>H&#246;he der HP-Bar in GUI-Pixeln.</summary>
        public float healthBarHeight = 9f;

        /// <summary>Vertikaler Abstand (GUI-Pixel) zwischen Nametag-Unterkante und HP-Bar.</summary>
        public float healthBarOffsetY = 2f;
    }
}
