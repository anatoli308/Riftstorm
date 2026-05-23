using System;
using Riftstorm.Game.Items;
using Riftstorm.Game.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI.Inventory
{
    /// <summary>
    /// UIToolkit-HUD fuer das Spieler-Inventar (49 Slots im 7x7-Grid).
    /// Visualisiert den replizierten <see cref="PlayerInventory"/>-State des
    /// lokalen Spielers, erlaubt Quick-Equip via Klick und Toggle via I-Taste.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle / Bindings spiegeln <see cref="ActionBarHUD"/>: das HUD lebt
    /// auf einem MonoBehaviour mit <c>UIDocument</c>; der lokale Spieler wird
    /// in <see cref="Update"/> nach-gebunden, sobald NGO ein <c>PlayerObject</c>
    /// geliefert hat.
    /// </para>
    /// <para>
    /// Tooltip und Drag&amp;Drop folgen in Phase 17B; aktuell ist ein Linksklick
    /// auf einen befuellten Slot ein Quick-Equip-Request via
    /// <see cref="PlayerEquipment.RequestEquipFromInventoryServerRpc"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryHUD : MonoBehaviour
    {
        // ---------------------------------------------------------------------
        // Konstanten / USS-Klassen
        // ---------------------------------------------------------------------

        private const string k_RootName = "inventory-hud-root";
        private const string k_PanelName = "inventory-panel";
        private const string k_GridName = "inventory-grid";
        private const string k_SlotClass = "inventory-slot";
        private const string k_SlotIconClass = "inventory-slot__icon";
        private const string k_SlotCountClass = "inventory-slot__count";
        private const string k_GoldName = "inventory-gold";

        // ---------------------------------------------------------------------
        // Felder
        // ---------------------------------------------------------------------

        private UIDocument m_Document;
        private InventoryConfig m_Config;

        private VisualElement m_Root;
        private VisualElement m_Panel;
        private VisualElement[] m_SlotRoots;
        private VisualElement[] m_SlotIcons;
        private Label[] m_SlotCounts;
        private Label m_GoldLabel;

        private InputAction m_ToggleAction;
        private bool m_Visible;

        private PlayerInventory m_BoundInventory;
        private PlayerEquipment m_BoundEquipment;

        /// <summary>Geteiltes Tooltip-Overlay; eine Instanz fuer alle 49 Slots.</summary>
        private TooltipPanel m_Tooltip;

        /// <summary>Index des aktuell gehoverten Slots oder -1.</summary>
        private int m_HoveredSlot = -1;

        // ---------------------------------------------------------------------
        // Unity-Lifecycle
        // ---------------------------------------------------------------------

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
            m_Config = InventoryConfigLoader.Load();
        }

        private void OnEnable()
        {
            BuildVisualTree();
            ApplyVisibility(false); // Inventar startet versteckt; Toggle via I-Taste.

            // Code-erzeugte InputAction (mirror PlayerInputController Spell-Slots).
            m_ToggleAction = new(name: "InventoryToggle", type: InputActionType.Button, binding: m_Config.toggleBinding);
            m_ToggleAction.performed += OnToggleInventory;
            m_ToggleAction.Enable();
        }

        private void OnDisable()
        {
            if (m_ToggleAction != null)
            {
                m_ToggleAction.performed -= OnToggleInventory;
                m_ToggleAction.Disable();
                m_ToggleAction.Dispose();
                m_ToggleAction = null;
            }

            UnbindLocalPlayer();

            m_Root = null;
            m_Panel = null;
            m_SlotRoots = null;
            m_SlotIcons = null;
            m_SlotCounts = null;
            m_GoldLabel = null;
            m_Tooltip = null;
            m_HoveredSlot = -1;
        }

        private void Update()
        {
            if (m_BoundInventory == null)
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
                Debug.LogWarning("[InventoryHUD] UIDocument hat kein rootVisualElement.");
                return;
            }

            // Fullscreen-Root als Anker (pickingMode = Ignore, damit darunterliegende
            // HUDs/Worldspace-Clicks nicht geblockt werden).
            m_Root = new() { name = k_RootName };
            m_Root.style.position = Position.Absolute;
            m_Root.style.left = 0;
            m_Root.style.right = 0;
            m_Root.style.top = 0;
            m_Root.style.bottom = 0;
            m_Root.pickingMode = PickingMode.Ignore;
            docRoot.Add(m_Root);

            // Panel
            m_Panel = new() { name = k_PanelName };
            m_Panel.style.position = Position.Absolute;
            m_Panel.style.right = m_Config.anchorRight;
            m_Panel.style.bottom = m_Config.anchorBottom;
            m_Panel.style.width = m_Config.panelWidth;
            m_Panel.style.height = m_Config.panelHeight;

            Texture2D bg = InventoryConfigLoader.LoadTextureOrNull(m_Config.backgroundTexture);
            if (bg != null)
            {
                m_Panel.style.backgroundImage = new StyleBackground(bg);
            }
            m_Root.Add(m_Panel);

            BuildGrid();
            BuildGoldLabel();

            // Tooltip-Overlay als letztes Kind, damit es per Z-Order ueber dem
            // Inventar-Panel liegt (gleiche Konvention wie ActionBarHUD).
            m_Tooltip = new(m_Root);
        }

        private void BuildGrid()
        {
            VisualElement grid = new() { name = k_GridName };
            grid.style.position = Position.Absolute;
            grid.style.left = m_Config.gridLeft;
            grid.style.top = m_Config.gridTop;
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            float totalWidth = (m_Config.slotSize * m_Config.columns) + (m_Config.slotSpacing * (m_Config.columns - 1));
            grid.style.width = totalWidth;
            m_Panel.Add(grid);

            int total = m_Config.columns * m_Config.rows;
            if (total > PlayerInventory.Capacity)
            {
                total = PlayerInventory.Capacity;
            }

            m_SlotRoots = new VisualElement[total];
            m_SlotIcons = new VisualElement[total];
            m_SlotCounts = new Label[total];

            Texture2D idleFrame = InventoryConfigLoader.LoadTextureOrNull(m_Config.slotIdleTexture);
            Texture2D hoverFrame = InventoryConfigLoader.LoadTextureOrNull(m_Config.slotHoverTexture);

            for (int i = 0; i < total; i++)
            {
                int slotIndex = i; // Local-Capture fuer Click-Callback.

                VisualElement slot = new() { name = $"inventory-slot-{i}" };
                slot.AddToClassList(k_SlotClass);
                slot.style.width = m_Config.slotSize;
                slot.style.height = m_Config.slotSize;

                // Spacing: rechts/unten, ausser letzte Spalte/Reihe.
                int col = i % m_Config.columns;
                int row = i / m_Config.columns;
                slot.style.marginRight = col == m_Config.columns - 1 ? 0 : m_Config.slotSpacing;
                slot.style.marginBottom = row == m_Config.rows - 1 ? 0 : m_Config.slotSpacing;

                if (idleFrame != null)
                {
                    slot.style.backgroundImage = new StyleBackground(idleFrame);
                }

                // Icon-Child (Background fuer die Item-Textur).
                VisualElement icon = new();
                icon.AddToClassList(k_SlotIconClass);
                icon.style.position = Position.Absolute;
                icon.style.left = 0;
                icon.style.right = 0;
                icon.style.top = 0;
                icon.style.bottom = 0;
                icon.pickingMode = PickingMode.Ignore;
                slot.Add(icon);

                // Count-Label (rechts-unten).
                Label count = new();
                count.AddToClassList(k_SlotCountClass);
                count.style.position = Position.Absolute;
                count.style.right = 2;
                count.style.bottom = 2;
                count.style.fontSize = m_Config.countFontSize;
                count.style.color = Color.white;
                count.style.unityTextAlign = TextAnchor.MiddleRight;
                count.pickingMode = PickingMode.Ignore;
                count.style.display = DisplayStyle.None;
                slot.Add(count);

                // Hover-Frame: Swap background on Pointer Enter/Leave (no-op wenn kein Hover-Asset).
                if (hoverFrame != null && idleFrame != null)
                {
                    slot.RegisterCallback<PointerEnterEvent>(_ => slot.style.backgroundImage = new StyleBackground(hoverFrame));
                    slot.RegisterCallback<PointerLeaveEvent>(_ => slot.style.backgroundImage = new StyleBackground(idleFrame));
                }

                // Linksklick = Quick-Equip.
                slot.RegisterCallback<ClickEvent>(evt => OnSlotClicked(slotIndex, evt));

                // Tooltip-Hover: Item-Details on Enter, Hide on Leave. Lambdas
                // capturen slotIndex (Local-Capture oben), nicht den Item-State.
                slot.RegisterCallback<PointerEnterEvent>(_ => ShowSlotTooltip(slotIndex));
                slot.RegisterCallback<PointerLeaveEvent>(_ => HideSlotTooltip(slotIndex));

                grid.Add(slot);

                m_SlotRoots[i] = slot;
                m_SlotIcons[i] = icon;
                m_SlotCounts[i] = count;
            }
        }

        private void BuildGoldLabel()
        {
            m_GoldLabel = new() { name = k_GoldName };
            m_GoldLabel.style.position = Position.Absolute;
            m_GoldLabel.style.left = m_Config.goldLeft;
            m_GoldLabel.style.bottom = m_Config.goldBottom;
            m_GoldLabel.style.fontSize = m_Config.goldFontSize;
            m_GoldLabel.style.color = new StyleColor(new Color(1f, 0.84f, 0f, 1f)); // Gold
            m_GoldLabel.text = "0"; // Placeholder, PlayerWallet folgt spaeter.
            m_Panel.Add(m_GoldLabel);
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

            PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>()
                ?? playerObj.GetComponentInChildren<PlayerInventory>();
            PlayerEquipment equipment = playerObj.GetComponent<PlayerEquipment>()
                ?? playerObj.GetComponentInChildren<PlayerEquipment>();
            if (inventory == null || equipment == null)
            {
                return;
            }

            m_BoundInventory = inventory;
            m_BoundEquipment = equipment;
            m_BoundInventory.SlotChanged += OnSlotChanged;

            // Initiale Belegung uebernehmen (replizierter State ist hier bereits da).
            int snapshot = Mathf.Min(m_SlotRoots?.Length ?? 0, m_BoundInventory.Count);
            for (int i = 0; i < snapshot; i++)
            {
                RenderSlot(i, m_BoundInventory.GetSlot(i));
            }
        }

        private void UnbindLocalPlayer()
        {
            if (m_BoundInventory != null)
            {
                m_BoundInventory.SlotChanged -= OnSlotChanged;
                m_BoundInventory = null;
            }
            m_BoundEquipment = null;
        }

        // ---------------------------------------------------------------------
        // Render
        // ---------------------------------------------------------------------

        private void OnSlotChanged(int index, InventoryItem item) => RenderSlot(index, item);

        private void RenderSlot(int index, InventoryItem item)
        {
            if (m_SlotIcons == null || index < 0 || index >= m_SlotIcons.Length)
            {
                return;
            }

            VisualElement icon = m_SlotIcons[index];
            Label count = m_SlotCounts[index];

            if (item.IsEmpty)
            {
                icon.style.backgroundImage = new StyleBackground((Texture2D)null);
                count.style.display = DisplayStyle.None;
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(item.TemplateId, out ItemTemplate tpl))
            {
                Debug.LogWarning($"[InventoryHUD] Kein Template fuer Entry {item.TemplateId} (Slot {index}).");
                icon.style.backgroundImage = new StyleBackground((Texture2D)null);
                count.style.display = DisplayStyle.None;
                return;
            }

            Texture2D iconTex = InventoryConfigLoader.LoadItemIconOrNull(tpl.Icon, m_Config.itemIconKeyPrefix);
            icon.style.backgroundImage = iconTex != null ? new StyleBackground(iconTex) : new StyleBackground((Texture2D)null);

            if (tpl.IsStackable && item.Count > 1)
            {
                count.text = item.Count.ToString();
                count.style.display = DisplayStyle.Flex;
            }
            else
            {
                count.style.display = DisplayStyle.None;
            }
        }

        // ---------------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------------

        private void OnToggleInventory(InputAction.CallbackContext _)
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
        }

        private void OnSlotClicked(int slotIndex, ClickEvent evt)
        {
            // Nur Linksklick triggert Quick-Equip; Drag&Drop folgt in Phase 17B.
            if (evt.button != 0)
            {
                return;
            }
            if (m_BoundInventory == null || m_BoundEquipment == null)
            {
                return;
            }

            InventoryItem slot = m_BoundInventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
            {
                return;
            }

            m_BoundEquipment.RequestEquipFromInventoryServerRpc(slotIndex);
        }

        // ---------------------------------------------------------------------
        // Tooltip-Bruecke (Visual-Tree liegt in TooltipPanel, hier nur das
        // Slot-spezifische Befuellen aus PlayerInventory + ItemCatalog).
        // ---------------------------------------------------------------------

        private void ShowSlotTooltip(int slotIndex)
        {
            m_HoveredSlot = slotIndex;
            if (m_Tooltip == null || m_BoundInventory == null)
            {
                return;
            }
            if (slotIndex < 0 || slotIndex >= (m_SlotRoots?.Length ?? 0))
            {
                return;
            }
            InventoryItem item = m_BoundInventory.GetSlot(slotIndex);
            if (item.IsEmpty)
            {
                m_Tooltip.Hide();
                return;
            }
            if (!ItemCatalogLoader.TryGetTemplate(item.TemplateId, out ItemTemplate tpl) || tpl == null)
            {
                m_Tooltip.Hide();
                return;
            }
            m_Tooltip.Show(
                TooltipPanel.GetItemDisplayName(tpl),
                TooltipPanel.BuildItemMeta(tpl),
                TooltipPanel.GetItemDescription(tpl, item.Count),
                m_SlotRoots[slotIndex].worldBound,
                TooltipPlacement.Above);
        }

        private void HideSlotTooltip(int slotIndex)
        {
            if (m_HoveredSlot == slotIndex)
            {
                m_HoveredSlot = -1;
            }
            m_Tooltip?.Hide();
        }
    }
}
