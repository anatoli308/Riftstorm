using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Geteilte Styling-Helfer fuer das ingame-HUD (Player-/Target-Frame,
    /// Action-Bars). Haelt Farben und Frame-/Bar-Baukloetze an EINER Stelle,
    /// damit die einzelnen Panels visuell konsistent bleiben (Dark Panel mit
    /// Gold-Border, einheitliche HP/Mana/XP-Bar-Geometrie).
    /// </summary>
    internal static class HudStyle
    {
        public static readonly Color AccentGold = new(0.78f, 0.65f, 0.20f, 1f);
        public static readonly Color PanelBackground = new(0.04f, 0.04f, 0.06f, 0.82f);
        public static readonly Color BarTrack = new(0.10f, 0.10f, 0.12f, 1f);
        public static readonly Color SlotBackground = new(0.06f, 0.06f, 0.08f, 0.92f);

        /// <summary>Wendet den dunklen Panel-Look mit gold-getoenter Border an.</summary>
        public static void ApplyFramePanel(VisualElement frame)
        {
            frame.style.paddingTop = 8f;
            frame.style.paddingBottom = 8f;
            frame.style.paddingLeft = 10f;
            frame.style.paddingRight = 10f;
            frame.style.backgroundColor = new StyleColor(PanelBackground);
            ApplyBorder(frame, AccentGold, 2f);
        }

        /// <summary>Einheitliche Rahmenfarbe + -breite fuer alle vier Seiten.</summary>
        public static void ApplyBorder(VisualElement el, Color color, float width)
        {
            el.style.borderTopColor = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor = color;
            el.style.borderRightColor = color;
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
        }

        /// <summary>
        /// Erzeugt eine horizontale Bar-Reihe (HP/Mana/XP-Stil): dunkle Track,
        /// farbiges Fill (default 100% Breite), zentriertes Wert-Label.
        /// </summary>
        public static VisualElement BuildBarRow(string baseName, Color fillColor, out VisualElement fill, out Label valueLabel)
        {
            VisualElement row = new() { name = baseName };
            row.style.height = 18f;
            row.style.backgroundColor = new StyleColor(BarTrack);
            ApplyBorder(row, new Color(0f, 0f, 0f, 0.6f), 1f);
            row.style.overflow = Overflow.Hidden;

            fill = new VisualElement { name = baseName + "-fill" };
            fill.style.position = Position.Absolute;
            fill.style.top = 0f;
            fill.style.left = 0f;
            fill.style.bottom = 0f;
            fill.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            fill.style.backgroundColor = new StyleColor(fillColor);
            row.Add(fill);

            valueLabel = new Label("0 / 0") { name = baseName + "-value" };
            valueLabel.style.position = Position.Absolute;
            valueLabel.style.top = 0f;
            valueLabel.style.left = 0f;
            valueLabel.style.right = 0f;
            valueLabel.style.bottom = 0f;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            valueLabel.style.color = Color.white;
            valueLabel.style.fontSize = 12f;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(valueLabel);

            return row;
        }

        /// <summary>
        /// Erzeugt einen leeren Action-Bar-Slot (dunkles Quadrat mit
        /// Tastatur-Binding-Label oben rechts). Inhalt (Icon, Cooldown) wird
        /// vom Skill-/Inventory-System spaeter befuellt.
        /// </summary>
        public static VisualElement BuildActionSlot(int size, string keyBind)
        {
            VisualElement slot = new() { name = "action-slot" };
            slot.style.width = size;
            slot.style.height = size;
            slot.style.marginLeft = 2f;
            slot.style.marginRight = 2f;
            slot.style.marginTop = 2f;
            slot.style.marginBottom = 2f;
            slot.style.backgroundColor = new StyleColor(SlotBackground);
            ApplyBorder(slot, AccentGold, 1f);

            if (!string.IsNullOrEmpty(keyBind))
            {
                Label bind = new(keyBind) { name = "action-slot-bind" };
                bind.style.position = Position.Absolute;
                bind.style.top = 1f;
                bind.style.right = 3f;
                bind.style.fontSize = 10f;
                bind.style.color = new StyleColor(new Color(0.95f, 0.92f, 0.70f, 0.9f));
                bind.style.unityFontStyleAndWeight = FontStyle.Bold;
                slot.Add(bind);
            }
            return slot;
        }
    }
}
