using System;

namespace Riftstorm.Game.UI.Inventory
{
    /// <summary>
    /// Datengetriebene Konfiguration fuer das Inventar-Panel.
    /// Quelle: <c>StreamingAssets/interface/inventory_config.json</c>.
    /// Felder tragen Defaults; fehlende Keys uebernehmen diese.
    /// </summary>
    /// <remarks>
    /// Textur-Keys sind relative Pfade unter <c>Application.dataPath/Art</c>
    /// ohne Extension (z. B. <c>"interface/inventory"</c>), aufgeloest ueber
    /// den <see cref="Riftstorm.Management.TextureManagement.TextureManager"/>.
    /// </remarks>
    [Serializable]
    public sealed class InventoryConfig
    {
        // ---------------------------------------------------------------------
        // Panel-Anchor (rechts-unten verankert, wie WoW-Default)
        // ---------------------------------------------------------------------

        /// <summary>Panel-Breite in Pixeln (sollte zur Aspect-Ratio der Hintergrund-Textur passen).</summary>
        public float panelWidth = 380f;
        /// <summary>Panel-Hoehe in Pixeln.</summary>
        public float panelHeight = 460f;
        /// <summary>Abstand des Panels vom rechten Bildschirmrand.</summary>
        public float anchorRight = 16f;
        /// <summary>Abstand des Panels vom unteren Bildschirmrand.</summary>
        public float anchorBottom = 220f;

        // ---------------------------------------------------------------------
        // Grid-Layout (7x7 = 49 Slots, deckt PlayerInventory.Capacity)
        // ---------------------------------------------------------------------

        /// <summary>Spalten im Slot-Grid.</summary>
        public int columns = 7;
        /// <summary>Zeilen im Slot-Grid.</summary>
        public int rows = 7;
        /// <summary>Linker Inset des Grids im Panel.</summary>
        public float gridLeft = 22f;
        /// <summary>Oberer Inset des Grids im Panel.</summary>
        public float gridTop = 60f;
        /// <summary>Kantenlaenge eines Slots in Pixeln.</summary>
        public float slotSize = 40f;
        /// <summary>Abstand zwischen zwei Slots in Pixeln.</summary>
        public float slotSpacing = 4f;

        // ---------------------------------------------------------------------
        // Texturen
        // ---------------------------------------------------------------------

        /// <summary>Hintergrund-Textur des Inventar-Panels.</summary>
        public string backgroundTexture = "interface/inventory";
        /// <summary>Idle-Frame fuer leere Slots (optional).</summary>
        public string slotIdleTexture = "interface/icons/gameicon40_idle";
        /// <summary>Hover-Frame, wird beim Mouseover ueber den Idle-Frame gelegt.</summary>
        public string slotHoverTexture = "interface/icons/gameicon40_hover";

        // ---------------------------------------------------------------------
        // Stack-Count-Label
        // ---------------------------------------------------------------------

        /// <summary>Schriftgroesse des Stack-Count-Labels rechts-unten im Slot.</summary>
        public float countFontSize = 11f;

        // ---------------------------------------------------------------------
        // Gold-Anzeige (Phase 17 Placeholder; PlayerWallet folgt spaeter)
        // ---------------------------------------------------------------------

        /// <summary>Linker Inset des Gold-Labels im Panel.</summary>
        public float goldLeft = 22f;
        /// <summary>Abstand des Gold-Labels vom unteren Panel-Rand.</summary>
        public float goldBottom = 22f;
        /// <summary>Schriftgroesse des Gold-Labels.</summary>
        public float goldFontSize = 16f;

        // ---------------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------------

        /// <summary>InputSystem-Binding-Path fuer den Toggle (Default: <c>I</c>).</summary>
        public string toggleBinding = "<Keyboard>/i";

        // ---------------------------------------------------------------------
        // Item-Icon-Resolution
        // ---------------------------------------------------------------------

        /// <summary>Praefix unter dem <see cref="Riftstorm.Game.Items.ItemTemplate.Icon"/> aufgeloest wird (z. B. <c>item_icons/icon_item_potion_hp02_1</c>).</summary>
        public string itemIconKeyPrefix = "item_icons/";
    }
}
