using Riftstorm.Management.FontManagement;
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
            UIFonts.Apply(valueLabel, UIFonts.Numeric);
            row.Add(valueLabel);

            return row;
        }

        // -------------------------------------------------------------------
        // Textured Unit-Frame Helpers (LoL/WoW-Style mit gemalten Sprites)
        // -------------------------------------------------------------------

        /// <summary>
        /// Erzeugt das Wurzel-Element eines Unit-Frames mit einer
        /// gemalten Hintergrund-Textur (z. B. <c>unit_frame.png</c> oder
        /// <c>unit_frame_reverse.png</c>). Kein Panel-Hintergrund, kein
        /// Border \u2014 die Optik kommt komplett aus dem Sprite.
        /// </summary>
        public static VisualElement BuildTexturedFrame(Texture2D background, float width, float height)
        {
            VisualElement frame = new() { name = "unit-frame-root" };
            frame.style.width = width;
            frame.style.height = height;
            if (background != null)
            {
                frame.style.backgroundImage = new StyleBackground(background);
            }
            return frame;
        }

        /// <summary>
        /// Erzeugt ein Portrait-Rund: dunkler Kreis als Platzhalter, ueber
        /// dem spaeter ein 3D-/Sprite-Portrait gerendert werden kann.
        /// Border-Radius = size/2 \u2014 die Maske wird so kreisrund.
        /// </summary>
        public static VisualElement BuildPortraitCircle(float size)
        {
            VisualElement portrait = new() { name = "unit-frame-portrait" };
            portrait.style.position = Position.Absolute;
            portrait.style.width = size;
            portrait.style.height = size;
            // Kein Platzhalter-Fill: der Frame-Sprite zeichnet den Portrait-Sockel
            // bereits selbst. Border-Radius + Overflow bleiben, damit ein spaeter
            // hinzugefuegtes Portrait-Bild kreisfoermig maskiert wird.
            float r = size * 0.5f;
            portrait.style.borderTopLeftRadius = r;
            portrait.style.borderTopRightRadius = r;
            portrait.style.borderBottomLeftRadius = r;
            portrait.style.borderBottomRightRadius = r;
            portrait.style.overflow = Overflow.Hidden;
            return portrait;
        }

        /// <summary>
        /// Erzeugt eine Level-Badge auf Basis von <c>unit_frame_level_bg.png</c>.
        /// Positionierung muss vom Caller via <c>style.left/right/top/bottom</c>
        /// erfolgen \u2014 die Badge selbst ist Position.Absolute.
        /// </summary>
        public static VisualElement BuildLevelBadge(Texture2D background, float size, out Label levelLabel)
        {
            VisualElement badge = new() { name = "unit-frame-level-badge" };
            badge.style.position = Position.Absolute;
            badge.style.width = size;
            badge.style.height = size;
            if (background != null)
            {
                badge.style.backgroundImage = new StyleBackground(background);
            }

            levelLabel = new Label("1") { name = "unit-frame-level" };
            levelLabel.style.position = Position.Absolute;
            levelLabel.style.left = 0f;
            levelLabel.style.right = 0f;
            levelLabel.style.top = 0f;
            levelLabel.style.bottom = 0f;
            levelLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            levelLabel.style.color = new StyleColor(new Color(0.95f, 0.92f, 0.70f, 1f));
            levelLabel.style.fontSize = Mathf.Max(10f, size * 0.55f);
            levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            UIFonts.Apply(levelLabel, UIFonts.Numeric);
            badge.Add(levelLabel);

            return badge;
        }

        /// <summary>
        /// Erzeugt eine gemalte HP-/Mana-Bar auf Basis einer Fill-Textur
        /// (z. B. <c>unit_frame_hp.png</c>). Die Bar ist Position.Absolute
        /// und muss vom Caller via <c>style.left/right/top</c> ueber dem
        /// passenden Bereich des Frame-Hintergrunds platziert werden.
        /// Wenn <paramref name="fillFromRight"/> true ist, schrumpft die
        /// Fuellung nach links \u2014 fuer das gespiegelte Target-Frame.
        /// </summary>
        public static VisualElement BuildTexturedBar(
            string baseName,
            Texture2D fillTexture,
            float width,
            float height,
            bool fillFromRight,
            out VisualElement fill,
            out Label valueLabel)
        {
            VisualElement track = new() { name = baseName };
            track.style.position = Position.Absolute;
            track.style.width = width;
            track.style.height = height;
            track.style.overflow = Overflow.Hidden;

            fill = new VisualElement { name = baseName + "-fill" };
            fill.style.position = Position.Absolute;
            fill.style.top = 0f;
            fill.style.bottom = 0f;
            if (fillFromRight)
            {
                fill.style.right = 0f;
            }
            else
            {
                fill.style.left = 0f;
            }
            fill.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            if (fillTexture != null)
            {
                fill.style.backgroundImage = new StyleBackground(fillTexture);
            }
            track.Add(fill);

            valueLabel = new Label("0 / 0") { name = baseName + "-value" };
            valueLabel.style.position = Position.Absolute;
            valueLabel.style.left = 0f;
            valueLabel.style.right = 0f;
            valueLabel.style.top = 0f;
            valueLabel.style.bottom = 0f;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            valueLabel.style.color = Color.white;
            valueLabel.style.fontSize = Mathf.Max(10f, height * 0.7f);
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            UIFonts.Apply(valueLabel, UIFonts.Numeric);
            track.Add(valueLabel);

            return track;
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
                UIFonts.Apply(bind, UIFonts.Keybind);
                slot.Add(bind);
            }
            return slot;
        }

        /// <summary>
        /// Erzeugt einen Action-Bar-Slot, der auf einer gemalten Action-Bar-Textur
        /// (mit eingebrannten Slot-Wells) sitzt. Anders als <see cref="BuildActionSlot"/>
        /// traegt dieser Slot keinen eigenen Hintergrund und keinen Gold-Border \u2014
        /// die Optik kommt komplett aus der darunterliegenden Bar-Textur. Nur das
        /// Keybind-Label wird oben rechts platziert; Icon und Cooldown fuellt
        /// spaeter das Skill-System ein.
        /// <para>
        /// Optional kann eine Icon-Cell (idle/hover/press Texturen) als Overlay
        /// gerendert werden \u2014 dann reagiert der Slot auf Maus-Events. Fehlt
        /// idleTex, bleibt der Slot ein reiner Layout-Platzhalter (alte Optik).
        /// </para>
        /// </summary>
        public static VisualElement BuildTexturedActionSlot(
            float size,
            string keyBind,
            Texture2D idleTex = null,
            Texture2D hoverTex = null,
            Texture2D pressTex = null)
        {
            VisualElement slot = new() { name = "action-slot" };
            slot.style.width = size;
            slot.style.height = size;

            if (idleTex != null)
            {
                slot.style.backgroundImage = new StyleBackground(idleTex);
                // Slot reagiert nur auf Maus-Events, wenn eine Icon-Cell vorhanden ist.
                // Sonst bleibt pickingMode == Position (Default), wir lassen das aber
                // bewusst stehen, weil das Skill-System spaeter Click-Handler anhaengt.
                slot.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    slot.style.backgroundImage = new StyleBackground(hoverTex != null ? hoverTex : idleTex);
                });
                slot.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    slot.style.backgroundImage = new StyleBackground(idleTex);
                });
                slot.RegisterCallback<PointerDownEvent>(_ =>
                {
                    slot.style.backgroundImage = new StyleBackground(pressTex != null ? pressTex : idleTex);
                });
                slot.RegisterCallback<PointerUpEvent>(_ =>
                {
                    // Nach dem Loslassen wieder Hover (Maus ist noch ueber dem Slot)
                    // oder Idle (Pointer-Capture verloren). UI-Toolkit feuert nach
                    // PointerUp KEIN automatisches PointerEnter, daher hier manuell.
                    slot.style.backgroundImage = new StyleBackground(hoverTex != null ? hoverTex : idleTex);
                });
            }

            if (!string.IsNullOrEmpty(keyBind))
            {
                Label bind = new(keyBind) { name = "action-slot-bind" };
                bind.style.position = Position.Absolute;
                bind.style.top = 2f;
                bind.style.right = 4f;
                bind.style.fontSize = 11f;
                bind.style.color = new StyleColor(new Color(0.95f, 0.92f, 0.70f, 0.95f));
                bind.style.unityFontStyleAndWeight = FontStyle.Bold;
                UIFonts.Apply(bind, UIFonts.Keybind);
                slot.Add(bind);
            }
            return slot;
        }
    }
}
