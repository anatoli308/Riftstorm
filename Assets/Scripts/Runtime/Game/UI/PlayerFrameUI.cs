using System;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// WoW-Style Player-Portrait oben links. Zeigt Name, Level, HP- und Mana-Bar
    /// des lokalen Spielers. Reagiert ausschliesslich auf
    /// <see cref="UnitStats.HpChanged"/> / <see cref="UnitStats.ManaChanged"/> /
    /// <see cref="PlayerIdentity.DisplayNameChanged"/>. Kein Polling, kein
    /// Update-Tick fuer Werte; einzig das Binden des LocalPlayer-Objekts
    /// passiert in <see cref="Update"/>, bis NGO den lokalen Spieler gespawnt
    /// hat \u2014 danach laeuft alles eventbasiert.
    /// <para>
    /// Baut den Visual-Tree komplett programmatisch \u2014 es muss kein
    /// UXML-Asset zugewiesen werden. Es reicht ein GameObject mit
    /// <see cref="UIDocument"/> plus dieser Komponente in der Game-Scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerFrameUI : MonoBehaviour
    {
        private UIDocument m_Document;

        // Visual-Tree
        private VisualElement m_Root;
        private VisualElement m_Frame;
        private Label m_NameLabel;
        private Label m_LevelLabel;
        private VisualElement m_HpFill;
        private Label m_HpValueLabel;
        private VisualElement m_ManaRow;
        private VisualElement m_ManaFill;
        private Label m_ManaValueLabel;

        // Bindungen
        private UnitStats m_BoundStats;
        private PlayerIdentity m_BoundIdentity;

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
            DetachFromLocalPlayer();
        }

        private void Update()
        {
            if (m_BoundStats != null)
            {
                return;
            }
            TryBindLocalPlayer();
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

            m_Frame = new VisualElement { name = "player-frame" };
            HudStyle.ApplyFramePanel(m_Frame);
            m_Frame.style.position = Position.Absolute;
            m_Frame.style.top = 16f;
            m_Frame.style.left = 16f;
            m_Frame.style.minWidth = 280f;

            // Header: Portrait + (Name/Level)
            VisualElement header = new() { name = "player-frame-header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4f;

            VisualElement portrait = new() { name = "player-frame-portrait" };
            portrait.style.width = 56f;
            portrait.style.height = 56f;
            portrait.style.marginRight = 8f;
            portrait.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.10f, 1f));
            HudStyle.ApplyBorder(portrait, HudStyle.AccentGold, 2f);

            // Level-Badge in der Portrait-Ecke (WoW-Style)
            m_LevelLabel = new Label("1") { name = "player-frame-level" };
            m_LevelLabel.style.position = Position.Absolute;
            m_LevelLabel.style.bottom = -2f;
            m_LevelLabel.style.right = -2f;
            m_LevelLabel.style.paddingLeft = 4f;
            m_LevelLabel.style.paddingRight = 4f;
            m_LevelLabel.style.paddingTop = 1f;
            m_LevelLabel.style.paddingBottom = 1f;
            m_LevelLabel.style.fontSize = 12f;
            m_LevelLabel.style.color = Color.white;
            m_LevelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_LevelLabel.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.06f, 0.95f));
            HudStyle.ApplyBorder(m_LevelLabel, HudStyle.AccentGold, 1f);
            portrait.Add(m_LevelLabel);

            header.Add(portrait);

            VisualElement nameCol = new();
            nameCol.style.flexGrow = 1f;
            nameCol.style.flexDirection = FlexDirection.Column;

            m_NameLabel = new Label("\u2014") { name = "player-frame-name" };
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = 16f;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameCol.Add(m_NameLabel);

            header.Add(nameCol);
            m_Frame.Add(header);

            // HP
            VisualElement hpRow = HudStyle.BuildBarRow(
                "player-frame-hp",
                new Color(0.65f, 0.10f, 0.10f, 1f),
                out m_HpFill,
                out m_HpValueLabel);
            m_Frame.Add(hpRow);

            // Mana
            m_ManaRow = HudStyle.BuildBarRow(
                "player-frame-mana",
                new Color(0.15f, 0.40f, 0.85f, 1f),
                out m_ManaFill,
                out m_ManaValueLabel);
            m_ManaRow.style.marginTop = 4f;
            m_Frame.Add(m_ManaRow);

            m_Root.Add(m_Frame);
        }

        // -------------------------------------------------------------------------
        // LocalPlayer Binding
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

            UnitStats stats = playerObj.GetComponent<UnitStats>();
            if (stats == null)
            {
                stats = playerObj.GetComponentInChildren<UnitStats>();
            }
            if (stats == null)
            {
                return;
            }

            PlayerIdentity identity = playerObj.GetComponent<PlayerIdentity>();
            if (identity == null)
            {
                identity = playerObj.GetComponentInChildren<PlayerIdentity>();
            }

            AttachToLocalPlayer(stats, identity, playerObj);
        }

        private void AttachToLocalPlayer(UnitStats stats, PlayerIdentity identity, NetworkObject sourceObj)
        {
            m_BoundStats = stats;
            m_BoundStats.HpChanged += OnHpChanged;
            m_BoundStats.ManaChanged += OnManaChanged;

            if (m_ManaRow != null)
            {
                m_ManaRow.style.display = stats.HasMana ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateHpVisual(stats.CurrentHp, stats.MaxHp);
            if (stats.HasMana)
            {
                UpdateManaVisual(stats.CurrentMana, stats.MaxMana);
            }

            if (m_LevelLabel != null)
            {
                m_LevelLabel.text = stats.Level.ToString();
            }

            if (identity != null)
            {
                m_BoundIdentity = identity;
                m_BoundIdentity.DisplayNameChanged += OnNameChanged;
                string n = m_BoundIdentity.DisplayName;
                m_NameLabel.text = string.IsNullOrWhiteSpace(n) ? sourceObj.name : n;
            }
            else
            {
                m_NameLabel.text = sourceObj != null ? sourceObj.name : "\u2014";
            }
        }

        private void DetachFromLocalPlayer()
        {
            if (m_BoundStats != null)
            {
                m_BoundStats.HpChanged -= OnHpChanged;
                m_BoundStats.ManaChanged -= OnManaChanged;
                m_BoundStats = null;
            }
            if (m_BoundIdentity != null)
            {
                m_BoundIdentity.DisplayNameChanged -= OnNameChanged;
                m_BoundIdentity = null;
            }
        }

        private void OnHpChanged(int current, int max) => UpdateHpVisual(current, max);
        private void OnManaChanged(int current, int max) => UpdateManaVisual(current, max);
        private void OnNameChanged(string n)
        {
            if (m_NameLabel != null)
            {
                m_NameLabel.text = string.IsNullOrWhiteSpace(n) ? "\u2014" : n;
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
