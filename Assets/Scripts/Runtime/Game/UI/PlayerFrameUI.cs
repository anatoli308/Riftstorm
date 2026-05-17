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

        // Single Source of Truth: HudConfig + JSON. Keine doppelten
        // SerializeField-Defaults mehr im Component.
        private HudConfig m_Config;
        private Texture2D m_FrameBackground;
        private Texture2D m_HpFillTexture;
        private Texture2D m_ManaFillTexture;
        private Texture2D m_LevelBadgeBackground;

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
            m_Config = HudConfigLoader.Load();
            ResolveTextures();
        }

        private void OnEnable()
        {
            BuildVisualTree();
        }

        /// <summary>
        /// Resolved die HUD-Texturen ueber den <see cref="TextureManager"/>-
        /// Pure-Service. Pfade kommen aus <see cref="HudConfig"/>.
        /// </summary>
        private void ResolveTextures()
        {
            m_FrameBackground = HudConfigLoader.LoadTextureOrNull(m_Config.frameTexture);
            m_HpFillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.hpFillTexture);
            m_ManaFillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.manaFillTexture);
            m_LevelBadgeBackground = HudConfigLoader.LoadTextureOrNull(m_Config.levelBadgeTexture);
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

            HudConfig c = m_Config;

            // Frame-Hintergrund (gemaltes Sprite: Portrait-Kreis links + brushy Bar rechts).
            m_Frame = HudStyle.BuildTexturedFrame(m_FrameBackground, c.frameWidth, c.frameHeight);
            m_Frame.style.position = Position.Absolute;
            m_Frame.style.top = c.anchorTop;
            m_Frame.style.left = c.playerAnchorLeft;

            // Portrait-Kreis (Platzhalter, kann spaeter durch 3D-/Sprite-Render ersetzt werden).
            VisualElement portrait = HudStyle.BuildPortraitCircle(c.portraitSize);
            portrait.style.left = c.portraitInset;
            portrait.style.top = c.portraitTop;
            m_Frame.Add(portrait);

            // Level-Badge unten LINKS am Portrait-Kreis. Versatz konfigurierbar
            // ueber levelBadgeOffsetXRatio (nach aussen) und levelBadgeOffsetYRatio
            // (vertikale Ueberlappung).
            VisualElement levelBadge = HudStyle.BuildLevelBadge(m_LevelBadgeBackground, c.levelBadgeSize, out m_LevelLabel);
            levelBadge.style.left = c.portraitInset - c.levelBadgeSize * c.levelBadgeOffsetXRatio;
            levelBadge.style.top = c.portraitTop + c.portraitSize - c.levelBadgeSize * c.levelBadgeOffsetYRatio;
            m_Frame.Add(levelBadge);

            // Name-Label oberhalb der HP-Bar.
            m_NameLabel = new Label("\u2014") { name = "player-frame-name" };
            m_NameLabel.style.position = Position.Absolute;
            m_NameLabel.style.left = c.hpBarInset;
            m_NameLabel.style.top = c.nameTop;
            m_NameLabel.style.width = c.hpBarWidth;
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = c.nameFontSize;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Frame.Add(m_NameLabel);

            // HP-Bar (linksbuendig).
            VisualElement hpRow = HudStyle.BuildTexturedBar(
                "player-frame-hp",
                m_HpFillTexture,
                c.hpBarWidth,
                c.hpBarHeight,
                fillFromRight: false,
                out m_HpFill,
                out m_HpValueLabel);
            hpRow.style.left = c.hpBarInset;
            hpRow.style.top = c.hpTop;
            m_Frame.Add(hpRow);

            // Mana-Bar (linksbuendig).
            m_ManaRow = HudStyle.BuildTexturedBar(
                "player-frame-mana",
                m_ManaFillTexture,
                c.manaBarWidth,
                c.manaBarHeight,
                fillFromRight: false,
                out m_ManaFill,
                out m_ManaValueLabel);
            m_ManaRow.style.left = c.manaBarInset;
            m_ManaRow.style.top = c.manaTop;
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
