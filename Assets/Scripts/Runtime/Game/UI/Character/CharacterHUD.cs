using System.Collections.Generic;
using System.Text;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Items;
using Riftstorm.Game.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI.Character
{
    /// <summary>
    /// UIToolkit-HUD fuer das Charakter-Panel: equipment-Slots (12) plus
    /// Stats-Spalte (Phase 17C). Visualisiert <see cref="PlayerEquipment"/>
    /// und <see cref="UnitStats"/> des lokalen Spielers; Toggle via C-Taste.
    /// </summary>
    /// <remarks>
    /// Bindings spiegeln <see cref="Riftstorm.Game.UI.Inventory.InventoryHUD"/>:
    /// das HUD lebt auf einem MonoBehaviour mit <c>UIDocument</c>; lokaler
    /// Spieler wird in <see cref="Update"/> nach-gebunden, sobald NGO ein
    /// <c>PlayerObject</c> geliefert hat. Rechtsklick auf belegten Slot
    /// triggert <see cref="PlayerEquipment.RequestUnequipServerRpc"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterHUD : MonoBehaviour
    {
        // ---------------------------------------------------------------------
        // Konstanten / USS-Klassen
        // ---------------------------------------------------------------------

        private const string k_RootName = "character-hud-root";
        private const string k_PanelName = "character-panel";
        private const string k_SlotClass = "character-slot";
        private const string k_SlotIconClass = "character-slot__icon";
        private const string k_StatsName = "character-stats";

        // ---------------------------------------------------------------------
        // Felder
        // ---------------------------------------------------------------------

        private UIDocument m_Document;
        private CharacterConfig m_Config;

        private VisualElement m_Root;
        private VisualElement m_Panel;

        /// <summary>Lookup: EquipSlot -> Icon-VisualElement im Panel.</summary>
        private readonly Dictionary<EquipSlot, VisualElement> m_SlotIcons = new();

        private Label m_StatsLabel;

        private InputAction m_ToggleAction;
        private bool m_Visible;

        private PlayerEquipment m_BoundEquipment;
        private PlayerInventory m_BoundInventory; // nur fuer Item-Catalog-Lookups
        private UnitStats m_BoundStats;

        /// <summary>Verhindert Log-Spam in <see cref="TryBindLocalPlayer"/>: nur
        /// ein Diagnose-Eintrag pro PlayerObject-Instanz, sobald ein Bind-Versuch
        /// scheitert (typisch fehlende <see cref="UnitStats"/> am Prefab).</summary>
        private int m_LastDiagPlayerInstanceId;

        /// <summary>Geteiltes Tooltip-Overlay; eine Instanz fuer alle 12 Equip-Slots.</summary>
        private TooltipPanel m_Tooltip;

        /// <summary>Aktuell gehoverter Slot oder <see cref="EquipSlot.None"/>.</summary>
        private EquipSlot m_HoveredSlot = EquipSlot.None;

        // ---------------------------------------------------------------------
        // Unity-Lifecycle
        // ---------------------------------------------------------------------

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
            m_Config = CharacterConfigLoader.Load();
        }

        private void OnEnable()
        {
            BuildVisualTree();
            ApplyVisibility(false);

            m_ToggleAction = new(name: "CharacterToggle", type: InputActionType.Button, binding: m_Config.toggleBinding);
            m_ToggleAction.performed += OnToggleCharacter;
            m_ToggleAction.Enable();
        }

        private void OnDisable()
        {
            if (m_ToggleAction != null)
            {
                m_ToggleAction.performed -= OnToggleCharacter;
                m_ToggleAction.Disable();
                m_ToggleAction.Dispose();
                m_ToggleAction = null;
            }

            UnbindLocalPlayer();

            m_Root = null;
            m_Panel = null;
            m_SlotIcons.Clear();
            m_StatsLabel = null;
            m_Tooltip = null;
            m_HoveredSlot = EquipSlot.None;
        }

        private void Update()
        {
            if (m_BoundEquipment == null)
            {
                TryBindLocalPlayer();
            }
        }

        // ---------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------

        private void BuildVisualTree()
        {
            VisualElement docRoot = m_Document.rootVisualElement;
            if (docRoot == null)
            {
                Debug.LogWarning("[CharacterHUD] UIDocument hat kein rootVisualElement.");
                return;
            }

            m_Root = new() { name = k_RootName };
            m_Root.style.position = Position.Absolute;
            m_Root.style.left = 0;
            m_Root.style.right = 0;
            m_Root.style.top = 0;
            m_Root.style.bottom = 0;
            m_Root.pickingMode = PickingMode.Ignore;
            docRoot.Add(m_Root);

            m_Panel = new() { name = k_PanelName };
            m_Panel.style.position = Position.Absolute;
            m_Panel.style.left = m_Config.anchorLeft;
            m_Panel.style.bottom = m_Config.anchorBottom;
            m_Panel.style.width = m_Config.panelWidth;
            m_Panel.style.height = m_Config.panelHeight;

            Texture2D bg = CharacterConfigLoader.LoadTextureOrNull(m_Config.backgroundTexture);
            if (bg != null)
            {
                m_Panel.style.backgroundImage = new StyleBackground(bg);
            }
            m_Root.Add(m_Panel);

            BuildSlots();
            BuildStatsLabel();

            // Tooltip-Overlay als letztes Kind, damit es per Z-Order ueber
            // Slots + Stats-Label liegt (gleiche Konvention wie ActionBarHUD).
            m_Tooltip = new(m_Root);
        }

        private void BuildSlots()
        {
            if (m_Config.slots == null)
            {
                return;
            }

            Texture2D idleFrame = CharacterConfigLoader.LoadTextureOrNull(m_Config.slotIdleTexture);
            Texture2D hoverFrame = CharacterConfigLoader.LoadTextureOrNull(m_Config.slotHoverTexture);
            Texture2D pressFrame = CharacterConfigLoader.LoadTextureOrNull(m_Config.slotPressTexture);

            foreach (CharacterSlotLayout layout in m_Config.slots)
            {
                if (layout == null)
                {
                    continue;
                }
                EquipSlot slotKey = layout.slot;
                if (m_SlotIcons.ContainsKey(slotKey))
                {
                    Debug.LogWarning($"[CharacterHUD] Duplicate slot layout fuer {slotKey} - ignoriert.");
                    continue;
                }

                // Gleiche Optik + Hover/Press-Logik wie ActionBars: idle/hover/press
                // Frame-Swap kommt aus HudStyle, kein eigener Pointer-Handler noetig.
                VisualElement slotElem = HudStyle.BuildTexturedActionSlot(
                    m_Config.slotSize, keyBind: null, idleFrame, hoverFrame, pressFrame);
                slotElem.name = $"character-slot-{slotKey}";
                slotElem.AddToClassList(k_SlotClass);
                slotElem.style.position = Position.Absolute;
                slotElem.style.left = layout.x;
                slotElem.style.top = layout.y;

                VisualElement icon = new();
                icon.AddToClassList(k_SlotIconClass);
                icon.style.position = Position.Absolute;
                icon.style.left = 0;
                icon.style.right = 0;
                icon.style.top = 0;
                icon.style.bottom = 0;
                icon.pickingMode = PickingMode.Ignore;
                slotElem.Add(icon);

                EquipSlot captured = slotKey; // Local-Capture fuer Closure.
                slotElem.RegisterCallback<ClickEvent>(evt => OnSlotClicked(captured, evt));

                // Tooltip-Hover: Item-Details on Enter, Hide on Leave. HudStyle
                // registriert intern bereits PointerEnter/Leave fuer den
                // Frame-Swap; zusaetzliche Handler stoeren das nicht.
                slotElem.RegisterCallback<PointerEnterEvent>(_ => ShowSlotTooltip(captured));
                slotElem.RegisterCallback<PointerLeaveEvent>(_ => HideSlotTooltip(captured));

                m_Panel.Add(slotElem);
                m_SlotIcons[slotKey] = icon;
            }
        }

        private void BuildStatsLabel()
        {
            m_StatsLabel = new() { name = k_StatsName };
            m_StatsLabel.style.position = Position.Absolute;
            m_StatsLabel.style.left = m_Config.statsLeft;
            m_StatsLabel.style.top = m_Config.statsTop;
            m_StatsLabel.style.width = m_Config.statsWidth;
            m_StatsLabel.style.fontSize = m_Config.statsFontSize;
            m_StatsLabel.style.color = Color.white;
            m_StatsLabel.style.whiteSpace = WhiteSpace.Normal;
            m_StatsLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            m_StatsLabel.pickingMode = PickingMode.Ignore;
            m_StatsLabel.text = string.Empty;
            m_Panel.Add(m_StatsLabel);
        }

        // ---------------------------------------------------------------------
        // Local-Player-Binding
        // ---------------------------------------------------------------------

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

            PlayerEquipment equipment = playerObj.GetComponent<PlayerEquipment>()
                ?? playerObj.GetComponentInChildren<PlayerEquipment>();
            PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>()
                ?? playerObj.GetComponentInChildren<PlayerInventory>();
            UnitStats stats = playerObj.GetComponent<UnitStats>()
                ?? playerObj.GetComponentInChildren<UnitStats>();

            // Diagnose: einmal pro PlayerObject loggen, was am Prefab fehlt.
            // Ohne Log waere ein leeres Stats-Label / nicht-bindendes Equipment
            // im Build kaum zu unterscheiden von "noch nicht gespawnt".
            int pid = playerObj.GetInstanceID();
            if (pid != m_LastDiagPlayerInstanceId && (equipment == null || inventory == null || stats == null))
            {
                m_LastDiagPlayerInstanceId = pid;
                Debug.LogWarning(
                    $"[CharacterHUD] Bind incomplete on '{playerObj.name}': "
                    + $"PlayerEquipment={(equipment != null ? "ok" : "MISSING")}, "
                    + $"PlayerInventory={(inventory != null ? "ok" : "MISSING")}, "
                    + $"UnitStats={(stats != null ? "ok" : "MISSING")}.");
            }

            if (equipment == null || inventory == null)
            {
                return;
            }

            m_BoundEquipment = equipment;
            m_BoundInventory = inventory;
            m_BoundStats = stats;

            m_BoundEquipment.EquipChanged += OnEquipChanged;
            if (m_BoundStats != null)
            {
                m_BoundStats.HpChanged += OnHpOrManaChanged;
                m_BoundStats.ManaChanged += OnHpOrManaChanged;
            }

            // Initiale Belegung: alle 12 Slots durchrendern + Stats-Snapshot.
            foreach (KeyValuePair<EquipSlot, VisualElement> pair in m_SlotIcons)
            {
                RenderSlot(pair.Key, m_BoundEquipment.GetEquipped(pair.Key));
            }
            RefreshStats();
        }

        private void UnbindLocalPlayer()
        {
            if (m_BoundEquipment != null)
            {
                m_BoundEquipment.EquipChanged -= OnEquipChanged;
                m_BoundEquipment = null;
            }
            if (m_BoundStats != null)
            {
                m_BoundStats.HpChanged -= OnHpOrManaChanged;
                m_BoundStats.ManaChanged -= OnHpOrManaChanged;
                m_BoundStats = null;
            }
            m_BoundInventory = null;
        }

        // ---------------------------------------------------------------------
        // Render
        // ---------------------------------------------------------------------

        private void OnEquipChanged(EquipSlot slot, int templateId)
        {
            RenderSlot(slot, templateId);
            // Gear-Recompute kann Stats anpassen (Armor, WeaponDamage, ...).
            RefreshStats();
        }

        private void RenderSlot(EquipSlot slot, int templateId)
        {
            if (!m_SlotIcons.TryGetValue(slot, out VisualElement icon) || icon == null)
            {
                return;
            }

            if (templateId <= 0)
            {
                icon.style.backgroundImage = new StyleBackground((Texture2D)null);
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate tpl))
            {
                Debug.LogWarning($"[CharacterHUD] Kein Template fuer Entry {templateId} (Slot {slot}).");
                icon.style.backgroundImage = new StyleBackground((Texture2D)null);
                return;
            }

            Texture2D iconTex = CharacterConfigLoader.LoadItemIconOrNull(tpl.Icon, m_Config.itemIconKeyPrefix);
            icon.style.backgroundImage = iconTex != null
                ? new StyleBackground(iconTex)
                : new StyleBackground((Texture2D)null);
        }

        // ---------------------------------------------------------------------
        // Stats (Phase 17C)
        // ---------------------------------------------------------------------

        private void OnHpOrManaChanged(int previous, int current) => RefreshStats();

        private void RefreshStats()
        {
            if (m_StatsLabel == null)
            {
                return;
            }
            if (m_BoundStats == null)
            {
                // Ohne UnitStats am Prefab waere das Panel komplett leer und
                // sichtbar kaputt; Platzhalter signalisieren "verbunden, aber
                // keine Stats-Quelle" und decken sich mit der Diagnose im
                // Bind-Pfad (siehe TryBindLocalPlayer).
                m_StatsLabel.text =
                    "Level -\nHP   -\n\nSTR  -\nINT  -\nWIL  -\nARM  -\nDMG  -\n"
                    + "\nCrit  -\nDodge -\nParry -\nBlock -\n"
                    + "\nFire   -\nFrost  -\nArcane -\nNature -\nShadow -";
                return;
            }

            StringBuilder sb = new(256);
            sb.Append("Level ").Append(m_BoundStats.Level).Append('\n');
            sb.Append("HP   ").Append(m_BoundStats.CurrentHp).Append(" / ").Append(m_BoundStats.MaxHp).Append('\n');
            if (m_BoundStats.HasMana)
            {
                sb.Append("MP   ").Append(m_BoundStats.CurrentMana).Append(" / ").Append(m_BoundStats.MaxMana).Append('\n');
            }
            sb.Append('\n');
            sb.Append("STR  ").Append(m_BoundStats.Strength).Append('\n');
            sb.Append("INT  ").Append(m_BoundStats.Intelligence).Append('\n');
            sb.Append("WIL  ").Append(m_BoundStats.Willpower).Append('\n');
            sb.Append("ARM  ").Append(m_BoundStats.Armor).Append('\n');
            sb.Append("DMG  ").Append(m_BoundStats.WeaponDamage).Append('\n');
            sb.Append('\n');
            sb.Append("Crit  ").Append(m_BoundStats.CritChance).Append('%').Append('\n');
            sb.Append("Dodge ").Append(m_BoundStats.DodgeChance).Append('%').Append('\n');
            sb.Append("Parry ").Append(m_BoundStats.ParryChance).Append('%').Append('\n');
            sb.Append("Block ").Append(m_BoundStats.BlockChance).Append('%').Append('\n');
            sb.Append('\n');
            sb.Append("Fire   ").Append(m_BoundStats.ResistFire).Append('\n');
            sb.Append("Frost  ").Append(m_BoundStats.ResistFrost).Append('\n');
            sb.Append("Arcane ").Append(m_BoundStats.ResistArcane).Append('\n');
            sb.Append("Nature ").Append(m_BoundStats.ResistNature).Append('\n');
            sb.Append("Shadow ").Append(m_BoundStats.ResistShadow);

            m_StatsLabel.text = sb.ToString();
        }

        // ---------------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------------

        private void OnToggleCharacter(InputAction.CallbackContext _)
        {
            ApplyVisibility(!m_Visible);
        }

        private void ApplyVisibility(bool visible)
        {
            m_Visible = visible;
            if (m_Panel != null)
            {
                m_Panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // Bei jedem Sichtbar-Werden Stats frischziehen, falls Werte still
            // (ohne Event) gewandert sind.
            if (visible)
            {
                RefreshStats();
            }
        }

        private void OnSlotClicked(EquipSlot slot, ClickEvent evt)
        {
            // Rechtsklick (button == 1) auf belegten Slot triggert Unequip.
            if (evt.button != 1)
            {
                return;
            }
            if (m_BoundEquipment == null)
            {
                return;
            }
            if (m_BoundEquipment.GetEquipped(slot) <= 0)
            {
                return;
            }
            m_BoundEquipment.RequestUnequipServerRpc(slot);
        }

        // ---------------------------------------------------------------------
        // Tooltip-Bruecke (Visual-Tree liegt in TooltipPanel, hier nur das
        // Slot-spezifische Befuellen aus PlayerEquipment + ItemCatalog).
        // ---------------------------------------------------------------------

        private void ShowSlotTooltip(EquipSlot slot)
        {
            m_HoveredSlot = slot;
            if (m_Tooltip == null || m_BoundEquipment == null)
            {
                return;
            }
            if (!m_SlotIcons.TryGetValue(slot, out VisualElement icon) || icon == null)
            {
                return;
            }
            int templateId = m_BoundEquipment.GetEquipped(slot);
            if (templateId <= 0)
            {
                m_Tooltip.Hide();
                return;
            }
            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate tpl) || tpl == null)
            {
                m_Tooltip.Hide();
                return;
            }
            // Anker = SlotRoot (parent des Icons), damit der Tooltip nicht im
            // 0-Rect der Icon-Child sondern auf der vollen Slot-Flaeche hockt.
            VisualElement slotRoot = icon.parent ?? icon;
            m_Tooltip.Show(
                TooltipPanel.GetItemDisplayName(tpl),
                TooltipPanel.BuildItemMeta(tpl),
                TooltipPanel.GetItemDescription(tpl),
                slotRoot.worldBound,
                TooltipPlacement.Above);
        }

        private void HideSlotTooltip(EquipSlot slot)
        {
            if (m_HoveredSlot == slot)
            {
                m_HoveredSlot = EquipSlot.None;
            }
            m_Tooltip?.Hide();
        }
    }
}
