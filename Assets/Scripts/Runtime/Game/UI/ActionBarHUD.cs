using Riftstorm.ApplicationLifecycle.UI;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Input;
using Riftstorm.Game.Spells;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// WoW-Style Action-Bar-HUD: eine horizontale Bar unten in der Mitte
    /// (Slots gebunden an <see cref="PlayerInputController.SpellSlotCount"/> +
    /// duenne XP-Bar darunter) sowie zwei vertikale Bars am rechten Bildrand
    /// (je <see cref="RightSlotCount"/> Slots, rein dekorativ).
    /// <para>
    /// Untere Slots werden zur Laufzeit an die Spell-Loadout-Eintraege aus
    /// <see cref="PlayerSpellInput.SlotEntries"/> des lokalen Spielers gebunden
    /// und zeigen pro Slot: Spell-Icon (aus <see cref="SpellTemplate.Icon"/>),
    /// vertikaler Cooldown-Sweep (WoW-Klassik: dunkler Overlay schrumpft von
    /// 100% auf 0% Hoehe) und Restzeit-Label in Sekunden. GCD wird ueberlagert:
    /// pro Slot wird das Maximum aus Spell-CD und Global-CD angezeigt (LoL/WoW-Konvention).
    /// </para>
    /// <para>
    /// Bar-Hintergruende und XP-Fill stammen aus <see cref="HudConfig"/>
    /// (Texturen <c>actionbar_base</c> und <c>xp_bar</c>). Spell-Icons werden
    /// ueber <c>spell_icons/{Icon}</c> aufgeloest (siehe <see cref="NormalizeSpellIconKey"/>).
    /// </para>
    /// <para>
    /// Cooldowns liegen server-authoritativ in <see cref="CooldownManager"/>
    /// auf <see cref="UnitStats"/> (Zugriff via <see cref="ICombatUnit.Cooldowns"/>).
    /// Im Host- oder Client-Host-Modus ist dieser Container fuer den lokalen
    /// Spieler direkt lesbar; pure Remote-Clients ohne Sync-Layer sehen 0-Werte
    /// — ein separater Netcode-Pass fuer Cooldown-Replikation ist out of scope.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ActionBarHUD : MonoBehaviour
    {
        /// <summary>Anzahl Slots in der rechten Vertikal-Bar (rein dekorativ).</summary>
        private const int RightSlotCount = 12;

        /// <summary>Semi-transparenter Cooldown-Overlay (gleicher Ton wie <see cref="UnitAuraBarUI"/>).</summary>
        private static readonly Color SweepOverlayColor = new(0f, 0f, 0f, 0.55f);

        private UIDocument m_Document;
        private VisualElement m_Root;

        // Bottom-Slot-Tracking: ein Eintrag pro gebundenem Hotkey-Slot.
        private ActionSlotBinding[] m_BottomSlots;

        // Geteiltes Tooltip-Panel fuer alle Slots (WoW-Style: ein einziges Overlay,
        // wird beim Hover umpositioniert und neu befuellt). Initial unsichtbar.
        private TooltipPanel m_Tooltip;

        // Lokaler Spieler (zur Laufzeit gebunden, sobald NGO einen LocalClient hat).
        private PlayerSpellInput m_BoundInput;
        private UnitStats m_BoundStats;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildVisualTree();
        }

        private void OnDisable()
        {
            // Bindings loslassen — bei Scene-Reload baut OnEnable den Tree neu auf.
            m_BoundInput = null;
            m_BoundStats = null;
            m_BottomSlots = null;
            m_Tooltip = null;
        }

        private void Update()
        {
            // Erst Local-Player binden, sobald NGO ein PlayerObject geliefert hat
            // (mirror <see cref="PlayerFrameUI.TryBindLocalPlayer"/>).
            if (m_BoundInput == null || m_BoundStats == null)
            {
                TryBindLocalPlayer();
            }
            TickCooldowns();
        }

        private void BuildVisualTree()
        {
            m_Root = m_Document.rootVisualElement;
            if (m_Root == null)
            {
                return;
            }
            m_Root.Clear();
            m_Root.pickingMode = PickingMode.Ignore;

            HudConfig cfg = HudConfigLoader.Load();
            Texture2D baseTex = HudConfigLoader.LoadTextureOrNull(cfg.actionBarBaseTexture);
            Texture2D xpTex = HudConfigLoader.LoadTextureOrNull(cfg.actionBarXpFillTexture);
            Texture2D slotIdleTex = HudConfigLoader.LoadTextureOrNull(cfg.actionSlotIconIdleTexture);
            Texture2D slotHoverTex = HudConfigLoader.LoadTextureOrNull(cfg.actionSlotIconHoverTexture);
            Texture2D slotPressTex = HudConfigLoader.LoadTextureOrNull(cfg.actionSlotIconPressTexture);

            BuildBottomBar(m_Root, cfg, baseTex, xpTex, slotIdleTex, slotHoverTex, slotPressTex);

            // Tooltip-Overlay als letztes Kind anhaengen, damit es per Z-Order
            // immer ueber den Bars liegt. Eine einzige Instanz fuer alle Slots.
            m_Tooltip = new TooltipPanel(m_Root);

            float rightOffset = cfg.actionBarRightMargin;
            for (int i = 0; i < cfg.actionBarRightCount; i++)
            {
                BuildRightBar(m_Root, cfg, baseTex, rightOffset, slotIdleTex, slotHoverTex, slotPressTex);
                rightOffset += cfg.actionBarRightWidth + cfg.actionBarRightSpacing;
            }
        }

        // -------------------------------------------------------------------------
        // Bottom Bar (Action-Bar-Base-Textur + N Hotkey-Slots + XP-Bar darunter)
        // -------------------------------------------------------------------------

        private void BuildBottomBar(VisualElement root, HudConfig cfg, Texture2D baseTex, Texture2D xpTex,
            Texture2D slotIdleTex, Texture2D slotHoverTex, Texture2D slotPressTex)
        {
            int slotCount = PlayerInputController.SpellSlotCount;

            float width = cfg.actionBarBottomWidth;
            float height = cfg.actionBarBottomHeight;
            float slotSize = cfg.actionBarBottomSlotSize;
            float xpHeight = cfg.actionBarBottomXpHeight;

            // Aeusserer Wrapper: zentriert, traegt Bar + XP untereinander.
            VisualElement wrapper = new() { name = "action-bar-bottom" };
            wrapper.style.position = Position.Absolute;
            wrapper.style.bottom = cfg.actionBarBottomMargin;
            wrapper.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            wrapper.style.translate = new StyleTranslate(new Translate(
                new Length(-50f, LengthUnit.Percent), 0f, 0f));
            wrapper.style.width = width;
            wrapper.style.flexDirection = FlexDirection.Column;
            wrapper.style.alignItems = Align.Center;
            wrapper.pickingMode = PickingMode.Ignore;

            // Bar-Hintergrund mit eingebrannten Slot-Wells.
            VisualElement bar = new() { name = "action-bar-bottom-base" };
            bar.style.width = width;
            bar.style.height = height;
            if (baseTex != null)
            {
                bar.style.backgroundImage = new StyleBackground(baseTex);
            }

            // XP-Bar als Overlay INNERHALB der Basis-Textur (League/WoW-Style:
            // duenner Balken unten in die Bar gezeichnet). Wird zuerst dem bar
            // hinzugefuegt, damit die Slot-Reihe danach darueber liegt.
            VisualElement xpRow = HudStyle.BuildTexturedBar(
                "action-bar-xp",
                xpTex,
                width - 2f * cfg.actionBarBottomXpInsetX,
                xpHeight,
                fillFromRight: false,
                out VisualElement xpFill,
                out Label xpValue);
            xpRow.style.position = Position.Absolute;
            xpRow.style.left = cfg.actionBarBottomXpInsetX;
            xpRow.style.bottom = cfg.actionBarBottomXpInsetBottom;
            // Track-Backdrop, damit der XP-Slot auch bei 0% Fuellung sichtbar
            // ist (xp_bar.png ist nur die Fuellung, keine Rinne). Dunkles
            // Semi-Transparent, passt zu den Slot-Wells der Basis-Textur.
            xpRow.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            xpFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            // Wert-Text einblenden: zeigt initial "0%", bis das XP-System
            // echte Werte liefert. Schrift dezent kleiner, damit sie in den
            // schmalen Balken passt.
            xpValue.text = "0%";
            xpValue.style.fontSize = Mathf.Max(9f, xpHeight * 0.85f);
            UIFonts.Apply(xpValue, UIFonts.Numeric);
            bar.Add(xpRow);

            // Slot-Reihe als Container, absolut ueber den eingebrannten Wells.
            // Slots werden INNERHALB der Reihe absolut positioniert (jede Position
            // einzeln auf ganze Pixel gerundet), damit sich keine Subpixel-Fehler
            // aufsummieren wie bei flex/marginLeft. Reihenbreite = N*slotSize +
            // (N-1)*gap; die Reihe wird in der Bar horizontal zentriert.
            float gap = cfg.actionBarBottomSlotGap;
            float rowWidth = slotCount * slotSize + (slotCount - 1) * gap;
            float rowLeft = Mathf.Round((width - rowWidth) * 0.5f + cfg.actionBarBottomSlotsRowOffsetX);

            VisualElement slotsRow = new() { name = "action-bar-bottom-slots" };
            slotsRow.style.position = Position.Absolute;
            slotsRow.style.top = cfg.actionBarBottomSlotInsetY;
            slotsRow.style.left = rowLeft;
            slotsRow.style.width = rowWidth;
            slotsRow.style.height = slotSize;

            m_BottomSlots = new ActionSlotBinding[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                string bind = GetBottomKeyBind(i);
                VisualElement slot = baseTex != null
                    ? HudStyle.BuildTexturedActionSlot(slotSize, bind, slotIdleTex, slotHoverTex, slotPressTex)
                    : HudStyle.BuildActionSlot((int)slotSize, bind);
                slot.style.position = Position.Absolute;
                slot.style.top = 0f;
                // Jede Position unabhaengig runden -> Fehler bleibt <= 0.5px pro
                // Slot statt sich linear aufzusummieren. Pro-Slot-Korrektur aus
                // Config addieren, falls die eingebrannten Wells der PNG nicht
                // exakt aequidistant sind.
                float perSlotOffset = (cfg.actionBarBottomSlotOffsetsX != null
                                       && i < cfg.actionBarBottomSlotOffsetsX.Length)
                    ? cfg.actionBarBottomSlotOffsetsX[i]
                    : 0f;
                slot.style.left = Mathf.Round(i * (slotSize + gap) + perSlotOffset);

                // Icon / Cooldown-Sweep / Restzeit-Label als Overlay-Layer einhaengen.
                m_BottomSlots[i] = AttachSpellOverlay(slot);

                slotsRow.Add(slot);
            }
            bar.Add(slotsRow);
            wrapper.Add(bar);

            root.Add(wrapper);
        }

        /// <summary>
        /// Ergaenzt einen vorhandenen Slot-Container (aus <see cref="HudStyle"/>)
        /// um die drei Spell-Overlays: Icon, Cooldown-Sweep, Restzeit-Label. Alle
        /// drei sind <c>PickingMode.Ignore</c>, damit Hover/Press am Slot-Root
        /// weiterhin funktioniert. Reihenfolge: die Overlays werden VOR den
        /// vorhandenen Kindern (insbesondere dem Keybind-Label) eingehaengt,
        /// damit Keybind und Press-Border oben bleiben.
        /// </summary>
        private ActionSlotBinding AttachSpellOverlay(VisualElement slot)
        {
            int insertIndex = 0;

            VisualElement icon = new() { name = "action-slot-icon" };
            icon.style.position = Position.Absolute;
            icon.style.left = 0;
            icon.style.right = 0;
            icon.style.top = 0;
            icon.style.bottom = 0;
            icon.pickingMode = PickingMode.Ignore;
            icon.style.display = DisplayStyle.None;
            slot.Insert(insertIndex++, icon);

            VisualElement sweep = new() { name = "action-slot-sweep" };
            sweep.style.position = Position.Absolute;
            sweep.style.left = 0;
            sweep.style.right = 0;
            sweep.style.top = 0;
            sweep.style.height = new StyleLength(new Length(0f, LengthUnit.Percent));
            sweep.style.backgroundColor = SweepOverlayColor;
            sweep.pickingMode = PickingMode.Ignore;
            slot.Insert(insertIndex++, sweep);

            Label cdLabel = new() { name = "action-slot-cd", text = string.Empty };
            cdLabel.style.position = Position.Absolute;
            cdLabel.style.left = 0;
            cdLabel.style.right = 0;
            cdLabel.style.top = 0;
            cdLabel.style.bottom = 0;
            cdLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            cdLabel.style.color = Color.white;
            cdLabel.style.fontSize = 16;
            cdLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            cdLabel.style.textShadow = new TextShadow
            {
                offset = new Vector2(1f, 1f),
                blurRadius = 1f,
                color = new Color(0f, 0f, 0f, 0.9f),
            };
            UIFonts.Apply(cdLabel, UIFonts.Numeric);
            cdLabel.pickingMode = PickingMode.Ignore;
            slot.Insert(insertIndex, cdLabel);

            ActionSlotBinding binding = new()
            {
                SlotRoot = slot,
                IconElement = icon,
                SweepElement = sweep,
                CooldownLabel = cdLabel,
                SpellEntry = 0,
                MaxCooldownMs = 0,
            };

            // Tooltip-Hover: einmalig pro Slot registrieren. Beim Enter wird der
            // aktuelle Binding-State (SpellEntry) ausgewertet — leere Slots zeigen
            // keinen Tooltip. Lambdas capturen das Binding-Objekt, nicht den Entry,
            // damit BindSlot()/UnbindSlot() ohne Re-Registrierung funktioniert.
            slot.RegisterCallback<MouseEnterEvent>(_ => ShowSlotTooltip(binding));
            slot.RegisterCallback<MouseLeaveEvent>(_ => m_Tooltip?.Hide());

            return binding;
        }

        /// <summary>Hotkey-Labels parallel zu <see cref="PlayerInputController"/>-Bindings ("1"…"9","0").</summary>
        private static readonly string[] BottomKeyBinds =
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
        };

        private static string GetBottomKeyBind(int index)
        {
            return index >= 0 && index < BottomKeyBinds.Length
                ? BottomKeyBinds[index]
                : string.Empty;
        }

        // -------------------------------------------------------------------------
        // LocalPlayer Binding + Cooldown-Tick
        // -------------------------------------------------------------------------

        private void TryBindLocalPlayer()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient)
            {
                return;
            }
            NetworkObject playerObj = nm.LocalClient?.PlayerObject;
            if (playerObj == null)
            {
                return;
            }

            PlayerSpellInput input = playerObj.GetComponent<PlayerSpellInput>()
                ?? playerObj.GetComponentInChildren<PlayerSpellInput>();
            UnitStats stats = playerObj.GetComponent<UnitStats>()
                ?? playerObj.GetComponentInChildren<UnitStats>();
            if (input == null || stats == null)
            {
                return;
            }

            m_BoundInput = input;
            m_BoundStats = stats;

            RefreshSlotIcons();
        }

        /// <summary>
        /// Initial-Belegung der Slot-Icons aus dem aktuellen Loadout. Wird einmalig
        /// beim Binden des lokalen Spielers aufgerufen — Slot-Inhalt aendert sich
        /// aktuell zur Laufzeit nicht (kein Loadout-Swap-System).
        /// </summary>
        private void RefreshSlotIcons()
        {
            if (m_BottomSlots == null || m_BoundInput == null)
            {
                return;
            }
            System.Collections.Generic.IReadOnlyList<int> entries = m_BoundInput.SlotEntries;
            int count = Mathf.Min(m_BottomSlots.Length, entries.Count);
            for (int i = 0; i < count; i++)
            {
                BindSlot(m_BottomSlots[i], entries[i]);
            }
        }

        private static void BindSlot(ActionSlotBinding binding, int spellEntry)
        {
            if (binding == null)
            {
                return;
            }
            if (spellEntry <= 0)
            {
                ClearSlot(binding);
                return;
            }

            SpellTemplate template = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (template == null)
            {
                Debug.LogWarning($"[ActionBarHUD] Slot-Eintrag {spellEntry} hat keinen SpellTemplate — Slot bleibt leer.");
                ClearSlot(binding);
                return;
            }

            Texture2D icon = HudConfigLoader.LoadTextureOrNull(NormalizeSpellIconKey(template.Icon));
            if (icon != null)
            {
                binding.IconElement.style.backgroundImage = new StyleBackground(icon);
                binding.IconElement.style.backgroundColor = new StyleColor(StyleKeyword.Initial);
                binding.IconElement.style.display = DisplayStyle.Flex;
            }
            else
            {
                // Kein Icon vorhanden: dezent dunkle Fuellung, damit der Slot trotzdem
                // belegt aussieht; Warnung, damit Designer fehlende Icons schnell finden.
                binding.IconElement.style.backgroundImage = StyleKeyword.Null;
                binding.IconElement.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.75f);
                binding.IconElement.style.display = DisplayStyle.Flex;
                if (!string.IsNullOrEmpty(template.Icon))
                {
                    Debug.LogWarning($"[ActionBarHUD] Icon '{template.Icon}' fuer Spell {spellEntry} nicht gefunden.");
                }
            }

            binding.SpellEntry = spellEntry;
            // Total-Cooldown cachen: <see cref="CooldownManager"/> liefert nur Remaining,
            // kein Max — die Sweep-Prozent-Berechnung braucht aber beides.
            binding.MaxCooldownMs = Mathf.Max(0, template.Cooldown);
        }

        private static void ClearSlot(ActionSlotBinding binding)
        {
            binding.SpellEntry = 0;
            binding.MaxCooldownMs = 0;
            binding.IconElement.style.display = DisplayStyle.None;
            binding.IconElement.style.backgroundImage = StyleKeyword.Null;
            binding.SweepElement.style.height = new StyleLength(new Length(0f, LengthUnit.Percent));
            binding.CooldownLabel.text = string.Empty;
        }

        private void TickCooldowns()
        {
            if (m_BottomSlots == null || m_BoundStats == null)
            {
                return;
            }

            CooldownManager cd = ((ICombatUnit)m_BoundStats).Cooldowns;
            if (cd == null)
            {
                return;
            }

            int gcdRemaining = cd.GetRemainingGcd();
            int gcdTotal = CooldownManager.GcdDurationMs;

            for (int i = 0; i < m_BottomSlots.Length; i++)
            {
                ActionSlotBinding b = m_BottomSlots[i];
                if (b == null || b.SpellEntry <= 0)
                {
                    continue;
                }

                int spellRemaining = cd.GetRemainingCooldown(b.SpellEntry);
                int spellTotal = b.MaxCooldownMs;

                // LoL/WoW-Konvention: laengeren Cooldown anzeigen. GCD ueberlagert alle
                // Slots, Spell-CD nur den eigenen Slot.
                int displayRemaining;
                int displayTotal;
                if (spellRemaining > 0 && spellRemaining >= gcdRemaining)
                {
                    displayRemaining = spellRemaining;
                    displayTotal = spellTotal > 0 ? spellTotal : spellRemaining;
                }
                else if (gcdRemaining > 0)
                {
                    displayRemaining = gcdRemaining;
                    displayTotal = gcdTotal;
                }
                else
                {
                    displayRemaining = 0;
                    displayTotal = 0;
                }

                ApplyCooldownVisual(b, displayRemaining, displayTotal);
            }
        }

        private static void ApplyCooldownVisual(ActionSlotBinding b, int remainingMs, int totalMs)
        {
            if (remainingMs <= 0 || totalMs <= 0)
            {
                b.SweepElement.style.height = new StyleLength(new Length(0f, LengthUnit.Percent));
                b.CooldownLabel.text = string.Empty;
                return;
            }

            // Sweep faellt von 100% (frisch) auf 0% (bereit).
            float pct = Mathf.Clamp01((float)remainingMs / totalMs) * 100f;
            b.SweepElement.style.height = new StyleLength(new Length(pct, LengthUnit.Percent));

            float remainingSec = remainingMs / 1000f;
            if (remainingSec >= 10f)
            {
                b.CooldownLabel.text = Mathf.CeilToInt(remainingSec).ToString();
            }
            else
            {
                // Unter 10s: eine Nachkommastelle, damit der Countdown weich wirkt.
                b.CooldownLabel.text = remainingSec.ToString("0.0");
            }
        }

        /// <summary>
        /// Spiegelt <see cref="UnitAuraBarUI"/>-Logik: blanke Icon-Dateinamen
        /// (z. B. <c>"Whirlwind.png"</c>) auf <c>spell_icons/Whirlwind</c> mappen,
        /// damit <see cref="HudConfigLoader.LoadTextureOrNull"/> den Pfad aufloest.
        /// Lokal dupliziert statt cross-class exportiert, weil der Helper in
        /// <see cref="UnitAuraBarUI"/> privat ist und ein eigener Helper-Typ hier
        /// nichts spart.
        /// </summary>
        private static string NormalizeSpellIconKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
            string normalized = raw.Replace('\\', '/');
            int dot = normalized.LastIndexOf('.');
            int slash = normalized.LastIndexOf('/');
            if (dot > slash)
            {
                normalized = normalized[..dot];
            }
            if (normalized.IndexOf('/') < 0)
            {
                // Item- bzw. Scroll-Icons liegen unter Art/item_icons/, alle restlichen
                // Spell-Icons unter Art/spell_icons/ (Konvention aus dem Source-Export:
                // Item-Use-Effekte sind Spells, deren Icon-Datei mit "item_" oder
                // "icon_item_" beginnt — z. B. Tranke und Scrolls).
                bool isItem = normalized.StartsWith("item_", System.StringComparison.OrdinalIgnoreCase)
                              || normalized.StartsWith("icon_item_", System.StringComparison.OrdinalIgnoreCase);
                normalized = (isItem ? "item_icons/" : "spell_icons/") + normalized;
            }
            return normalized;
        }

        // -------------------------------------------------------------------------
        // Tooltip-Bruecke (Visual-Tree liegt in <see cref="TooltipPanel"/>, hier
        // nur das Slot-spezifische Befuellen).
        // -------------------------------------------------------------------------

        private void ShowSlotTooltip(ActionSlotBinding binding)
        {
            if (m_Tooltip == null || binding == null || binding.SpellEntry <= 0)
            {
                return;
            }
            SpellTemplate tpl = SpellCatalogLoader.GetTemplateOrNull(binding.SpellEntry);
            if (tpl == null)
            {
                return;
            }
            string name = string.IsNullOrWhiteSpace(tpl.Name) ? $"#{tpl.Entry}" : tpl.Name;
            m_Tooltip.Show(
                name,
                TooltipPanel.BuildSpellMeta(tpl),
                TooltipPanel.GetSpellDescription(tpl),
                binding.SlotRoot.worldBound,
                TooltipPlacement.Above);
        }

        /// <summary>
        /// Per-Slot-Tracking-Struktur. Haelt sowohl die VisualElements (Icon/Sweep/Label)
        /// als auch den gerade gebundenen Spell-Entry und dessen Total-Cooldown
        /// (fuer die Sweep-Prozent-Berechnung).
        /// </summary>
        private sealed class ActionSlotBinding
        {
            public VisualElement SlotRoot;
            public VisualElement IconElement;
            public VisualElement SweepElement;
            public Label CooldownLabel;
            public int SpellEntry;
            public int MaxCooldownMs;
        }

        // -------------------------------------------------------------------------
        // Right Vertical Bar (12 Slots, Basis-Textur 90deg rotiert)
        // -------------------------------------------------------------------------

        private static void BuildRightBar(VisualElement root, HudConfig cfg, Texture2D baseTex, float rightOffset,
            Texture2D slotIdleTex, Texture2D slotHoverTex, Texture2D slotPressTex)
        {
            float w = cfg.actionBarRightWidth;
            float h = cfg.actionBarRightHeight;
            float slotSize = cfg.actionBarRightSlotSize;

            VisualElement container = new() { name = "action-bar-right" };
            container.style.position = Position.Absolute;
            container.style.right = rightOffset;
            container.style.top = new StyleLength(new Length(50f, LengthUnit.Percent));
            container.style.translate = new StyleTranslate(new Translate(
                0f, new Length(-50f, LengthUnit.Percent), 0f));
            container.style.width = w;
            container.style.height = h;
            container.pickingMode = PickingMode.Ignore;

            // Rotierte Basis-Textur als reine Hintergrund-Ebene.
            // Pre-Rotation hat das Element die Masse der unteren Bar (lange Achse
            // horizontal), wird dann um die Mitte 90deg gedreht und sitzt damit
            // vertikal exakt im Container.
            if (baseTex != null)
            {
                VisualElement bg = new() { name = "action-bar-right-bg" };
                bg.style.position = Position.Absolute;
                // Pre-Rotation: width = lange Achse (= Container-Hoehe),
                //               height = kurze Achse (= Container-Breite).
                bg.style.width = h;
                bg.style.height = w;
                // Zentrieren: pre-rotation linke obere Ecke so platzieren, dass
                // der Mittelpunkt des Bildes mit dem Mittelpunkt des Containers
                // zusammenfaellt.
                bg.style.left = (w - h) * 0.5f;
                bg.style.top = (h - w) * 0.5f;
                bg.style.backgroundImage = new StyleBackground(baseTex);
                bg.style.rotate = new StyleRotate(new Rotate(new Angle(cfg.actionBarRightRotationDegrees, AngleUnit.Degree)));
                bg.pickingMode = PickingMode.Ignore;
                container.Add(bg);
            }

            // Aufrechte Slot-Spalte ueber der rotierten Basis. Slots werden
            // einzeln absolut positioniert (jede Y-Position auf ganze Pixel
            // gerundet), damit sich keine Subpixel-Fehler aufsummieren. Pro-Slot-
            // Korrektur aus Config gleicht Asymmetrien in der gemalten Basis aus
            // (analog zur unteren Bar).
            float rightGap = cfg.actionBarRightSlotGap;
            float colHeight = RightSlotCount * slotSize + (RightSlotCount - 1) * rightGap;
            float colTop = Mathf.Round((h - colHeight) * 0.5f + cfg.actionBarRightSlotsColOffsetY);
            float colLeft = Mathf.Round((w - slotSize) * 0.5f);

            VisualElement slotsCol = new() { name = "action-bar-right-slots" };
            slotsCol.style.position = Position.Absolute;
            slotsCol.style.top = colTop;
            slotsCol.style.left = colLeft;
            slotsCol.style.width = slotSize;
            slotsCol.style.height = colHeight;

            for (int i = 0; i < RightSlotCount; i++)
            {
                VisualElement slot = baseTex != null
                    ? HudStyle.BuildTexturedActionSlot(slotSize, keyBind: null, slotIdleTex, slotHoverTex, slotPressTex)
                    : HudStyle.BuildActionSlot((int)slotSize, keyBind: null);
                slot.style.position = Position.Absolute;
                slot.style.left = 0f;
                float perSlotOffset = (cfg.actionBarRightSlotOffsetsY != null
                                       && i < cfg.actionBarRightSlotOffsetsY.Length)
                    ? cfg.actionBarRightSlotOffsetsY[i]
                    : 0f;
                slot.style.top = Mathf.Round(i * (slotSize + rightGap) + perSlotOffset);
                slotsCol.Add(slot);
            }
            container.Add(slotsCol);

            root.Add(container);
        }
    }
}

