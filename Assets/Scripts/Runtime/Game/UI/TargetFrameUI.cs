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
        [Header("Skin (Assets/Art/interface, _reverse Varianten)")]
        [SerializeField] private Texture2D m_FrameBackground;
        [SerializeField] private Texture2D m_HpFillTexture;
        [SerializeField] private Texture2D m_ManaFillTexture;
        [SerializeField] private Texture2D m_LevelBadgeBackground;
        [Tooltip("Optionale Border-Overlay-Textur (z. B. unit_frame_bronze), wird ueber das gesamte Target-Frame gelegt.")]
        [SerializeField] private Texture2D m_BorderOverlay;

        [Header("Layout")]
        [SerializeField] private float m_FrameWidth = 360f;
        [SerializeField] private float m_FrameHeight = 96f;
        [SerializeField] private float m_PortraitSize = 84f;
        [Tooltip("Abstand des Portraits vom rechten Frame-Rand.")]
        [SerializeField] private float m_PortraitRight = 6f;
        [SerializeField] private float m_PortraitTop = 6f;
        [SerializeField] private float m_LevelBadgeSize = 28f;
        [Tooltip("Rechter Beginn der HP-Bar im Frame.")]
        [SerializeField] private float m_BarRight = 92f;
        [SerializeField] private float m_BarWidth = 256f;
        [Tooltip("Rechter Beginn der Mana-Bar im Frame (typisch leicht eingerueckt).")]
        [SerializeField] private float m_ManaBarRight = 100f;
        [Tooltip("Breite der Mana-Bar (typisch etwas schmaler als HP).")]
        [SerializeField] private float m_ManaBarWidth = 240f;
        [Tooltip("Hoehe der HP-Bar (etwas hoeher als die Mana-Bar).")]
        [SerializeField] private float m_HpBarHeight = 20f;
        [Tooltip("Hoehe der Mana-Bar.")]
        [SerializeField] private float m_ManaBarHeight = 16f;
        [SerializeField] private float m_HpTop = 38f;
        [SerializeField] private float m_ManaTop = 62f;
        [Tooltip("Oberer Beginn des Name-Labels. 0 = automatisch (HpTop - 16).")]
        [SerializeField] private float m_NameTop = 18f;

        [Header("Anchor")]
        [SerializeField] private float m_AnchorTop = 16f;
        [Tooltip("Abstand des Target-Frames vom linken Bildschirmrand. Default = Player-Anchor (16) + Player-Frame (360) + kleine Luecke.")]
        [SerializeField] private float m_AnchorLeft = 388f;

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

        // Lokaler Spieler
        private TargetSelection m_LocalSelection;

        // Aktuelles Ziel-Abo
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
            HideFrame();
        }

        /// <summary>
        /// Liest <c>StreamingAssets/interface/hud_config.json</c> und ueberschreibt
        /// die SerializeField-Defaults. Target-Frame verwendet die gespiegelten
        /// (_reverse) Texturpfade aus dem Config.
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
            if (cfg.portraitInset > 0f) m_PortraitRight = cfg.portraitInset;
            if (cfg.portraitTop > 0f) m_PortraitTop = cfg.portraitTop;
            if (cfg.levelBadgeSize > 0f) m_LevelBadgeSize = cfg.levelBadgeSize;
            if (cfg.barInset > 0f)
            {
                m_BarRight = cfg.barInset;
                m_ManaBarRight = cfg.barInset;
            }
            if (cfg.barWidth > 0f)
            {
                m_BarWidth = cfg.barWidth;
                m_ManaBarWidth = cfg.barWidth;
            }
            if (cfg.hpBarInset > 0f) m_BarRight = cfg.hpBarInset;
            if (cfg.hpBarWidth > 0f) m_BarWidth = cfg.hpBarWidth;
            if (cfg.manaBarInset > 0f) m_ManaBarRight = cfg.manaBarInset;
            if (cfg.manaBarWidth > 0f) m_ManaBarWidth = cfg.manaBarWidth;
            if (cfg.hpBarHeight > 0f) m_HpBarHeight = cfg.hpBarHeight;
            if (cfg.manaBarHeight > 0f) m_ManaBarHeight = cfg.manaBarHeight;
            if (cfg.hpTop > 0f) m_HpTop = cfg.hpTop;
            if (cfg.manaTop > 0f) m_ManaTop = cfg.manaTop;
            if (cfg.nameTop.HasValue) m_NameTop = cfg.nameTop.Value;
            if (cfg.anchorTop > 0f) m_AnchorTop = cfg.anchorTop;
            if (cfg.targetAnchorLeft > 0f) m_AnchorLeft = cfg.targetAnchorLeft;

            Texture2D frameTex = HudConfigLoader.LoadTextureOrNull(cfg.frameTextureReverse);
            if (frameTex != null) m_FrameBackground = frameTex;
            Texture2D hpTex = HudConfigLoader.LoadTextureOrNull(cfg.hpFillTextureReverse);
            if (hpTex != null) m_HpFillTexture = hpTex;
            Texture2D manaTex = HudConfigLoader.LoadTextureOrNull(cfg.manaFillTextureReverse);
            if (manaTex != null) m_ManaFillTexture = manaTex;
            Texture2D badgeTex = HudConfigLoader.LoadTextureOrNull(cfg.levelBadgeTexture);
            if (badgeTex != null) m_LevelBadgeBackground = badgeTex;
            Texture2D borderTex = HudConfigLoader.LoadTextureOrNull(cfg.targetBorderTexture);
            if (borderTex != null) m_BorderOverlay = borderTex;
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
            // Wir besitzen das Root NICHT mit Clear(), weil sich Player- und
            // Target-Frame potenziell ein Root teilen koennen.
            m_Root.pickingMode = PickingMode.Ignore;

            // Gespiegelter Frame-Hintergrund (Portrait rechts, brushy Bar links).
            m_Frame = HudStyle.BuildTexturedFrame(m_FrameBackground, m_FrameWidth, m_FrameHeight);
            m_Frame.name = "target-frame";
            m_Frame.style.position = Position.Absolute;
            m_Frame.style.top = m_AnchorTop;
            m_Frame.style.left = m_AnchorLeft;

            // Portrait-Kreis rechts.
            VisualElement portrait = HudStyle.BuildPortraitCircle(m_PortraitSize);
            portrait.style.right = m_PortraitRight;
            portrait.style.top = m_PortraitTop;
            m_Frame.Add(portrait);

            // Level-Badge unten RECHTS am Portrait (gespiegelt zum Player-Frame): 15% nach aussen,
            // 50% vertikal ueberlappt — Badge "haengt" sichtbar unter dem Portrait.
            // Wird ERST nach dem Border-Overlay angehaengt, damit das Badge vor
            // dem Rarity-Ring sitzt.
            VisualElement levelBadge = HudStyle.BuildLevelBadge(m_LevelBadgeBackground, m_LevelBadgeSize, out m_LevelLabel);
            levelBadge.style.right = m_PortraitRight - m_LevelBadgeSize * 0.15f;
            levelBadge.style.top = m_PortraitTop + m_PortraitSize - m_LevelBadgeSize * 0.5f;

            // Name-Label oberhalb der HP-Bar, rechtsbuendig.
            m_NameLabel = new Label("\u2014") { name = "target-frame-name" };
            m_NameLabel.style.position = Position.Absolute;
            m_NameLabel.style.right = m_BarRight;
            m_NameLabel.style.top = m_NameTop;
            m_NameLabel.style.width = m_BarWidth;
            m_NameLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = 13f;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Frame.Add(m_NameLabel);

            // HP-Bar (rechtsbuendig, fuellt von rechts).
            VisualElement hpRow = HudStyle.BuildTexturedBar(
                "target-frame-hp",
                m_HpFillTexture,
                m_BarWidth,
                m_HpBarHeight,
                fillFromRight: true,
                out m_HpFill,
                out m_HpValueLabel);
            hpRow.style.right = m_BarRight;
            hpRow.style.top = m_HpTop;
            m_Frame.Add(hpRow);

            // Mana-Bar (rechtsbuendig, fuellt von rechts).
            m_ManaRow = HudStyle.BuildTexturedBar(
                "target-frame-mana",
                m_ManaFillTexture,
                m_ManaBarWidth,
                m_ManaBarHeight,
                fillFromRight: true,
                out m_ManaFill,
                out m_ManaValueLabel);
            m_ManaRow.style.right = m_ManaBarRight;
            m_ManaRow.style.top = m_ManaTop;
            m_Frame.Add(m_ManaRow);

            // Optionaler Border-Overlay (z. B. Bronze-Rarity-Rahmen). Liegt NUR
            // um das Portrait herum (leicht groesser, zentriert + leicht nach unten
            // versetzt) und ignoriert Input. Das Level-Badge wird danach
            // hinzugefuegt, damit es vor dem Ring sitzt.
            if (m_BorderOverlay != null)
            {
                const float c_BorderScale = 1.35f;   // Border ist ~35% groesser als das Portrait
                const float c_BorderYOffset = 6f;    // ein paar Pixel nach unten, damit Ring satt um den Kreis sitzt
                float borderSize = m_PortraitSize * c_BorderScale;
                float borderOffset = (borderSize - m_PortraitSize) * 0.5f;

                VisualElement border = new() { name = "target-frame-border" };
                border.pickingMode = PickingMode.Ignore;
                border.style.position = Position.Absolute;
                border.style.right = m_PortraitRight - borderOffset;
                border.style.top = m_PortraitTop - borderOffset + c_BorderYOffset;
                border.style.width = borderSize;
                border.style.height = borderSize;
                border.style.backgroundImage = new StyleBackground(m_BorderOverlay);
                m_Frame.Add(border);
            }

            // Level-Badge ZULETZT anhaengen -> liegt vor dem Border-Ring.
            m_Frame.Add(levelBadge);

            m_Root.Add(m_Frame);
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

            if (m_LevelLabel != null)
            {
                m_LevelLabel.text = stats.Level.ToString();
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
