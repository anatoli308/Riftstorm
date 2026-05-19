using System.Collections.Generic;
using Riftstorm.ApplicationLifecycle.UI;
using Riftstorm.Game.Spells;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Wo der Tooltip relativ zum Anker-Rect platziert wird.
    /// </summary>
    public enum TooltipPlacement
    {
        /// <summary>Ueber dem Anker (Default fuer ActionBar — Bar liegt unten am Bildrand).</summary>
        Above = 0,
        /// <summary>Unter dem Anker (z. B. Aura-Icons oben links).</summary>
        Below = 1,
    }

    /// <summary>
    /// Wiederverwendbares Tooltip-Overlay-Panel im WoW-Klassik-Stil:
    /// dunkler Hintergrund, gold-getoenter Rahmen, drei Text-Zeilen (Name,
    /// Meta, Description). Eine Instanz pro HUD-Komponente — wird beim
    /// Hover umpositioniert und neu befuellt.
    /// </summary>
    /// <remarks>
    /// Texte werden bewusst auf Englisch gehalten (Source-Daten sind
    /// englisch, MMO-Convention). Ein einzelnes Panel kann von beliebig
    /// vielen Slot-Bindings geteilt werden.
    /// </remarks>
    public sealed class TooltipPanel
    {
        /// <summary>Maximale Breite des Tooltip-Panels (WoW-Klassik ~260px).</summary>
        public const float DefaultWidth = 260f;

        /// <summary>Vertikaler Abstand zwischen Anker und Tooltip.</summary>
        public const float DefaultGap = 6f;

        private readonly VisualElement m_Root;
        private readonly Label m_NameLabel;
        private readonly Label m_MetaLabel;
        private readonly Label m_DescLabel;

        /// <summary>Root-Element (read-only). Liegt im Eltern-Container, das das HUD anlegt.</summary>
        public VisualElement Root => m_Root;

        /// <summary>Baut das Panel als Kind von <paramref name="parent"/>. Initial unsichtbar.</summary>
        public TooltipPanel(VisualElement parent, float width = DefaultWidth)
        {
            VisualElement panel = new() { name = "tooltip-panel" };
            panel.style.position = Position.Absolute;
            panel.style.left = 0f;
            panel.style.top = 0f;
            panel.style.width = width;
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 6f;
            panel.style.paddingBottom = 6f;
            panel.style.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 0.95f);
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderBottomWidth = 1f;
            Color border = new(1f, 0.84f, 0.0f, 0.6f);
            panel.style.borderLeftColor = border;
            panel.style.borderRightColor = border;
            panel.style.borderTopColor = border;
            panel.style.borderBottomColor = border;
            panel.style.display = DisplayStyle.None;
            panel.pickingMode = PickingMode.Ignore;

            m_NameLabel = new Label { name = "tooltip-name" };
            m_NameLabel.style.color = new Color(1f, 0.84f, 0.0f, 1f);
            m_NameLabel.style.fontSize = 14;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_NameLabel.style.whiteSpace = WhiteSpace.Normal;
            UIFonts.Apply(m_NameLabel, UIFonts.Body);
            panel.Add(m_NameLabel);

            m_MetaLabel = new Label { name = "tooltip-meta" };
            m_MetaLabel.style.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            m_MetaLabel.style.fontSize = 11;
            m_MetaLabel.style.marginTop = 2f;
            m_MetaLabel.style.whiteSpace = WhiteSpace.Normal;
            UIFonts.Apply(m_MetaLabel, UIFonts.Body);
            panel.Add(m_MetaLabel);

            m_DescLabel = new Label { name = "tooltip-description" };
            m_DescLabel.style.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            m_DescLabel.style.fontSize = 11;
            m_DescLabel.style.marginTop = 4f;
            m_DescLabel.style.whiteSpace = WhiteSpace.Normal;
            UIFonts.Apply(m_DescLabel, UIFonts.Body);
            panel.Add(m_DescLabel);

            parent.Add(panel);
            m_Root = panel;
        }

        // ---------------------------------------------------------------------
        // Anzeige
        // ---------------------------------------------------------------------

        /// <summary>
        /// Befuellt und positioniert das Panel ueber/unter <paramref name="anchorWorld"/>
        /// (worldBound des Hover-Targets). Leerer Name oder Description blendet
        /// die jeweilige Zeile aus.
        /// </summary>
        public void Show(string name, string meta, string description, Rect anchorWorld,
            TooltipPlacement placement = TooltipPlacement.Above)
        {
            m_NameLabel.text = string.IsNullOrWhiteSpace(name) ? "?" : name;

            bool hasMeta = !string.IsNullOrWhiteSpace(meta);
            m_MetaLabel.text = hasMeta ? meta : string.Empty;
            m_MetaLabel.style.display = hasMeta ? DisplayStyle.Flex : DisplayStyle.None;

            bool hasDesc = !string.IsNullOrWhiteSpace(description);
            m_DescLabel.text = hasDesc ? description : string.Empty;
            m_DescLabel.style.display = hasDesc ? DisplayStyle.Flex : DisplayStyle.None;

            // Positionierung: relativ zur eigenen Eltern-Element-Box.
            // resolvedStyle.height ist beim ersten Show noch 0 → 80px-Fallback.
            VisualElement parent = m_Root.parent;
            if (parent == null)
            {
                // Niemand hat uns angehaengt — defensives No-Op, sollte nicht passieren.
                return;
            }
            Rect panelBox = parent.worldBound;
            float w = m_Root.resolvedStyle.width > 0f ? m_Root.resolvedStyle.width : DefaultWidth;
            float h = m_Root.resolvedStyle.height > 0f ? m_Root.resolvedStyle.height : 80f;

            float left = Mathf.Clamp(anchorWorld.xMin - panelBox.xMin, 0f, Mathf.Max(0f, panelBox.width - w));
            float top = placement == TooltipPlacement.Above
                ? Mathf.Max(0f, anchorWorld.yMin - panelBox.yMin - h - DefaultGap)
                : Mathf.Min(panelBox.height - h, anchorWorld.yMax - panelBox.yMin + DefaultGap);

            m_Root.style.left = left;
            m_Root.style.top = top;
            m_Root.style.display = DisplayStyle.Flex;
            m_Root.BringToFront();
        }

        /// <summary>Blendet das Panel aus. Idempotent.</summary>
        public void Hide()
        {
            m_Root.style.display = DisplayStyle.None;
        }

        // ---------------------------------------------------------------------
        // Builder fuer Spell-Tooltips (English)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Baut die Meta-Zeile fuer einen Action-Bar-Slot (Range / Mana / Cast / CD).
        /// Texte hart in Englisch — entsprechend der Source-Datenbasis.
        /// </summary>
        public static string BuildSpellMeta(SpellTemplate tpl)
        {
            if (tpl == null)
            {
                return string.Empty;
            }
            List<string> parts = new(4);
            if (tpl.Range > 0f)
            {
                parts.Add($"Range {tpl.Range:0.#}m");
            }
            if (!string.IsNullOrWhiteSpace(tpl.ManaFormula) && tpl.ManaFormula != "0")
            {
                parts.Add($"Mana {tpl.ManaFormula}");
            }
            parts.Add(tpl.CastTime > 0 ? $"Cast {tpl.CastTime / 1000f:0.0}s" : "Instant");
            if (tpl.Cooldown > 0)
            {
                parts.Add($"Cooldown {tpl.Cooldown / 1000f:0.0}s");
            }
            return string.Join(" \u2022 ", parts);
        }

        /// <summary>
        /// Baut die Meta-Zeile fuer ein Aura-Icon (Buff/Debuff/HoT/DoT).
        /// Zeigt Buff/Debuff-Klassifizierung, optional Stacks und Restdauer
        /// im aktuellen Snapshot.
        /// </summary>
        public static string BuildAuraMeta(bool isPositive, int stacks, int remainingMs, int maxDurationMs)
        {
            List<string> parts = new(3);
            parts.Add(isPositive ? "Buff" : "Debuff");
            if (stacks > 1)
            {
                parts.Add($"{stacks} stacks");
            }
            if (maxDurationMs > 0 && remainingMs > 0)
            {
                float secs = remainingMs / 1000f;
                string fmt = secs >= 60f
                    ? $"{Mathf.CeilToInt(secs / 60f)}m"
                    : (secs >= 10f ? Mathf.CeilToInt(secs).ToString() : secs.ToString("0.0"));
                parts.Add($"{fmt}s remaining");
            }
            else if (maxDurationMs <= 0)
            {
                parts.Add("Permanent");
            }
            return string.Join(" \u2022 ", parts);
        }

        /// <summary>
        /// Liefert den besten verfuegbaren Beschreibungs-Text fuer eine Aura:
        /// bevorzugt <see cref="SpellTemplate.AuraDescription"/>, faellt auf
        /// <see cref="SpellTemplate.Description"/> zurueck.
        /// </summary>
        public static string GetAuraDescription(SpellTemplate tpl)
        {
            if (tpl == null) { return string.Empty; }
            if (!string.IsNullOrWhiteSpace(tpl.AuraDescription))
            {
                return tpl.AuraDescription;
            }
            return tpl.Description ?? string.Empty;
        }

        /// <summary>
        /// Liefert den besten Beschreibungs-Text fuer einen aktiven Spell-Slot:
        /// bevorzugt <see cref="SpellTemplate.Description"/>, faellt auf
        /// <see cref="SpellTemplate.AuraDescription"/> zurueck.
        /// </summary>
        public static string GetSpellDescription(SpellTemplate tpl)
        {
            if (tpl == null) { return string.Empty; }
            if (!string.IsNullOrWhiteSpace(tpl.Description))
            {
                return tpl.Description;
            }
            return tpl.AuraDescription ?? string.Empty;
        }
    }
}
