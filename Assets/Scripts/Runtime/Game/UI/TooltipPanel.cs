using System.Collections.Generic;
using System.Text;
using Riftstorm.ApplicationLifecycle.UI;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Items;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
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
        /// <summary>Maximale Breite des Tooltip-Panels (WoW-Klassik, etwas breiter fuer 1080p+).</summary>
        public const float DefaultWidth = 400f;

        /// <summary>Vertikaler Abstand zwischen Anker und Tooltip.</summary>
        public const float DefaultGap = 8f;

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
            panel.style.paddingLeft = 12f;
            panel.style.paddingRight = 12f;
            panel.style.paddingTop = 10f;
            panel.style.paddingBottom = 10f;
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
            m_NameLabel.style.fontSize = 22;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_NameLabel.style.whiteSpace = WhiteSpace.Normal;
            UIFonts.Apply(m_NameLabel, UIFonts.Body);
            panel.Add(m_NameLabel);

            m_MetaLabel = new Label { name = "tooltip-meta" };
            m_MetaLabel.style.color = new Color(0.78f, 0.78f, 0.82f, 1f);
            m_MetaLabel.style.fontSize = 17;
            m_MetaLabel.style.marginTop = 4f;
            m_MetaLabel.style.whiteSpace = WhiteSpace.Normal;
            UIFonts.Apply(m_MetaLabel, UIFonts.Body);
            panel.Add(m_MetaLabel);

            m_DescLabel = new Label { name = "tooltip-description" };
            m_DescLabel.style.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            m_DescLabel.style.fontSize = 17;
            m_DescLabel.style.marginTop = 6f;
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
            float h = m_Root.resolvedStyle.height > 0f ? m_Root.resolvedStyle.height : 110f;

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
        /// Caster-loser Fallback: zeigt die Mana-Formel literal.
        /// </summary>
        public static string BuildSpellMeta(SpellTemplate tpl)
        {
            return BuildSpellMeta(tpl, null);
        }

        /// <summary>
        /// Caster-bewusste Variante: loest <see cref="SpellTemplate.ManaFormula"/>
        /// gegen den uebergebenen Caster auf (clvl/splvl/Stats), sodass im
        /// Tooltip die echte Mana-Zahl steht statt einer Formel.
        /// <paramref name="caster"/> = <c>null</c> faellt auf die Roh-Formel zurueck.
        /// </summary>
        public static string BuildSpellMeta(SpellTemplate tpl, ICombatUnit caster)
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
                if (caster != null)
                {
                    int mana = SpellUtils.CalculateManaCost(tpl, caster);
                    if (mana > 0)
                    {
                        parts.Add($"{mana} Mana");
                    }
                }
                else
                {
                    parts.Add($"Mana {tpl.ManaFormula}");
                }
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
            return GetAuraDescription(tpl, null);
        }

        /// <summary>
        /// Caster-bewusste Variante: loest <c>$E&lt;N&gt;min</c>, <c>$DUR</c>
        /// etc. gegen den uebergebenen <paramref name="caster"/> auf.
        /// <paramref name="caster"/> = <c>null</c> haelt die Tokens literal.
        /// </summary>
        public static string GetAuraDescription(SpellTemplate tpl, ICombatUnit caster)
        {
            if (tpl == null) { return string.Empty; }
            string raw = !string.IsNullOrWhiteSpace(tpl.AuraDescription)
                ? tpl.AuraDescription
                : (tpl.Description ?? string.Empty);
            return caster != null ? SpellTooltipFormatter.Format(raw, tpl, caster) : raw;
        }

        /// <summary>
        /// Liefert den besten Beschreibungs-Text fuer einen aktiven Spell-Slot:
        /// bevorzugt <see cref="SpellTemplate.Description"/>, faellt auf
        /// <see cref="SpellTemplate.AuraDescription"/> zurueck.
        /// </summary>
        public static string GetSpellDescription(SpellTemplate tpl)
        {
            return GetSpellDescription(tpl, null);
        }

        /// <summary>
        /// Caster-bewusste Variante: loest WoW-/FLARE-Platzhalter
        /// (<c>$E1min</c>, <c>$E1max</c>, <c>$DUR</c>, ...) gegen den
        /// uebergebenen <paramref name="caster"/> auf. <paramref name="caster"/>
        /// = <c>null</c> haelt die Tokens literal stehen.
        /// </summary>
        public static string GetSpellDescription(SpellTemplate tpl, ICombatUnit caster)
        {
            if (tpl == null) { return string.Empty; }
            string raw = !string.IsNullOrWhiteSpace(tpl.Description)
                ? tpl.Description
                : (tpl.AuraDescription ?? string.Empty);
            return caster != null ? SpellTooltipFormatter.Format(raw, tpl, caster) : raw;
        }

        // ---------------------------------------------------------------------
        // Builder fuer Item-Tooltips (Inventory + Character)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Liefert den Anzeige-Namen eines Items, mit Fallback auf <c>#Entry</c>
        /// wenn der Template-Name leer ist.
        /// </summary>
        public static string GetItemDisplayName(ItemTemplate tpl)
        {
            if (tpl == null)
            {
                return "?";
            }
            return string.IsNullOrWhiteSpace(tpl.Name) ? $"#{tpl.Entry}" : tpl.Name;
        }

        /// <summary>
        /// Baut die Meta-Zeile fuer ein Item: Quality - Slot/Type - iLvl -
        /// Required-Level. Leere Felder werden uebersprungen. Bullet-Separator
        /// identisch zu <see cref="BuildSpellMeta(SpellTemplate)"/>.
        /// </summary>
        public static string BuildItemMeta(ItemTemplate tpl)
        {
            if (tpl == null)
            {
                return string.Empty;
            }
            List<string> parts = new(5);
            parts.Add(GetItemQualityLabel(tpl.Quality));
            string slotLabel = GetItemSlotLabel(tpl);
            if (!string.IsNullOrEmpty(slotLabel))
            {
                parts.Add(slotLabel);
            }
            if (tpl.ItemLevel > 0)
            {
                parts.Add($"iLvl {tpl.ItemLevel}");
            }
            if (tpl.RequiredLevel > 0)
            {
                parts.Add($"Requires Level {tpl.RequiredLevel}");
            }
            return string.Join(" \u2022 ", parts);
        }

        /// <summary>
        /// Baut den Beschreibungs-Block fuer ein Item: Stat-Boni (eine Zeile
        /// pro <see cref="ItemTemplate.StatType1"/>..<c>StatType4</c>),
        /// optional Flavor-Description und Vendor-Sell-Price. <see cref="StackCount"/>
        /// wird ueber den Caller mit der aktuellen Stueckzahl gemerged (siehe
        /// optionale Overload).
        /// </summary>
        public static string GetItemDescription(ItemTemplate tpl)
        {
            return GetItemDescription(tpl, count: 0);
        }

        /// <summary>
        /// Variante mit aktueller Stueckzahl (z. B. Inventory-Stack). Zeigt
        /// bei stackbaren Items zusaetzlich "Stack: N / Max". <paramref name="count"/>
        /// = 0 unterdrueckt die Zeile.
        /// </summary>
        public static string GetItemDescription(ItemTemplate tpl, int count)
        {
            if (tpl == null)
            {
                return string.Empty;
            }
            StringBuilder sb = new(160);
            AppendWeaponDamageLine(sb, tpl);
            AppendItemStatLine(sb, tpl.StatType1, tpl.StatValue1);
            AppendItemStatLine(sb, tpl.StatType2, tpl.StatValue2);
            AppendItemStatLine(sb, tpl.StatType3, tpl.StatValue3);
            AppendItemStatLine(sb, tpl.StatType4, tpl.StatValue4);
            if (tpl.IsStackable && count > 1)
            {
                if (sb.Length > 0) { sb.Append('\n'); }
                sb.Append("Stack: ").Append(count).Append(" / ").Append(tpl.StackCount);
            }
            if (!string.IsNullOrWhiteSpace(tpl.Description))
            {
                if (sb.Length > 0) { sb.Append('\n'); }
                sb.Append(tpl.Description);
            }
            if (tpl.SellPrice > 0)
            {
                if (sb.Length > 0) { sb.Append('\n'); }
                sb.Append("Sell: ").Append(tpl.SellPrice).Append('c');
            }
            return sb.ToString();
        }

        // ---- Item-Label-Helpers (Source: ItemDefines.h Quality + EquipSlot) ----

        private static string GetItemQualityLabel(int quality) => quality switch
        {
            5 => "Legendary",
            4 => "Epic",
            3 => "Rare",
            2 => "Uncommon",
            1 => "Common",
            _ => "Poor",
        };

        private static string GetItemSlotLabel(ItemTemplate tpl)
        {
            if (tpl.EquipType <= 0)
            {
                return string.Empty;
            }
            // EquipSlot-Enum spiegelt 1:1 Source-EquipType (Helm=1..Ranged=11),
            // plus Ring2=12 als UI-Extra. Cast ist sicher fuer alle Werte
            // > 0 — unbekannte Werte landen auf der int-Repraesentation.
            EquipSlot slot = (EquipSlot)tpl.EquipType;
            return System.Enum.IsDefined(typeof(EquipSlot), slot)
                ? slot.ToString()
                : $"Equip #{tpl.EquipType}";
        }

        private static void AppendItemStatLine(StringBuilder sb, int statTypeId, int value)
        {
            if (statTypeId <= 0 || value == 0)
            {
                return;
            }
            StatId id = (StatId)statTypeId;
            string label = GetItemStatLabel(id, statTypeId);
            if (sb.Length > 0) { sb.Append('\n'); }
            string sign = value > 0 ? "+" : string.Empty;
            sb.Append(sign).Append(value).Append(' ').Append(label);
        }

        /// <summary>
        /// Liest die Waffen-Definition aus dem <see cref="WeaponCatalog"/> ueber
        /// <see cref="ItemTemplate.Model"/> und h&#228;ngt eine Damage-/Speed-
        /// Zeile an den Tooltip an. Nur fuer Waffen-Slots (EquipType=Mainhand/
        /// Ranged) und nur wenn der Catalog bereits via ServiceLocator
        /// verfuegbar ist (sonst stillschweigend &#252;berspringen — der reine
        /// Stat-Block bleibt darunter sichtbar).
        /// </summary>
        private static void AppendWeaponDamageLine(StringBuilder sb, ItemTemplate tpl)
        {
            if (tpl == null || tpl.WeaponType <= 0 || string.IsNullOrEmpty(tpl.Model))
            {
                return;
            }
            WeaponCatalogLoader loader = ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = loader?.GetCached();
            if (catalog == null || !catalog.TryGet(tpl.Model, out WeaponDefinition weapon) || weapon == null)
            {
                return;
            }
            if (sb.Length > 0) { sb.Append('\n'); }
            sb.Append(weapon.BaseDamage).Append(" Damage");
            if (weapon.AttackCooldown > 0f)
            {
                sb.Append("  (").Append(weapon.AttackCooldown.ToString("0.00")).Append("s)");
                float dps = weapon.BaseDamage / weapon.AttackCooldown;
                sb.Append("  ").Append(dps.ToString("0.0")).Append(" DPS");
            }
        }

        private static string GetItemStatLabel(StatId id, int rawId) => id switch
        {
            StatId.Health => "Health",
            StatId.ArmorValue => "Armor",
            StatId.Strength => "Strength",
            StatId.Agility => "Agility",
            StatId.Willpower => "Willpower",
            StatId.Intelligence => "Intelligence",
            StatId.WeaponValue => "Weapon Damage",
            StatId.RangedWeaponValue => "Ranged Damage",
            StatId.MeleeCritical => "Melee Crit",
            StatId.RangedCritical => "Ranged Crit",
            StatId.SpellCritical => "Spell Crit",
            StatId.DodgeRating => "Dodge",
            StatId.BlockRating => "Block",
            StatId.ResistFrost => "Frost Resist",
            StatId.ResistFire => "Fire Resist",
            StatId.ResistShadow => "Shadow Resist",
            StatId.ResistHoly => "Holy Resist",
            _ => $"Stat #{rawId}",
        };
    }
}
