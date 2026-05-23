using System;
using Riftstorm.Game.Items;

namespace Riftstorm.Game.UI.Character
{
    /// <summary>
    /// JSON-Config fuer das Charakter-Panel (Equipment + Stats).
    /// Geladen via <see cref="CharacterConfigLoader"/>; Werte sind reine
    /// Layout-/Asset-Defaults und werden im Editor per JSON getuned.
    /// </summary>
    [Serializable]
    public sealed class CharacterConfig
    {
        // ---------------------------------------------------------------------
        // Panel-Layout
        // ---------------------------------------------------------------------

        /// <summary>Panel-Breite in px (linke Haelfte = Equipment, rechte = Stats).</summary>
        public int panelWidth = 360;

        /// <summary>Panel-Hoehe in px.</summary>
        public int panelHeight = 380;

        /// <summary>Anker-Abstand zum linken Bildschirmrand.</summary>
        public int anchorLeft = 16;

        /// <summary>Anker-Abstand zum unteren Bildschirmrand.</summary>
        public int anchorBottom = 220;

        // ---------------------------------------------------------------------
        // Assets
        // ---------------------------------------------------------------------

        /// <summary>TextureManager-Key fuer den Panel-Hintergrund.</summary>
        public string backgroundTexture = "interface/equipment";

        /// <summary>Slot-Frame im Ruhezustand (gleiche Optik wie ActionBar).</summary>
        public string slotIdleTexture = "interface/icons/gameicon40_idle";

        /// <summary>Slot-Frame beim Hovern.</summary>
        public string slotHoverTexture = "interface/icons/gameicon40_hover";

        /// <summary>Slot-Frame beim Druecken.</summary>
        public string slotPressTexture = "interface/icons/gameicon40_press";

        /// <summary>Pixelgroesse jedes Equipment-Slots (entspricht gameicon40).</summary>
        public int slotSize = 40;

        /// <summary>Praefix fuer Item-Icon-Keys im TextureManager.</summary>
        public string itemIconKeyPrefix = "item_icons/";

        // ---------------------------------------------------------------------
        // Slot-Layout (datengetrieben)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Position jedes der 12 Equipment-Slots im Panel-Koordinatensystem
        /// (Origin oben-links). Reihenfolge frei — gerendert wird per <c>slot</c>.
        /// </summary>
        public CharacterSlotLayout[] slots = new CharacterSlotLayout[]
        {
            // Linke Spalte (Body): Helm, Amulet, Chest, Belt, Legs, Boots
            new() { slot = EquipSlot.Helm,     x = 16,  y = 16  },
            new() { slot = EquipSlot.Amulet,   x = 16,  y = 66  },
            new() { slot = EquipSlot.Chest,    x = 16,  y = 116 },
            new() { slot = EquipSlot.Belt,     x = 16,  y = 166 },
            new() { slot = EquipSlot.Legs,     x = 16,  y = 216 },
            new() { slot = EquipSlot.Boots,    x = 16,  y = 266 },

            // Mittlere Spalte (Hands/Rings + Waffen)
            new() { slot = EquipSlot.Hands,    x = 66,  y = 16  },
            new() { slot = EquipSlot.Ring1,    x = 66,  y = 66  },
            new() { slot = EquipSlot.Ring2,    x = 66,  y = 116 },
            new() { slot = EquipSlot.MainHand, x = 16,  y = 316 },
            new() { slot = EquipSlot.Offhand,  x = 66,  y = 316 },
            new() { slot = EquipSlot.Ranged,   x = 116, y = 316 },
        };

        // ---------------------------------------------------------------------
        // Stats-Display (Phase 17C)
        // ---------------------------------------------------------------------

        /// <summary>X-Offset (von links) fuer die Stats-Spalte (rechte Panel-Haelfte).</summary>
        public int statsLeft = 180;

        /// <summary>Y-Offset (von oben) fuer die erste Stats-Zeile.</summary>
        public int statsTop = 16;

        /// <summary>Hoehe einer Stats-Zeile in px.</summary>
        public int statsLineHeight = 16;

        /// <summary>Schriftgroesse fuer Stats-Werte.</summary>
        public int statsFontSize = 12;

        /// <summary>Breite der Stats-Spalte (begrenzt Label-Width).</summary>
        public int statsWidth = 170;

        // ---------------------------------------------------------------------
        // Character Preview (Render-to-Texture Paper-Doll)
        // ---------------------------------------------------------------------

        /// <summary>True ⇒ Live-Preview des Spielers im Panel rendern.</summary>
        public bool previewEnabled = true;

        /// <summary>X-Offset im Panel fuer das Preview-Rechteck.</summary>
        public int previewLeft = 170;

        /// <summary>Y-Offset im Panel fuer das Preview-Rechteck.</summary>
        public int previewTop = 16;

        /// <summary>Breite des Preview-Rechtecks (UI px).</summary>
        public int previewWidth = 160;

        /// <summary>Hoehe des Preview-Rechtecks (UI px).</summary>
        public int previewHeight = 220;

        /// <summary>Aufloesung der RenderTexture (quadratisch, in Texel).</summary>
        public int previewTextureSize = 256;

        /// <summary>
        /// Orthografische Halbhoehe der Preview-Kamera in World-Units. Bestimmt
        /// wie stark der Spieler herangezoomt wird (kleiner = naeher).
        /// </summary>
        public float previewOrthoSize = 1.2f;

        /// <summary>
        /// Welt-Offset der Preview-Kamera relativ zur Spielerposition (Local-XYZ).
        /// Default: leicht ueber Schulterhoehe vor dem Spieler, isometrisch nach
        /// unten geneigt.
        /// </summary>
        public float previewCameraOffsetX = 0f;
        public float previewCameraOffsetY = 1.6f;
        public float previewCameraOffsetZ = -3.2f;

        /// <summary>Pitch-Winkel der Preview-Kamera in Grad (0 = horizontal).</summary>
        public float previewCameraPitchDeg = 15f;

        /// <summary>
        /// Hintergrundfarbe der Preview (RGBA 0..1). Alpha &lt; 1 erlaubt
        /// Durchscheinen des Panel-Backgrounds.
        /// </summary>
        public float previewBackgroundR = 0.05f;
        public float previewBackgroundG = 0.05f;
        public float previewBackgroundB = 0.07f;
        public float previewBackgroundA = 0.85f;

        // ---------------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------------

        /// <summary>InputSystem-Binding zum Togglen des Panels.</summary>
        public string toggleBinding = "<Keyboard>/c";
    }

    /// <summary>Einzelner Equipment-Slot mit Layout-Position.</summary>
    [Serializable]
    public sealed class CharacterSlotLayout
    {
        /// <summary>Welcher EquipSlot an dieser Position dargestellt wird.</summary>
        public EquipSlot slot;

        /// <summary>X-Position relativ zum Panel-Origin (oben-links).</summary>
        public int x;

        /// <summary>Y-Position relativ zum Panel-Origin (oben-links).</summary>
        public int y;
    }
}
