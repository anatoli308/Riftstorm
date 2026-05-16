using System;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// WoW-Style Portrait/Target-Frame als UIToolkit-View. Reagiert
    /// ausschliesslich auf das gelockte Server-Target
    /// (<see cref="TargetSelection.CurrentTargetIdChanged"/>) und
    /// aktualisiert HP/Mana/Name event-getrieben ueber
    /// <see cref="UnitStats.HpChanged"/> / <see cref="UnitStats.ManaChanged"/> /
    /// <see cref="PlayerIdentity.DisplayNameChanged"/>. Kein Polling, kein Update-Tick.
    /// <para>
    /// Das Frame baut seinen Visual-Tree komplett programmatisch \u2014 es muss
    /// kein UXML-Asset zugewiesen werden. Es reicht ein GameObject mit
    /// <see cref="UIDocument"/> (PanelSettings gesetzt, SourceAsset leer) plus
    /// dieser Komponente in der Game-Scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TargetFrameUI : MonoBehaviour
    {
        private UIDocument m_Document;

        // Visual-Tree
        private VisualElement m_Root;
        private VisualElement m_Frame;
        private Label m_NameLabel;
        private VisualElement m_HpFill;
        private Label m_HpValueLabel;
        private VisualElement m_ManaRow;
        private VisualElement m_ManaFill;
        private Label m_ManaValueLabel;

        // Lokaler Spieler
        private TargetSelection m_LocalSelection;

        // Aktuelles Ziel-Abo
        private UnitStats m_BoundStats;
        private PlayerIdentity m_BoundIdentity;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildVisualTree();
            HideFrame();
        }

        private void OnDisable()
        {
            DetachFromLocalSelection();
            DetachFromTarget();
        }

        private void Update()
        {
            // Wir versuchen einmal pro Frame, den lokalen Spieler zu binden,
            // bis es klappt. Anschliessend ist das ein reines Event-System
            // (kein Polling von Werten).
            if (m_LocalSelection != null)
            {
                return;
            }
            TryBindLocalSelection();
        }

        // -------------------------------------------------------------------------
        // Visual-Tree
        // -------------------------------------------------------------------------

        private void BuildVisualTree()
        {
            m_Root = m_Document.rootVisualElement;
            if (m_Root == null)
            {
                return;
            }
            m_Root.Clear();
            m_Root.pickingMode = PickingMode.Ignore;

            m_Frame = new VisualElement { name = "target-frame" };
            ApplyFrameStyle(m_Frame);

            // Header: Portrait-Box + Name
            VisualElement header = new() { name = "target-frame-header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4f;

            VisualElement portrait = new() { name = "target-frame-portrait" };
            portrait.style.width = 48f;
            portrait.style.height = 48f;
            portrait.style.marginRight = 8f;
            portrait.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.10f, 1f));
            ApplyBorder(portrait, new Color(0.78f, 0.65f, 0.20f, 1f), 2f);
            header.Add(portrait);

            m_NameLabel = new Label("\u2014") { name = "target-frame-name" };
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = 16f;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_NameLabel.style.flexGrow = 1f;
            header.Add(m_NameLabel);

            m_Frame.Add(header);

            // HP-Reihe
            VisualElement hpRow = BuildBarRow(
                "target-frame-hp",
                new Color(0.65f, 0.10f, 0.10f, 1f),
                out m_HpFill,
                out m_HpValueLabel);
            m_Frame.Add(hpRow);

            // Mana-Reihe (kann ausgeblendet werden)
            m_ManaRow = BuildBarRow(
                "target-frame-mana",
                new Color(0.15f, 0.40f, 0.85f, 1f),
                out m_ManaFill,
                out m_ManaValueLabel);
            m_ManaRow.style.marginTop = 4f;
            m_Frame.Add(m_ManaRow);

            m_Root.Add(m_Frame);
        }

        private static void ApplyFrameStyle(VisualElement frame)
        {
            frame.style.position = Position.Absolute;
            frame.style.top = 16f;
            // Direkt rechts neben dem Player-Frame (links 16 + Breite ~ 300 + Luecke).
            frame.style.left = 332f;
            frame.style.minWidth = 280f;
            frame.style.paddingTop = 8f;
            frame.style.paddingBottom = 8f;
            frame.style.paddingLeft = 10f;
            frame.style.paddingRight = 10f;
            frame.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.06f, 0.82f));
            ApplyBorder(frame, new Color(0.78f, 0.65f, 0.20f, 1f), 2f);
        }

        private static void ApplyBorder(VisualElement el, Color color, float width)
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

        private static VisualElement BuildBarRow(string baseName, Color fillColor, out VisualElement fill, out Label valueLabel)
        {
            VisualElement row = new() { name = baseName };
            row.style.height = 18f;
            row.style.backgroundColor = new StyleColor(new Color(0.10f, 0.10f, 0.12f, 1f));
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

        private void ShowFrame()
        {
            if (m_Frame != null)
            {
                m_Frame.style.display = DisplayStyle.Flex;
            }
        }

        private void HideFrame()
        {
            if (m_Frame != null)
            {
                m_Frame.style.display = DisplayStyle.None;
            }
        }

        // -------------------------------------------------------------------------
        // Lokaler Spieler -> TargetSelection
        // -------------------------------------------------------------------------

        private void TryBindLocalSelection()
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
            TargetSelection sel = playerObj.GetComponent<TargetSelection>();
            if (sel == null)
            {
                sel = playerObj.GetComponentInChildren<TargetSelection>();
            }
            if (sel == null)
            {
                return;
            }

            m_LocalSelection = sel;
            m_LocalSelection.CurrentTargetIdChanged += OnLocalLockChanged;
            // Initial-Sync.
            OnLocalLockChanged(TargetSelection.NoTarget, m_LocalSelection.CurrentTargetId);
        }

        private void DetachFromLocalSelection()
        {
            if (m_LocalSelection != null)
            {
                m_LocalSelection.CurrentTargetIdChanged -= OnLocalLockChanged;
                m_LocalSelection = null;
            }
        }

        private void OnLocalLockChanged(ulong previous, ulong current)
        {
            DetachFromTarget();

            if (current == TargetSelection.NoTarget)
            {
                HideFrame();
                return;
            }
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(current, out NetworkObject no) || no == null)
            {
                HideFrame();
                return;
            }

            UnitStats stats = no.GetComponent<UnitStats>();
            if (stats == null)
            {
                stats = no.GetComponentInChildren<UnitStats>();
            }
            if (stats == null)
            {
                HideFrame();
                return;
            }
            PlayerIdentity identity = no.GetComponent<PlayerIdentity>();
            if (identity == null)
            {
                identity = no.GetComponentInChildren<PlayerIdentity>();
            }

            AttachToTarget(stats, identity, no);
            ShowFrame();
        }

        // -------------------------------------------------------------------------
        // Ziel-Abo: HP / Mana / Name
        // -------------------------------------------------------------------------

        private void AttachToTarget(UnitStats stats, PlayerIdentity identity, NetworkObject sourceObj)
        {
            m_BoundStats = stats;
            m_BoundStats.HpChanged += OnTargetHpChanged;
            m_BoundStats.ManaChanged += OnTargetManaChanged;

            // Mana-Reihe nur einblenden, wenn die Einheit ueberhaupt eine Mana-Resource hat.
            if (m_ManaRow != null)
            {
                m_ManaRow.style.display = stats.HasMana ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Initialwerte sofort darstellen \u2014 OnNetworkSpawn feuert die Events
            // beim ersten Listener-Abo nicht erneut.
            UpdateHpVisual(stats.CurrentHp, stats.MaxHp);
            if (stats.HasMana)
            {
                UpdateManaVisual(stats.CurrentMana, stats.MaxMana);
            }

            // Name
            if (identity != null)
            {
                m_BoundIdentity = identity;
                m_BoundIdentity.DisplayNameChanged += OnTargetNameChanged;
                string name = m_BoundIdentity.DisplayName;
                m_NameLabel.text = string.IsNullOrWhiteSpace(name) ? sourceObj.name : name;
            }
            else
            {
                m_NameLabel.text = sourceObj != null ? sourceObj.name : "\u2014";
            }
        }

        private void DetachFromTarget()
        {
            if (m_BoundStats != null)
            {
                m_BoundStats.HpChanged -= OnTargetHpChanged;
                m_BoundStats.ManaChanged -= OnTargetManaChanged;
                m_BoundStats = null;
            }
            if (m_BoundIdentity != null)
            {
                m_BoundIdentity.DisplayNameChanged -= OnTargetNameChanged;
                m_BoundIdentity = null;
            }
        }

        private void OnTargetHpChanged(int current, int max) => UpdateHpVisual(current, max);
        private void OnTargetManaChanged(int current, int max) => UpdateManaVisual(current, max);
        private void OnTargetNameChanged(string name)
        {
            if (m_NameLabel != null)
            {
                m_NameLabel.text = string.IsNullOrWhiteSpace(name) ? "\u2014" : name;
            }
        }

        private void UpdateHpVisual(int current, int max)
        {
            if (m_HpFill == null || m_HpValueLabel == null)
            {
                return;
            }
            float pct = max > 0 ? Mathf.Clamp01(current / (float)max) * 100f : 0f;
            m_HpFill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
            m_HpValueLabel.text = current + " / " + max;
        }

        private void UpdateManaVisual(int current, int max)
        {
            if (m_ManaFill == null || m_ManaValueLabel == null)
            {
                return;
            }
            float pct = max > 0 ? Mathf.Clamp01(current / (float)max) * 100f : 0f;
            m_ManaFill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
            m_ManaValueLabel.text = current + " / " + max;
        }
    }
}
