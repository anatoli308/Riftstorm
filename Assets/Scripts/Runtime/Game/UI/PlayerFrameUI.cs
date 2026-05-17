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
        // Skin-Texturen (Assets/Art/interface/unit_frame*.png).
        [Header("Skin (Assets/Art/interface)")]
        [SerializeField] private Texture2D m_FrameBackground;
        [SerializeField] private Texture2D m_HpFillTexture;
        [SerializeField] private Texture2D m_ManaFillTexture;
        [SerializeField] private Texture2D m_LevelBadgeBackground;

        [Header("Layout")]
        [Tooltip("Gesamtbreite des Frame-Sprites in Pixeln.")]
        [SerializeField] private float m_FrameWidth = 360f;
        [Tooltip("Gesamthoehe des Frame-Sprites in Pixeln.")]
        [SerializeField] private float m_FrameHeight = 96f;
        [Tooltip("Durchmesser des runden Portraits.")]
        [SerializeField] private float m_PortraitSize = 84f;
        [Tooltip("Abstand des Portraits vom linken Frame-Rand.")]
        [SerializeField] private float m_PortraitLeft = 6f;
        [Tooltip("Abstand des Portraits vom oberen Frame-Rand.")]
        [SerializeField] private float m_PortraitTop = 6f;
        [Tooltip("Durchmesser der Level-Badge.")]
        [SerializeField] private float m_LevelBadgeSize = 28f;
        [Tooltip("Linker Beginn der HP-Bar im Frame.")]
        [SerializeField] private float m_BarLeft = 92f;
        [Tooltip("Breite der HP-Bar.")]
        [SerializeField] private float m_BarWidth = 256f;
        [Tooltip("Linker Beginn der Mana-Bar im Frame (typisch leicht eingerueckt).")]
        [SerializeField] private float m_ManaBarLeft = 100f;
        [Tooltip("Breite der Mana-Bar (typisch etwas schmaler als HP).")]
        [SerializeField] private float m_ManaBarWidth = 240f;
        [Tooltip("Hoehe der HP-Bar (etwas hoeher als die Mana-Bar).")]
        [SerializeField] private float m_HpBarHeight = 20f;
        [Tooltip("Hoehe der Mana-Bar.")]
        [SerializeField] private float m_ManaBarHeight = 16f;
        [Tooltip("Oberer Beginn der HP-Bar.")]
        [SerializeField] private float m_HpTop = 38f;
        [Tooltip("Oberer Beginn der Mana-Bar.")]
        [SerializeField] private float m_ManaTop = 62f;
        [Tooltip("Oberer Beginn des Name-Labels. 0 = automatisch (HpTop - 16).")]
        [SerializeField] private float m_NameTop = 18f;

        [Header("Anchor")]
        [SerializeField] private float m_AnchorTop = 16f;
        [SerializeField] private float m_AnchorLeft = 16f;

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
            ApplyConfigOverrides();
        }

        private void OnEnable()
        {
            BuildVisualTree();
        }

        /// <summary>
        /// Liest <c>StreamingAssets/interface/hud_config.json</c> und ueberschreibt
        /// die SerializeField-Defaults, falls dort Werte gesetzt sind. So koennen
        /// Layout und Texturen extern getweakt werden, ohne das Component zu touchen.
        /// </summary>
        private void ApplyConfigOverrides()
        {
            HudConfig cfg = HudConfigLoader.LoadOrNull();
            if (cfg == null)
            {
                return;
            }
            if (cfg.frameWidth > 0f) m_FrameWidth = cfg.frameWidth;
            if (cfg.frameHeight > 0f) m_FrameHeight = cfg.frameHeight;
            if (cfg.portraitSize > 0f) m_PortraitSize = cfg.portraitSize;
            if (cfg.portraitInset > 0f) m_PortraitLeft = cfg.portraitInset;
            if (cfg.portraitTop > 0f) m_PortraitTop = cfg.portraitTop;
            if (cfg.levelBadgeSize > 0f) m_LevelBadgeSize = cfg.levelBadgeSize;
            if (cfg.barInset > 0f)
            {
                m_BarLeft = cfg.barInset;
                m_ManaBarLeft = cfg.barInset;
            }
            if (cfg.barWidth > 0f)
            {
                m_BarWidth = cfg.barWidth;
                m_ManaBarWidth = cfg.barWidth;
            }
            if (cfg.hpBarInset > 0f) m_BarLeft = cfg.hpBarInset;
            if (cfg.hpBarWidth > 0f) m_BarWidth = cfg.hpBarWidth;
            if (cfg.manaBarInset > 0f) m_ManaBarLeft = cfg.manaBarInset;
            if (cfg.manaBarWidth > 0f) m_ManaBarWidth = cfg.manaBarWidth;
            if (cfg.hpBarHeight > 0f) m_HpBarHeight = cfg.hpBarHeight;
            if (cfg.manaBarHeight > 0f) m_ManaBarHeight = cfg.manaBarHeight;
            if (cfg.hpTop > 0f) m_HpTop = cfg.hpTop;
            if (cfg.manaTop > 0f) m_ManaTop = cfg.manaTop;
            if (cfg.nameTop.HasValue) m_NameTop = cfg.nameTop.Value;
            if (cfg.anchorTop > 0f) m_AnchorTop = cfg.anchorTop;
            if (cfg.playerAnchorLeft > 0f) m_AnchorLeft = cfg.playerAnchorLeft;

            Texture2D frameTex = HudConfigLoader.LoadTextureOrNull(cfg.frameTexture);
            if (frameTex != null) m_FrameBackground = frameTex;
            Texture2D hpTex = HudConfigLoader.LoadTextureOrNull(cfg.hpFillTexture);
            if (hpTex != null) m_HpFillTexture = hpTex;
            Texture2D manaTex = HudConfigLoader.LoadTextureOrNull(cfg.manaFillTexture);
            if (manaTex != null) m_ManaFillTexture = manaTex;
            Texture2D badgeTex = HudConfigLoader.LoadTextureOrNull(cfg.levelBadgeTexture);
            if (badgeTex != null) m_LevelBadgeBackground = badgeTex;
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

            // Frame-Hintergrund (gemaltes Sprite: Portrait-Kreis links + brushy Bar rechts).
            m_Frame = HudStyle.BuildTexturedFrame(m_FrameBackground, m_FrameWidth, m_FrameHeight);
            m_Frame.style.position = Position.Absolute;
            m_Frame.style.top = m_AnchorTop;
            m_Frame.style.left = m_AnchorLeft;

            // Portrait-Kreis (Platzhalter, kann spaeter durch 3D-/Sprite-Render ersetzt werden).
            VisualElement portrait = HudStyle.BuildPortraitCircle(m_PortraitSize);
            portrait.style.left = m_PortraitLeft;
            portrait.style.top = m_PortraitTop;
            m_Frame.Add(portrait);

            // Level-Badge unten LINKS am Portrait-Kreis: horizontal 15% nach aussen, vertikal
            // 50% ueberlappt (Badge-Mitte sitzt auf Portrait-Unterkante — "haengt" sichtbar nach unten).
            VisualElement levelBadge = HudStyle.BuildLevelBadge(m_LevelBadgeBackground, m_LevelBadgeSize, out m_LevelLabel);
            levelBadge.style.left = m_PortraitLeft - m_LevelBadgeSize * 0.15f;
            levelBadge.style.top = m_PortraitTop + m_PortraitSize - m_LevelBadgeSize * 0.5f;
            m_Frame.Add(levelBadge);

            // Name-Label oberhalb der HP-Bar.
            m_NameLabel = new Label("\u2014") { name = "player-frame-name" };
            m_NameLabel.style.position = Position.Absolute;
            m_NameLabel.style.left = m_BarLeft;
            m_NameLabel.style.top = m_NameTop;
            m_NameLabel.style.width = m_BarWidth;
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = 13f;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Frame.Add(m_NameLabel);

            // HP-Bar (linksbuendig).
            VisualElement hpRow = HudStyle.BuildTexturedBar(
                "player-frame-hp",
                m_HpFillTexture,
                m_BarWidth,
                m_HpBarHeight,
                fillFromRight: false,
                out m_HpFill,
                out m_HpValueLabel);
            hpRow.style.left = m_BarLeft;
            hpRow.style.top = m_HpTop;
            m_Frame.Add(hpRow);

            // Mana-Bar (linksbuendig).
            m_ManaRow = HudStyle.BuildTexturedBar(
                "player-frame-mana",
                m_ManaFillTexture,
                m_ManaBarWidth,
                m_ManaBarHeight,
                fillFromRight: false,
                out m_ManaFill,
                out m_ManaValueLabel);
            m_ManaRow.style.left = m_ManaBarLeft;
            m_ManaRow.style.top = m_ManaTop;
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
