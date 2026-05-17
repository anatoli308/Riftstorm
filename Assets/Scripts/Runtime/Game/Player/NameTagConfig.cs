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
    }
}
