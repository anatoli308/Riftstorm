using System;
using Riftstorm.Management.FontManagement;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Player;
using Riftstorm.Game.Spells;
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
        private Texture2D m_FrameBackgroundBoss;
        private Texture2D m_FrameBackgroundElite;
        private Texture2D m_HpFillTexture;
        private Texture2D m_ManaFillTexture;
        private Texture2D m_LevelBadgeBackground;
        private Texture2D m_CastBarBackground;
        private Texture2D m_CastBarFill;

        // Visual-Tree
        private VisualElement m_Root;
        private VisualElement m_Frame;
        private VisualElement m_RankBorder;
        private Label m_NameLabel;
        private Label m_LevelLabel;
        private VisualElement m_HpFill;
        private Label m_HpValueLabel;
        private VisualElement m_ManaRow;
        private VisualElement m_ManaFill;
        private Label m_ManaValueLabel;
        private Label m_TargetOfTargetLabel;

        // Cast-Bar des Ziels (unter dem Portrait). Fortschritt wird lokal aus
        // Start + Dauer interpoliert (UIToolkit-Scheduler, kein Update-Polling).
        private VisualElement m_CastRow;
        private VisualElement m_CastFill;
        private Label m_CastNameLabel;
        private IVisualElementScheduledItem m_CastTick;
        private float m_CastStartUnscaled;
        private float m_CastDurationSeconds;

        // Lokaler Spieler
        private TargetSelection m_LocalSelection;

        // Aktuelles Ziel-Abo
        private UnitStats m_BoundStats;
        private INameSource m_BoundIdentity;
        private Npc.NpcController m_BoundNpc;

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
            m_FrameBackgroundBoss = HudConfigLoader.LoadTextureOrNull(m_Config.frameTextureBoss);
            m_FrameBackgroundElite = HudConfigLoader.LoadTextureOrNull(m_Config.frameTextureElite);
            m_HpFillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.hpFillTextureReverse);
            m_ManaFillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.manaFillTextureReverse);
            m_LevelBadgeBackground = HudConfigLoader.LoadTextureOrNull(m_Config.levelBadgeTexture);
            m_CastBarBackground = HudConfigLoader.LoadTextureOrNull(m_Config.castBarBackgroundTexture);
            m_CastBarFill = HudConfigLoader.LoadTextureOrNull(m_Config.castBarFillTexture);
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

            m_TargetOfTargetLabel = new Label(string.Empty) { name = "target-frame-target-of-target" };
            m_TargetOfTargetLabel.style.position = Position.Absolute;
            m_TargetOfTargetLabel.style.right = c.hpBarInset;
            m_TargetOfTargetLabel.style.top = c.manaTop + c.manaBarHeight + 4f;
            m_TargetOfTargetLabel.style.width = c.hpBarWidth;
            m_TargetOfTargetLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            m_TargetOfTargetLabel.style.color = new Color(0.95f, 0.78f, 0.3f, 1f);
            m_TargetOfTargetLabel.style.fontSize = Mathf.Max(10, c.nameFontSize - 4);
            m_TargetOfTargetLabel.style.display = DisplayStyle.None;
            UIFonts.Apply(m_TargetOfTargetLabel, UIFonts.Body);
            m_Frame.Add(m_TargetOfTargetLabel);

            // Cast-Bar des Ziels, UNTER dem Frame. Standardmaessig versteckt; wird
            // nur waehrend eines Cast-Time-Spells eingeblendet und client-seitig aus
            // Start + Dauer interpoliert.
            // Breite = volle HP-Bar-Breite, rechtsbuendig wie HP/Mana-Bar, damit die
            // Bar den gesamten schwarzen Castbar-Bereich ausfuellt statt nur das
            // Portrait zu ueberspannen.
            float castWidth = c.hpBarWidth;
            float castHeight = c.castBarHeight;
            m_CastRow = HudStyle.BuildTexturedBar(
                "target-frame-cast",
                m_CastBarFill,
                castWidth,
                castHeight,
                fillFromRight: false,
                out m_CastFill,
                out m_CastNameLabel);
            if (m_CastBarBackground != null)
            {
                m_CastRow.style.backgroundImage = new StyleBackground(m_CastBarBackground);
            }
            // Rechtsbuendig wie HP/Mana-Bar — Cast-Bar fuellt den vollen Balkenbereich.
            m_CastRow.style.right = c.hpBarInset;
            m_CastRow.style.top = c.portraitTop + c.portraitSize + 4f;
            m_CastNameLabel.style.fontSize = c.castBarNameFontSize;
            m_CastNameLabel.text = string.Empty;
            m_CastRow.style.display = DisplayStyle.None;
            m_Frame.Add(m_CastRow);

            // Rang-Rahmen (Boss/Elite) als korrekt positionierter Overlay-Ring um
            // das Portrait (leicht groesser, zentriert + leicht nach unten
            // versetzt). Texturlos und versteckt erzeugt — ApplyRankFrame() setzt
            // Boss-/Elite-Textur und Sichtbarkeit nach NPC-Rang (Spec: Boss >
            // Elite > "sonst keins"). Ignoriert Input; das Level-Badge wird danach
            // angehaengt, damit es vor dem Ring sitzt.
            float rankBorderSize = c.portraitSize * c.targetBorderScale;
            float rankBorderOffset = (rankBorderSize - c.portraitSize) * 0.5f;

            m_RankBorder = new() { name = "target-frame-rank" };
            m_RankBorder.pickingMode = PickingMode.Ignore;
            m_RankBorder.style.position = Position.Absolute;
            m_RankBorder.style.right = c.portraitInset - rankBorderOffset;
            m_RankBorder.style.top = c.portraitTop - rankBorderOffset + c.targetBorderYOffset;
            m_RankBorder.style.width = rankBorderSize;
            m_RankBorder.style.height = rankBorderSize;
            m_RankBorder.style.display = DisplayStyle.None;
            m_Frame.Add(m_RankBorder);

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
            if (!playerObj.TryGetComponent<TargetSelection>(out var sel))
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

            if (!no.TryGetComponent<UnitStats>(out var stats))
            {
                stats = no.GetComponentInChildren<UnitStats>();
            }
            if (stats == null)
            {
                HideFrame();
                return;
            }
            // INameSource statt konkret PlayerIdentity: Spieler liefern den Namen
            // ueber PlayerIdentity (NetworkVariable), NPCs ueber NpcIdentity
            // (UnitStats.DisplayName aus dem npc_template). Ohne diese Aufloesung
            // fiel das Frame fuer NPCs auf sourceObj.name (Prefab "Flare_NPC") zurueck.
            INameSource identity = no.GetComponent<INameSource>();
            if (identity == null)
            {
                identity = no.GetComponentInChildren<INameSource>();
            }

            AttachToTarget(stats, identity, no);
            ShowFrame();
        }

        // -------------------------------------------------------------------------
        // Ziel-Abo: HP / Mana / Name
        // -------------------------------------------------------------------------

        private void AttachToTarget(UnitStats stats, INameSource identity, NetworkObject sourceObj)
        {
            m_BoundStats = stats;
            m_BoundStats.HpChanged += OnTargetHpChanged;
            m_BoundStats.ManaChanged += OnTargetManaChanged;

            if (!sourceObj.TryGetComponent<Npc.NpcController>(out var npc))
            {
                npc = sourceObj.GetComponentInChildren<Npc.NpcController>();
            }
            m_BoundNpc = npc;
            if (m_BoundNpc != null)
            {
                m_BoundNpc.CurrentTargetChanged += OnTargetOfTargetChanged;
                UpdateTargetOfTargetVisual(m_BoundNpc.CurrentTargetNetworkId);

                // Cast-Bar des Ziels anbinden. Laeuft bereits ein Cast (spaetes
                // Anvisieren), sofort mit dem vorhandenen Snapshot starten.
                m_BoundNpc.LocalCastStarted += OnTargetCastStarted;
                m_BoundNpc.LocalCastEnded += OnTargetCastEnded;
                if (m_BoundNpc.LocalCastActive)
                {
                    BeginCastVisual(
                        m_BoundNpc.LocalCastSpellId,
                        m_BoundNpc.LocalCastStartUnscaled,
                        m_BoundNpc.LocalCastDurationSeconds);
                }
                else
                {
                    HideCastBar();
                }
            }
            else
            {
                UpdateTargetOfTargetVisual(0UL);
                HideCastBar();
            }

            // Rahmen nach NPC-Rang waehlen (Boss > Elite > Default).
            ApplyRankFrame();

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
            if (m_BoundNpc != null)
            {
                m_BoundNpc.CurrentTargetChanged -= OnTargetOfTargetChanged;
                m_BoundNpc.LocalCastStarted -= OnTargetCastStarted;
                m_BoundNpc.LocalCastEnded -= OnTargetCastEnded;
                m_BoundNpc = null;
            }
            HideCastBar();
            // Rahmen auf Default zuruecksetzen, damit ein Boss/Elite-Rahmen nicht
            // beim naechsten Ziel kurz nachhaengt.
            ApplyRankFrame();
            UpdateTargetOfTargetVisual(0UL);
        }

        /// <summary>
        /// Setzt den korrekt positionierten Rang-Overlay-Ring (<see cref="m_RankBorder"/>)
        /// nach NPC-Rang: Boss &gt; Elite &gt; "sonst keins". Boss zeigt
        /// <c>unit_frame_boss</c>, Elite <c>unit_frame_elite</c>; bei normalem Rang
        /// (oder fehlender/nicht ladbarer Rang-Textur) wird der Ring versteckt.
        /// Der Frame-Hintergrund selbst bleibt unveraendert.
        /// </summary>
        private void ApplyRankFrame()
        {
            if (m_RankBorder == null)
            {
                return;
            }
            Texture2D tex = null;
            if (m_BoundNpc != null)
            {
                if (m_BoundNpc.IsBoss && m_FrameBackgroundBoss != null)
                {
                    tex = m_FrameBackgroundBoss;
                }
                else if (m_BoundNpc.IsElite && m_FrameBackgroundElite != null)
                {
                    tex = m_FrameBackgroundElite;
                }
            }
            if (tex != null)
            {
                m_RankBorder.style.backgroundImage = new StyleBackground(tex);
                m_RankBorder.style.display = DisplayStyle.Flex;
            }
            else
            {
                m_RankBorder.style.backgroundImage = new StyleBackground();
                m_RankBorder.style.display = DisplayStyle.None;
            }
        }

        private void OnTargetHpChanged(int current, int max) => UpdateHpVisual(current, max);
        private void OnTargetManaChanged(int current, int max) => UpdateManaVisual(current, max);
        private void OnTargetOfTargetChanged(ulong targetId) => UpdateTargetOfTargetVisual(targetId);
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

        /// <summary>
        /// Zeigt den Namen des aktuellen Ziels des anvisierten NPCs an
        /// ("Target-of-Target"). Bei <paramref name="targetId"/> == 0 (kein Ziel)
        /// wird das Label ausgeblendet. Der Name wird client-seitig aus dem
        /// replizierten <see cref="NetworkObject"/> aufgel&#246;st.
        /// </summary>
        /// <param name="targetId">Replizierte <c>NetworkObjectId</c> des NPC-Ziels; 0 = kein Ziel.</param>
        private void UpdateTargetOfTargetVisual(ulong targetId)
        {
            if (m_TargetOfTargetLabel == null)
            {
                return;
            }

            if (targetId == 0UL)
            {
                m_TargetOfTargetLabel.style.display = DisplayStyle.None;
                m_TargetOfTargetLabel.text = string.Empty;
                return;
            }

            string name = ResolveTargetName(targetId);
            if (string.IsNullOrWhiteSpace(name))
            {
                m_TargetOfTargetLabel.style.display = DisplayStyle.None;
                m_TargetOfTargetLabel.text = string.Empty;
                return;
            }

            m_TargetOfTargetLabel.style.display = DisplayStyle.Flex;
            m_TargetOfTargetLabel.text = name;
        }

        /// <summary>
        /// L&#246;st den Anzeigenamen eines replizierten Ziels &#252;ber dessen
        /// <c>NetworkObjectId</c> auf. Bevorzugt <see cref="INameSource.DisplayName"/>,
        /// f&#228;llt sonst auf den GameObject-Namen zur&#252;ck.
        /// </summary>
        /// <param name="targetId">Replizierte <c>NetworkObjectId</c> des Ziels.</param>
        /// <returns>Anzeigename oder leerer String, falls nicht aufl&#246;sbar.</returns>
        private static string ResolveTargetName(ulong targetId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null)
            {
                return string.Empty;
            }
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject obj) || obj == null)
            {
                return string.Empty;
            }
            if (obj.TryGetComponent<INameSource>(out var nameSource)
                && !string.IsNullOrWhiteSpace(nameSource.DisplayName))
            {
                return nameSource.DisplayName;
            }
            INameSource childSource = obj.GetComponentInChildren<INameSource>();
            if (childSource != null && !string.IsNullOrWhiteSpace(childSource.DisplayName))
            {
                return childSource.DisplayName;
            }
            return obj.name;
        }

        // -------------------------------------------------------------------------
        // Cast-Bar des Ziels (unter dem Portrait)
        // -------------------------------------------------------------------------

        /// <summary>Cast-Start des Ziels: Bar mit aktueller Zeit als Startpunkt einblenden.</summary>
        private void OnTargetCastStarted(int spellId, float durationSeconds)
            => BeginCastVisual(spellId, Time.unscaledTime, durationSeconds);

        /// <summary>Cast-Ende des Ziels (Abschluss/Abbruch/Interrupt): Bar ausblenden.</summary>
        private void OnTargetCastEnded() => HideCastBar();

        /// <summary>
        /// Blendet die Cast-Bar ein und startet den lokalen Fortschritts-Tick.
        /// <paramref name="startUnscaled"/> erlaubt das exakte Andocken an einen
        /// bereits laufenden Cast (spaetes Anvisieren).
        /// </summary>
        private void BeginCastVisual(int spellId, float startUnscaled, float durationSeconds)
        {
            if (m_CastRow == null || m_CastFill == null)
            {
                return;
            }
            m_CastStartUnscaled = startUnscaled;
            m_CastDurationSeconds = Mathf.Max(0.01f, durationSeconds);

            if (m_CastNameLabel != null)
            {
                m_CastNameLabel.text = ResolveSpellName(spellId);
            }
            m_CastRow.style.display = DisplayStyle.Flex;
            UpdateCastProgress();

            // UIToolkit-Scheduler statt Update()-Polling: ~60 Hz.
            if (m_CastTick == null)
            {
                m_CastTick = m_CastRow.schedule.Execute(UpdateCastProgress).Every(16);
            }
            else
            {
                m_CastTick.Resume();
            }
        }

        /// <summary>Stoppt den Fortschritts-Tick und versteckt die Cast-Bar.</summary>
        private void HideCastBar()
        {
            if (m_CastTick != null)
            {
                m_CastTick.Pause();
            }
            if (m_CastRow != null)
            {
                m_CastRow.style.display = DisplayStyle.None;
            }
        }

        /// <summary>Interpoliert den Cast-Fortschritt lokal aus Start + Dauer.</summary>
        private void UpdateCastProgress()
        {
            if (m_CastFill == null)
            {
                return;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - m_CastStartUnscaled) / m_CastDurationSeconds);
            m_CastFill.style.width = new StyleLength(new Length(t * 100f, LengthUnit.Percent));
        }

        /// <summary>Loest den Spell-Namen aus dem Katalog auf (Fallback: "Spell N").</summary>
        private static string ResolveSpellName(int spellId)
        {
            SpellTemplate template = SpellCatalogLoader.GetTemplateOrNull(spellId);
            if (template == null || string.IsNullOrWhiteSpace(template.Name))
            {
                return "Spell " + spellId;
            }
            return template.Name;
        }
    }
}
