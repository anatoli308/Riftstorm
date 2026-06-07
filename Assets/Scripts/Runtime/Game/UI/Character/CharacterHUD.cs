using System.Collections.Generic;
using System.Text;
using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
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
    /// Bindings spiegeln <see cref="Inventory.InventoryHUD"/>:
    /// das HUD lebt auf einem MonoBehaviour mit <c>UIDocument</c>; lokaler
    /// Spieler wird in <see cref="Update"/> nach-gebunden, sobald NGO ein
    /// <c>PlayerObject</c> geliefert hat. Linksklick auf belegten Slot
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

        /// <summary>Aktuelle Waffen-/Offhand-Quelle. Wird für die DMG-Anzeige
        /// (effektiver Melee-Schaden = <c>weapon.BaseDamage + WeaponDamage + STR/2</c>)
        /// und für automatisches Refresh bei <see cref="PlayerCombat.WeaponChanged"/>
        /// gebunden.</summary>
        private PlayerCombat m_BoundCombat;

        /// <summary>Verhindert Log-Spam in <see cref="TryBindLocalPlayer"/>: nur
        /// ein Diagnose-Eintrag pro PlayerObject-Instanz, sobald ein Bind-Versuch
        /// scheitert (typisch fehlende <see cref="UnitStats"/> am Prefab).</summary>
        private int m_LastDiagPlayerInstanceId;

        /// <summary>Geteiltes Tooltip-Overlay; eine Instanz fuer alle 12 Equip-Slots.</summary>
        private TooltipPanel m_Tooltip;

        /// <summary>Aktuell gehoverter Slot oder <see cref="EquipSlot.None"/>.</summary>
        private EquipSlot m_HoveredSlot = EquipSlot.None;

        // ---------------------------------------------------------------------
        // Character Preview (Paper-Doll via RenderTexture)
        // ---------------------------------------------------------------------

        /// <summary>UI-Element, das die Preview-RenderTexture im Panel anzeigt.</summary>
        private VisualElement m_PreviewElement;

        /// <summary>Off-Screen-Kamera, die den Spieler in <see cref="m_PreviewRT"/> rendert.</summary>
        private Camera m_PreviewCam;

        /// <summary>GameObject-Wrapper fuer <see cref="m_PreviewCam"/>, parent zum Spieler.</summary>
        private GameObject m_PreviewCamGO;

        /// <summary>RenderTexture, die als Background-Image des Preview-Elements dient.</summary>
        private RenderTexture m_PreviewRT;

        /// <summary>Aktuell verfolgter Spieler-Transform (parent der Preview-Cam).</summary>
        private Transform m_PreviewTarget;

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
            TeardownPreview();

            m_Root = null;
            m_Panel = null;
            m_SlotIcons.Clear();
            m_StatsLabel = null;
            m_Tooltip = null;
            m_HoveredSlot = EquipSlot.None;
        }

        private void TeardownPreview()
        {
            if (m_PreviewCam != null)
            {
                m_PreviewCam.targetTexture = null;
            }
            if (m_PreviewCamGO != null)
            {
                // DestroyImmediate nur im Edit-Mode noetig; Runtime nutzt Destroy.
                if (Application.isPlaying)
                {
                    Destroy(m_PreviewCamGO);
                }
                else
                {
                    DestroyImmediate(m_PreviewCamGO);
                }
                m_PreviewCamGO = null;
                m_PreviewCam = null;
            }
            if (m_PreviewRT != null)
            {
                m_PreviewRT.Release();
                if (Application.isPlaying)
                {
                    Destroy(m_PreviewRT);
                }
                else
                {
                    DestroyImmediate(m_PreviewRT);
                }
                m_PreviewRT = null;
            }
            m_PreviewElement = null;
            m_PreviewTarget = null;
        }

        private void Update()
        {
            if (m_BoundEquipment == null)
            {
                TryBindLocalPlayer();
            }
            else if (m_BoundStats == null)
            {
                // Equipment/Inventory waren beim ersten Bind da, UnitStats noch
                // nicht (z. B. NetworkBehaviour spawned spaeter). Stats nachziehen,
                // ohne den Equipment-Bind anzufassen.
                TryBindStats();
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
            BuildPreview();
            BuildStatsLabel();

            // Tooltip-Overlay als letztes Kind, damit es per Z-Order ueber
            // Slots + Stats-Label liegt (gleiche Konvention wie ActionBarHUD).
            m_Tooltip = new(m_Root);
        }

        private void BuildPreview()
        {
            if (!m_Config.previewEnabled || m_Config.previewWidth <= 0 || m_Config.previewHeight <= 0)
            {
                return;
            }

            m_PreviewElement = new() { name = "character-preview" };
            m_PreviewElement.style.position = Position.Absolute;
            m_PreviewElement.style.left = m_Config.previewLeft;
            m_PreviewElement.style.top = m_Config.previewTop;
            m_PreviewElement.style.width = m_Config.previewWidth;
            m_PreviewElement.style.height = m_Config.previewHeight;
            m_PreviewElement.pickingMode = PickingMode.Ignore;
            m_Panel.Add(m_PreviewElement);

            int size = Mathf.Max(64, m_Config.previewTextureSize);
            m_PreviewRT = new(size, size, depth: 24, RenderTextureFormat.ARGB32)
            {
                name = "CharacterPreviewRT",
                antiAliasing = 2,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            m_PreviewRT.Create();
            m_PreviewElement.style.backgroundImage = Background.FromRenderTexture(m_PreviewRT);
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
            // Rich-Text fuer die WoW-artige Stat-Faerbung (rot/gruen via
            // <color=#RRGGBB>) — Default ist true, hier explizit zur Absicherung.
            m_StatsLabel.enableRichText = true;
            m_StatsLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            // Picking aktiviert, damit der UIToolkit-Tooltip mit der
            // DMG-Aufschluesselung (siehe AppendMeleeDamageLine) auf Hover
            // erscheint.
            m_StatsLabel.pickingMode = PickingMode.Position;
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
                m_BoundStats.ClientAurasChanged += OnAurasChanged;
            }

            // PlayerCombat bindet die aktuell ausgerüstete Waffe; über WeaponChanged
            // / OffhandChanged wird die DMG-Zeile bei jedem Waffenwechsel
            // (Console /weapon, Equip-Server-RPC, Loadout) neu gerendert.
            m_BoundCombat = playerObj.GetComponent<PlayerCombat>()
                ?? playerObj.GetComponentInChildren<PlayerCombat>();
            if (m_BoundCombat != null)
            {
                m_BoundCombat.WeaponChanged += OnWeaponOrOffhandChanged;
                m_BoundCombat.OffhandChanged += OnWeaponOrOffhandChanged;
                m_BoundCombat.RangedChanged += OnWeaponOrOffhandChanged;
            }

            // Preview-Kamera an den Spieler haengen, sobald der Player-Transform
            // verfuegbar ist; muss VOR ApplyVisibility(true) erfolgen, sonst
            // bleibt das Render-Target schwarz beim ersten Toggle.
            BindPreviewTarget(playerObj.transform);

            // Initiale Belegung: alle 12 Slots durchrendern + Stats-Snapshot.
            foreach (KeyValuePair<EquipSlot, VisualElement> pair in m_SlotIcons)
            {
                RenderSlot(pair.Key, m_BoundEquipment.GetEquipped(pair.Key));
            }
            RefreshStats();
        }

        /// <summary>
        /// Erzeugt die Preview-Kamera lazy und parented sie an den Spieler.
        /// Kamera bleibt deaktiviert solange das Panel unsichtbar ist, damit
        /// kein Rendern pro Frame anfaellt.
        /// </summary>
        private void BindPreviewTarget(Transform target)
        {
            if (target == null || m_PreviewRT == null || !m_Config.previewEnabled)
            {
                return;
            }
            m_PreviewTarget = target;

            if (m_PreviewCamGO == null)
            {
                m_PreviewCamGO = new GameObject("CharacterPreviewCam");
                m_PreviewCam = m_PreviewCamGO.AddComponent<Camera>();
                m_PreviewCam.orthographic = true;
                m_PreviewCam.orthographicSize = Mathf.Max(0.1f, m_Config.previewOrthoSize);
                m_PreviewCam.clearFlags = CameraClearFlags.SolidColor;
                m_PreviewCam.backgroundColor = new Color(
                    m_Config.previewBackgroundR,
                    m_Config.previewBackgroundG,
                    m_Config.previewBackgroundB,
                    m_Config.previewBackgroundA);
                m_PreviewCam.cullingMask = ~0;
                m_PreviewCam.targetTexture = m_PreviewRT;
                m_PreviewCam.nearClipPlane = 0.05f;
                m_PreviewCam.farClipPlane = 50f;
                m_PreviewCam.enabled = m_Visible;
            }

            // Cam an den Spieler haengen, damit sie automatisch mitlaeuft.
            // Local-Offset + Pitch konfigurierbar via JSON.
            m_PreviewCamGO.transform.SetParent(target, worldPositionStays: false);
            m_PreviewCamGO.transform.localPosition = new Vector3(
                m_Config.previewCameraOffsetX,
                m_Config.previewCameraOffsetY,
                m_Config.previewCameraOffsetZ);
            m_PreviewCamGO.transform.localRotation = Quaternion.Euler(m_Config.previewCameraPitchDeg, 0f, 0f);
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
                m_BoundStats.ClientAurasChanged -= OnAurasChanged;
                m_BoundStats = null;
            }
            if (m_BoundCombat != null)
            {
                m_BoundCombat.WeaponChanged -= OnWeaponOrOffhandChanged;
                m_BoundCombat.OffhandChanged -= OnWeaponOrOffhandChanged;
                m_BoundCombat.RangedChanged -= OnWeaponOrOffhandChanged;
                m_BoundCombat = null;
            }
            m_BoundInventory = null;
        }

        /// <summary>Late-Bind fuer <see cref="UnitStats"/> falls die Komponente
        /// erst nach Equipment/Inventory verfuegbar wird (z. B. NetworkBehaviour
        /// spawnt verzoegert oder sitzt auf einem Child-GameObject das spaeter
        /// aktiviert wird).</summary>
        private void TryBindStats()
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
            UnitStats stats = playerObj.GetComponent<UnitStats>()
                ?? playerObj.GetComponentInChildren<UnitStats>();
            if (stats == null)
            {
                return;
            }

            m_BoundStats = stats;
            m_BoundStats.HpChanged += OnHpOrManaChanged;
            m_BoundStats.ManaChanged += OnHpOrManaChanged;
            m_BoundStats.ClientAurasChanged += OnAurasChanged;
            RefreshStats();
            Debug.Log($"[CharacterHUD] UnitStats late-bound on '{playerObj.name}'.");
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

        /// <summary>Refresh-Trigger sobald die replizierten Auren des lokalen
        /// Spielers wechseln (Buff/Debuff startet, stackt oder laeuft ab). Event-
        /// getrieben \u2014 kein Polling \u2014 damit die WoW-artige Stat-Faerbung live
        /// mitlaeuft, solange ein Debuff wie Infected Wound aktiv ist.</summary>
        private void OnAurasChanged() => RefreshStats();

        /// <summary>Refresh-Trigger fuer den DMG-Block bei Waffen-/Offhand-Wechsel.
        /// Payload (oldId/newId) wird hier nicht gebraucht — die Zeile liest die
        /// aktuelle <see cref="PlayerCombat.CurrentWeapon"/>-Definition direkt
        /// und addiert <c>WeaponDamage</c> + <c>STR/2</c> aus den gebundenen Stats.</summary>
        private void OnWeaponOrOffhandChanged(string previous, string current) => RefreshStats();

        /// <summary>
        /// Rendert die DMG-Zeile als effektiven Melee-Grundschaden
        /// (<c>weapon.BaseDamage + WeaponDamage-Stat + STR/2</c>), so dass der
        /// Spieler sieht, was die ausger\u00fcstete Waffe tats\u00e4chlich pro Swing
        /// austeilt. Ohne gebundenen <see cref=\"PlayerCombat\"/> bzw. ohne
        /// geladenen <see cref=\"WeaponCatalog\"/> faellt die Zeile auf den reinen
        /// <c>WeaponDamage</c>-Stat zurueck (entspricht der alten Anzeige).
        /// </summary>
        private void AppendMeleeDamageLine(StringBuilder sb)
        {
            int weaponValueStat = m_BoundStats.WeaponDamage;
            WeaponDefinition weapon = m_BoundCombat != null ? m_BoundCombat.CurrentWeapon : null;
            if (weapon == null)
            {
                sb.Append("DMG  ").Append(weaponValueStat).Append('\n');
                return;
            }
            int weaponBase = weapon.BaseDamage;
            int strBonus = m_BoundStats.Strength / 2;
            int effective = weaponBase + weaponValueStat + strBonus;
            // Kompakte Zeile fuer das Stats-Panel — die Aufschluesselung
            // (Wpn / Stat / STR/2) wandert in den Tooltip des Stats-Labels,
            // damit das Panel ruhig bleibt.
            sb.Append("DMG  ").Append(effective).Append('\n');
            if (m_StatsLabel != null)
            {
                m_StatsLabel.tooltip =
                    $"DMG {effective} = Wpn {weaponBase} + Stat {weaponValueStat} + STR/2 {strBonus}";
            }
        }

        /// <summary>
        /// Spiegelt <see cref="AppendMeleeDamageLine"/> fuer die Ranged-Waffe:
        /// effektiver Grundschaden = <c>ranged.BaseDamage + RangedWeaponDamage-Stat</c>.
        /// Anders als Melee gibt es <b>keinen Unarmed-Fallback</b> — ohne Bogen im
        /// Ranged-Slot zeigt die Zeile bewusst <c>"-"</c>, damit das Panel
        /// sichtbar macht, dass Ranged-Spells (Aimed Shot, Multi-Shot, ...)
        /// gerade durch <see cref="Spells.Runtime.SpellCaster"/>
        /// mit <c>NoRangedWeapon</c> geblockt werden.
        /// </summary>
        private void AppendRangedDamageLine(StringBuilder sb)
        {
            WeaponDefinition ranged = m_BoundCombat != null ? m_BoundCombat.CurrentRangedWeapon : null;
            if (ranged == null)
            {
                sb.Append("Rng  -\n");
                return;
            }
            int rangedBase = ranged.BaseDamage;
            int rangedStat = m_BoundStats.RangedWeaponDamage;
            int effective = rangedBase + rangedStat;
            sb.Append("Rng  ").Append(effective).Append('\n');
        }

        /// <summary>
        /// Zeigt effektive Ranged-Crit-% an — analog zur Rng-Damage-Zeile mit
        /// "-" wenn keine Ranged-Waffe equipped ist, weil
        /// <see cref="Spells.Runtime.SpellCaster"/> Ranged-Spells
        /// dann ohnehin via <c>NoRangedWeapon</c> blockt.
        /// Effektiver Wert: <c>BASE_CRIT(5) + RangedCritChance</c> (RangedCritChance
        /// liefert bereits Rating + <c>AGI/53</c>), Cap 0..95 wie in CombatFormulas.
        /// </summary>
        private void AppendRangedCritLine(StringBuilder sb)
        {
            WeaponDefinition ranged = m_BoundCombat != null ? m_BoundCombat.CurrentRangedWeapon : null;
            if (ranged == null)
            {
                sb.Append("Ranged Crit -\n");
                return;
            }
            int rangedCritEff = Mathf.Clamp(
                CombatFormulas.BaseCritChance + m_BoundStats.RangedCritChance,
                0,
                95);
            sb.Append("Ranged Crit ").Append(rangedCritEff).Append('%').Append('\n');
        }

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
                    "Level -\nHP   -\n\n"
                    + "STR  -\nAGI  -\nINT  -\nWIL  -\nFRT  -\nCRG  -\n\n"
                    + "ARM  -\nDMG  -\nRng  -\nSpell -\nHeal  -\n\n"
                    + "Melee Crit  -\nRanged Crit -\nSpell Crit  -\nHeal Crit   -\n"
                    + "Dodge -\nParry -\nBlock -\n\n"
                    + "HP Regen -\nMP Regen -\n\n"
                    + "Fire   -\nFrost  -\nShadow -\nHoly   -";
                return;
            }

            StringBuilder sb = new(512);
            sb.Append("Level ").Append(m_BoundStats.Level).Append('\n');
            sb.Append("HP   ").Append(m_BoundStats.CurrentHp).Append(" / ").Append(m_BoundStats.MaxHp).Append('\n');
            if (m_BoundStats.HasMana)
            {
                sb.Append("MP   ").Append(m_BoundStats.CurrentMana).Append(" / ").Append(m_BoundStats.MaxMana).Append('\n');
            }
            sb.Append('\n');

            // Primary Attributes (Original-Stat-Enum-Reihenfolge: STR=4, AGI=5, WIL=6, INT=7;
            // FRT/CRG sind die Riftstorm-Erweiterung). AGI skaliert Ranged-Damage,
            // Ranged-Crit und Dodge — Details siehe UnitStats.Agility / CombatFormulas.
            sb.Append("STR  "); AppendModifiedStat(sb, StatId.Strength); sb.Append('\n');
            sb.Append("AGI  "); AppendModifiedStat(sb, StatId.Agility); sb.Append('\n');
            sb.Append("INT  "); AppendModifiedStat(sb, StatId.Intelligence); sb.Append('\n');
            sb.Append("WIL  "); AppendModifiedStat(sb, StatId.Willpower); sb.Append('\n');
            sb.Append("FRT  ").Append(m_BoundStats.Fortitude).Append('\n');
            sb.Append("CRG  ").Append(m_BoundStats.Courage).Append('\n');
            sb.Append('\n');

            // Combat Stats: Armor + Waffenschaden (Melee + Ranged getrennt wie im Original).
            sb.Append("ARM  ").Append(m_BoundStats.Armor).Append('\n');
            AppendMeleeDamageLine(sb);
            AppendRangedDamageLine(sb);

            // Spell / Heal — Magic-Power-Wert, der direkt aus den Primary
            // Attributes abgeleitet wird und im Spell-Executor flat auf den
            // jeweiligen Effekt-Wert addiert wird (Scorch effectValue=4 +
            // SpellPower => sichtbarer Schaden). 1:1-Mapping macht die Skalierung
            // sofort lesbar: INT=5 -> Spell 5, WIL=5 -> Heal 5. Heal nimmt INT
            // zusaetzlich anteilig mit (halbe Wirkung), weil Heilung im
            // FLARE-Modell aus WIL primaer und INT sekundaer skaliert.
            int spellPower = m_BoundStats.Intelligence;
            int healPower = m_BoundStats.Willpower + (m_BoundStats.Intelligence / 2);
            sb.Append("Spell ").Append(spellPower).Append('\n');
            sb.Append("Heal  ").Append(healPower).Append('\n');
            sb.Append('\n');

            // Crit getrennt nach Schule (MeleeCritical / RangedCritical / SpellCritical),
            // Avoidance bleibt vereint da das Original nur Rating-Werte hatte.
            // Anzeige als *effektive* Roll-% gegen einen Gleich-Level-Gegner
            // (levelDiff=0): inklusive Source-Basen (BASE_CRIT=5, BASE_DODGE=5)
            // und gleichen Caps wie in CombatFormulas (Crit 0..95, Avoidance 0..75).
            // Damit liest das HUD denselben Wert ab, der in RollMeleeHit/Spell
            // tatsaechlich gewuerfelt wird — keine Asymmetrie mehr zwischen
            // Dodge/Crit (Rating-only) und Parry/Block (bereits aggregiert).
            int meleeCritEff = Mathf.Clamp(CombatFormulas.BaseCritChance + m_BoundStats.MeleeCritChance, 0, 95);
            int spellCritEff = Mathf.Clamp(CombatFormulas.BaseCritChance + m_BoundStats.SpellCritChance, 0, 95);
            int healCritEff = Mathf.Clamp(
                CombatFormulas.BaseCritChance + m_BoundStats.SpellCritChance + (m_BoundStats.Willpower / 40),
                0,
                95);
            int dodgeEff = Mathf.Clamp(
                CombatFormulas.BaseDodgeChance + (m_BoundStats.Agility / 20) + m_BoundStats.DodgeChance,
                0,
                CombatFormulas.MaxAvoidanceChance);

            sb.Append("Melee Crit  ").Append(meleeCritEff).Append('%').Append('\n');
            AppendRangedCritLine(sb);
            sb.Append("Spell Crit  ").Append(spellCritEff).Append('%').Append('\n');
            sb.Append("Heal Crit   ").Append(healCritEff).Append('%').Append('\n');
            sb.Append("Dodge ").Append(dodgeEff).Append('%').Append('\n');
            sb.Append("Parry ").Append(m_BoundStats.ParryChance).Append('%').Append('\n');
            sb.Append("Block ").Append(m_BoundStats.BlockChance).Append('%').Append('\n');
            sb.Append('\n');

            // Regeneration (Original "Regeneration") + Meditate (Mana-Regen).
            sb.Append("HP Regen ").Append(m_BoundStats.HpRegen).Append('\n');
            sb.Append("MP Regen ").Append(m_BoundStats.ManaRegen).Append('\n');
            sb.Append('\n');

            // Resistenzen — original-treu nur Fire/Frost/Shadow/Holy (kein Arcane/Nature im
            // Original-Stat-Enum, siehe UnitDefines.h).
            sb.Append("Fire   ").Append(m_BoundStats.ResistFire).Append('\n');
            sb.Append("Frost  ").Append(m_BoundStats.ResistFrost).Append('\n');
            sb.Append("Shadow ").Append(m_BoundStats.ResistShadow).Append('\n');
            sb.Append("Holy   ").Append(m_BoundStats.ResistHoly);

            // Kampf-Modifikatoren aus Auren — nur einblenden wenn aktiv, damit
            // der Block bei Default-Stats das Panel nicht mit Nullen flutet.
            int dmgDealt = m_BoundStats.DamageDealtPctMod;
            int dmgRecv = m_BoundStats.DamageReceivedPctMod;
            int healDealt = m_BoundStats.HealingDealtPctMod;
            int healRecv = m_BoundStats.HealingReceivedPctMod;
            if (dmgDealt != 0 || dmgRecv != 0 || healDealt != 0 || healRecv != 0)
            {
                sb.Append('\n').Append('\n');
                if (dmgDealt != 0)  sb.Append("Dmg+   ").Append(dmgDealt).Append('%').Append('\n');
                if (dmgRecv != 0)   sb.Append("DmgRcv ").Append(dmgRecv).Append('%').Append('\n');
                if (healDealt != 0) sb.Append("Heal+  ").Append(healDealt).Append('%').Append('\n');
                if (healRecv != 0)  sb.Append("HealRc ").Append(healRecv).Append('%');
            }

            m_StatsLabel.text = sb.ToString();
        }

        /// <summary>
        /// Haengt einen Primaerattribut-Wert WoW-artig eingefaerbt an den
        /// StringBuilder: rot (<c>#FF5050</c>) wenn ein Debuff den Wert senkt
        /// (z. B. Infected Wound minus 2 %), gruen (<c>#50FF50</c>) wenn ein
        /// Selbst-Buff ihn anhebt, sonst neutral-weiss. Bei Abweichung wird das
        /// vorzeichenbehaftete Delta in Klammern angezeigt (z. B.
        /// <c>98 (-2)</c>), damit die Skalierung detailliert ablesbar ist. Nutzt
        /// das Rich-Text-Markup des Labels (<see cref="Label.enableRichText"/>).
        /// </summary>
        /// <param name="sb">Ziel-StringBuilder der Stats-Zeile.</param>
        /// <param name="stat">Anzuzeigendes Primaerattribut.</param>
        private void AppendModifiedStat(StringBuilder sb, StatId stat)
        {
            m_BoundStats.GetDisplayStat(stat, out int baseValue, out int effective);
            if (effective == baseValue)
            {
                sb.Append(effective);
                return;
            }
            int delta = effective - baseValue;
            string hex = delta < 0 ? "#FF5050" : "#50FF50";
            sb.Append("<color=").Append(hex).Append('>').Append(effective).Append(" (");
            if (delta > 0)
            {
                sb.Append('+');
            }
            sb.Append(delta).Append(')').Append("</color>");
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
            // Preview-Kamera nur rendern wenn sichtbar — spart GPU-Last.
            if (m_PreviewCam != null)
            {
                m_PreviewCam.enabled = visible;
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
            // Linksklick (button == 0) auf belegten Slot triggert Unequip —
            // symmetrisch zum LMB-Equip im InventoryHUD.
            if (evt.button != 0)
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
