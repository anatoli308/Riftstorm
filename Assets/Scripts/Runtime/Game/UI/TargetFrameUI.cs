using System;
using Riftstorm.ApplicationLifecycle.UI;
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

        // Single Source of Truth: HudConfig + JSON. Target nutzt die _reverse-
        // Texturen sowie den Rarity-Border.
        private HudConfig m_Config;
        private Texture2D m_FrameBackground;
        private Texture2D m_HpFillTexture;
        private Texture2D m_ManaFillTexture;
        private Texture2D m_LevelBadgeBackground;
        private Texture2D m_BorderOverlay;

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
            m_Config = HudConfigLoader.Load();
            ResolveTextures();
        }

        private void OnEnable()
        {
            BuildVisualTree();
            HideFrame();
        }

        /// <summary>
        /// Resolved die HUD-Texturen ueber den <see cref="TextureManager"/>-
        /// Pure-Service. Target nutzt die <c>_reverse</c>-Varianten plus den
        /// optionalen Rarity-Ring.
        /// </summary>
        private void ResolveTextures()
        {
            m_FrameBackground = HudConfigLoader.LoadTextureOrNull(m_Config.frameTextureReverse);
            m_HpFillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.hpFillTextureReverse);
            m_ManaFillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.manaFillTextureReverse);
            m_LevelBadgeBackground = HudConfigLoader.LoadTextureOrNull(m_Config.levelBadgeTexture);
            m_BorderOverlay = HudConfigLoader.LoadTextureOrNull(m_Config.targetBorderTexture);
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

            HudConfig c = m_Config;

            // Gespiegelter Frame-Hintergrund (Portrait rechts, brushy Bar links).
            m_Frame = HudStyle.BuildTexturedFrame(m_FrameBackground, c.frameWidth, c.frameHeight);
            m_Frame.name = "target-frame";
            m_Frame.style.position = Position.Absolute;
            m_Frame.style.top = c.anchorTop;
            m_Frame.style.left = c.targetAnchorLeft;

            // Portrait-Kreis rechts.
            VisualElement portrait = HudStyle.BuildPortraitCircle(c.portraitSize);
            portrait.style.right = c.portraitInset;
            portrait.style.top = c.portraitTop;
            m_Frame.Add(portrait);

            // Level-Badge unten RECHTS am Portrait (gespiegelt zum Player-Frame). Versatz
            // konfigurierbar ueber levelBadgeOffsetXRatio / levelBadgeOffsetYRatio.
            // Wird ERST nach dem Border-Overlay angehaengt, damit das Badge vor
            // dem Rarity-Ring sitzt.
            VisualElement levelBadge = HudStyle.BuildLevelBadge(m_LevelBadgeBackground, c.levelBadgeSize, out m_LevelLabel);
            levelBadge.style.right = c.portraitInset - c.levelBadgeSize * c.levelBadgeOffsetXRatio;
            levelBadge.style.top = c.portraitTop + c.portraitSize - c.levelBadgeSize * c.levelBadgeOffsetYRatio;

            // Name-Label oberhalb der HP-Bar, rechtsbuendig.
            m_NameLabel = new Label("\u2014") { name = "target-frame-name" };
            m_NameLabel.style.position = Position.Absolute;
            m_NameLabel.style.right = c.hpBarInset;
            m_NameLabel.style.top = c.nameTop;
            m_NameLabel.style.width = c.hpBarWidth;
            m_NameLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = c.nameFontSize;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            UIFonts.Apply(m_NameLabel, UIFonts.Heading);
            m_Frame.Add(m_NameLabel);

            // HP-Bar (rechtsbuendig, fuellt von rechts).
            VisualElement hpRow = HudStyle.BuildTexturedBar(
                "target-frame-hp",
                m_HpFillTexture,
                c.hpBarWidth,
                c.hpBarHeight,
                fillFromRight: true,
                out m_HpFill,
                out m_HpValueLabel);
            hpRow.style.right = c.hpBarInset;
            hpRow.style.top = c.hpTop;
            m_Frame.Add(hpRow);

            // Mana-Bar (rechtsbuendig, fuellt von rechts).
            m_ManaRow = HudStyle.BuildTexturedBar(
                "target-frame-mana",
                m_ManaFillTexture,
                c.manaBarWidth,
                c.manaBarHeight,
                fillFromRight: true,
                out m_ManaFill,
                out m_ManaValueLabel);
            m_ManaRow.style.right = c.manaBarInset;
            m_ManaRow.style.top = c.manaTop;
            m_Frame.Add(m_ManaRow);

            // Optionaler Border-Overlay (z. B. Bronze-Rarity-Rahmen). Liegt NUR
            // um das Portrait herum (leicht groesser, zentriert + leicht nach unten
            // versetzt) und ignoriert Input. Das Level-Badge wird danach
            // hinzugefuegt, damit es vor dem Ring sitzt.
            if (m_BorderOverlay != null)
            {
                float borderSize = c.portraitSize * c.targetBorderScale;
                float borderOffset = (borderSize - c.portraitSize) * 0.5f;

                VisualElement border = new() { name = "target-frame-border" };
                border.pickingMode = PickingMode.Ignore;
                border.style.position = Position.Absolute;
                border.style.right = c.portraitInset - borderOffset;
                border.style.top = c.portraitTop - borderOffset + c.targetBorderYOffset;
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
