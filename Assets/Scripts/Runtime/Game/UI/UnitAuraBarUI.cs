using System.Collections.Generic;
using Riftstorm.ApplicationLifecycle.UI;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Bind-Modus fuer <see cref="UnitAuraBarUI"/>: an den lokalen Spieler
    /// oder an das aktuell gelockte Target koppeln.
    /// </summary>
    public enum AuraBarBindMode
    {
        /// <summary>Zeigt die Auren der lokalen <see cref="UnitStats"/>.</summary>
        LocalPlayer = 0,
        /// <summary>Zeigt die Auren des aktuell ueber <see cref="TargetSelection"/> gelockten Ziels.</summary>
        CurrentTarget = 1,
    }

    /// <summary>
    /// WoW-/FLARE-Style Buff-/Debuff-Icon-Bar. Reagiert ausschliesslich auf
    /// <see cref="UnitStats.ClientAurasChanged"/>. Kein Polling fuer die
    /// Aura-Liste; nur die verbleibende Dauer pro Icon (Cooldown-Sweep) wird
    /// lokal pro Frame berechnet, weil sie monoton mit der Zeit laeuft und
    /// keinen Netcode pro Tick rechtfertigt.
    /// <para>
    /// Baut den Visual-Tree programmatisch &#8212; nur ein GameObject mit
    /// <see cref="UIDocument"/> + dieser Komponente noetig. Pro Frame wird
    /// genau eine Bind-Pruefung gemacht, bis der Owner (LocalPlayer oder
    /// <see cref="TargetSelection"/>) verfuegbar ist; danach laeuft alles
    /// eventbasiert.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class UnitAuraBarUI : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Tooltip("Quelle der Auren: LocalPlayer oder CurrentTarget.")]
        [SerializeField] private AuraBarBindMode m_BindMode = AuraBarBindMode.LocalPlayer;

        [Tooltip("Ankerpunkt der Icon-Reihe in absoluten Pixeln vom oberen linken Rand des Root-VisualElement.")]
        [SerializeField] private Vector2 m_AnchorTopLeft = new(40f, 110f);

        [Tooltip("Kantenlaenge eines Aura-Icons in Pixel.")]
        [SerializeField] private float m_IconSize = 36f;

        [Tooltip("Abstand zwischen zwei benachbarten Icons in Pixel.")]
        [SerializeField] private float m_IconSpacing = 4f;

        [Tooltip("Maximale Icon-Anzahl pro Reihe, bevor in eine zweite Reihe umgebrochen wird.")]
        [SerializeField] private int m_IconsPerRow = 16;

        [Tooltip("True = Icons rechtsbuendig (z. B. Target-Frame). False = linksbuendig (Player).")]
        [SerializeField] private bool m_RightAligned = false;

        // -------------------------------------------------------------------------
        // Konstanten
        // -------------------------------------------------------------------------

        private static readonly Color BuffBorderColor = new(0.85f, 0.78f, 0.45f, 1f);   // gold
        private static readonly Color DebuffBorderColor = new(0.85f, 0.30f, 0.28f, 1f); // rot
        private static readonly Color BuffFallbackFill = new(0.18f, 0.42f, 0.18f, 1f);
        private static readonly Color DebuffFallbackFill = new(0.45f, 0.12f, 0.12f, 1f);
        private static readonly Color SweepOverlayColor = new(0f, 0f, 0f, 0.55f);

        // -------------------------------------------------------------------------
        // Runtime
        // -------------------------------------------------------------------------

        private UIDocument m_Document;
        private VisualElement m_Root;
        private VisualElement m_Container;

        private readonly List<AuraIconView> m_IconPool = new();

        // Bind-Pfad: LocalPlayer
        private UnitStats m_BoundStats;

        // Bind-Pfad: CurrentTarget
        private TargetSelection m_LocalSelection;

        private IVisualElementScheduledItem m_SweepTimer;

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
            StopSweepTimer();
            DetachFromStats();
            DetachFromLocalSelection();
        }

        private void Update()
        {
            // Bind so lange pollen, bis NGO den Owner gespawnt hat. Danach
            // laeuft alles event-getrieben.
            if (m_BindMode == AuraBarBindMode.LocalPlayer)
            {
                if (m_BoundStats == null)
                {
                    TryBindLocalPlayer();
                }
            }
            else
            {
                if (m_LocalSelection == null)
                {
                    TryBindLocalSelection();
                }
            }
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
            m_Root.pickingMode = PickingMode.Ignore;

            m_Container = new VisualElement { name = "aura-bar-container" };
            m_Container.style.position = Position.Absolute;
            m_Container.style.top = m_AnchorTopLeft.y;
            if (m_RightAligned)
            {
                m_Container.style.right = m_AnchorTopLeft.x;
            }
            else
            {
                m_Container.style.left = m_AnchorTopLeft.x;
            }
            m_Container.style.flexDirection = FlexDirection.Row;
            m_Container.style.flexWrap = Wrap.Wrap;
            m_Container.style.maxWidth = m_IconsPerRow * (m_IconSize + m_IconSpacing);
            // Bei rechts-ausgerichtetem Container Icons von rechts nach links flowen.
            if (m_RightAligned)
            {
                m_Container.style.flexDirection = FlexDirection.RowReverse;
            }
            m_Container.pickingMode = PickingMode.Ignore;

            m_Root.Add(m_Container);
        }

        // -------------------------------------------------------------------------
        // Bind: Local Player
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
            AttachToStats(stats);
        }

        // -------------------------------------------------------------------------
        // Bind: Current Target
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
            // Initial-Sync auf den aktuell gelockten Target-Slot.
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
            DetachFromStats();
            HideAllIcons();

            if (current == TargetSelection.NoTarget)
            {
                return;
            }
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null) { return; }
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(current, out NetworkObject no) || no == null)
            {
                return;
            }
            UnitStats stats = no.GetComponent<UnitStats>();
            if (stats == null)
            {
                stats = no.GetComponentInChildren<UnitStats>();
            }
            if (stats == null)
            {
                return;
            }
            AttachToStats(stats);
        }

        // -------------------------------------------------------------------------
        // Stats-Abo
        // -------------------------------------------------------------------------

        private void AttachToStats(UnitStats stats)
        {
            m_BoundStats = stats;
            m_BoundStats.ClientAurasChanged += OnClientAurasChanged;
            // Initial-Snapshot rendern (kann leer sein).
            RebuildIcons();
            EnsureSweepTimerRunning();
        }

        private void DetachFromStats()
        {
            if (m_BoundStats != null)
            {
                m_BoundStats.ClientAurasChanged -= OnClientAurasChanged;
                m_BoundStats = null;
            }
        }

        private void OnClientAurasChanged()
        {
            RebuildIcons();
        }

        // -------------------------------------------------------------------------
        // Icon-Rendering
        // -------------------------------------------------------------------------

        private void HideAllIcons()
        {
            for (int i = 0; i < m_IconPool.Count; i++)
            {
                m_IconPool[i].Root.style.display = DisplayStyle.None;
                m_IconPool[i].SpellEntry = 0;
            }
        }

        private void RebuildIcons()
        {
            if (m_Container == null)
            {
                return;
            }
            if (m_BoundStats == null)
            {
                HideAllIcons();
                return;
            }

            IReadOnlyList<UnitStats.AuraSnapshot> auras = m_BoundStats.ClientAuras;
            int count = auras.Count;

            // Pool wachsen lassen, niemals destruieren (kein GC-Churn beim Refresh).
            while (m_IconPool.Count < count)
            {
                AuraIconView v = CreateIconView();
                m_Container.Add(v.Root);
                m_IconPool.Add(v);
            }

            for (int i = 0; i < m_IconPool.Count; i++)
            {
                AuraIconView view = m_IconPool[i];
                if (i >= count)
                {
                    view.Root.style.display = DisplayStyle.None;
                    view.SpellEntry = 0;
                    continue;
                }
                ApplySnapshotToView(view, auras[i]);
            }
        }

        private void ApplySnapshotToView(AuraIconView view, UnitStats.AuraSnapshot snap)
        {
            view.Root.style.display = DisplayStyle.Flex;
            view.SpellEntry = snap.SpellEntry;
            view.MaxDurationMs = snap.MaxDurationMs;
            view.RemainingMsAtReceive = snap.RemainingMs;
            view.ReceivedAt = snap.ReceivedAt;
            view.IsPositive = snap.IsPositive;

            SpellTemplate template = SpellCatalogLoader.GetTemplateOrNull(snap.SpellEntry);
            Texture2D icon = template != null ? HudConfigLoader.LoadTextureOrNull(NormalizeSpellIconKey(template.Icon)) : null;

            // Icon-Bild: Textur wenn vorhanden, sonst Fallback-Farbflaeche.
            if (icon != null)
            {
                view.IconImage.style.backgroundImage = new StyleBackground(icon);
                view.IconImage.style.backgroundColor = new StyleColor(StyleKeyword.Initial);
            }
            else
            {
                view.IconImage.style.backgroundImage = StyleKeyword.Null;
                view.IconImage.style.backgroundColor = snap.IsPositive ? BuffFallbackFill : DebuffFallbackFill;
            }

            // Border-Farbe Buff vs Debuff.
            Color border = snap.IsPositive ? BuffBorderColor : DebuffBorderColor;
            view.Root.style.borderTopColor = border;
            view.Root.style.borderBottomColor = border;
            view.Root.style.borderLeftColor = border;
            view.Root.style.borderRightColor = border;

            // Stack-Label.
            if (snap.Stacks > 1)
            {
                view.StackLabel.style.display = DisplayStyle.Flex;
                view.StackLabel.text = snap.Stacks.ToString();
            }
            else
            {
                view.StackLabel.style.display = DisplayStyle.None;
            }

            // Sofortiges Sweep-Update fuer den frischen Snapshot.
            UpdateSweep(view, Time.unscaledTime);
        }

        /// <summary>
        /// Normalisiert einen <see cref="SpellTemplate.Icon"/>-Wert auf einen
        /// <see cref="HudConfigLoader.LoadTextureOrNull"/>-kompatiblen Key:
        /// strippt die Datei-Extension und stellt <c>spell_icons/</c> voran,
        /// falls der Wert keinen Verzeichnis-Separator enthaelt. SpellTemplate
        /// speichert blanke Dateinamen (z. B. <c>"Whirlwind.png"</c>); die
        /// physischen Dateien liegen unter <c>Assets/Art/spell_icons/</c>.
        /// </summary>
        private static string NormalizeSpellIconKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
            string normalized = raw.Replace('\\', '/');
            int dot = normalized.LastIndexOf('.');
            int slash = normalized.LastIndexOf('/');
            if (dot > slash)
            {
                normalized = normalized[..dot];
            }
            if (normalized.IndexOf('/') < 0)
            {
                normalized = "spell_icons/" + normalized;
            }
            return normalized;
        }

        private AuraIconView CreateIconView()
        {
            VisualElement root = new() { name = "aura-icon" };
            root.style.width = m_IconSize;
            root.style.height = m_IconSize;
            root.style.marginRight = m_IconSpacing;
            root.style.marginBottom = m_IconSpacing;
            root.style.borderTopWidth = 2f;
            root.style.borderBottomWidth = 2f;
            root.style.borderLeftWidth = 2f;
            root.style.borderRightWidth = 2f;
            root.style.borderTopLeftRadius = 4f;
            root.style.borderTopRightRadius = 4f;
            root.style.borderBottomLeftRadius = 4f;
            root.style.borderBottomRightRadius = 4f;
            root.style.overflow = Overflow.Hidden;
            root.pickingMode = PickingMode.Ignore;

            VisualElement iconImage = new() { name = "aura-icon-image" };
            iconImage.style.position = Position.Absolute;
            iconImage.style.left = 0;
            iconImage.style.right = 0;
            iconImage.style.top = 0;
            iconImage.style.bottom = 0;
            iconImage.pickingMode = PickingMode.Ignore;
            root.Add(iconImage);

            // Cooldown-Sweep: dunkler Overlay, dessen Hoehe in Prozent
            // skaliert wird. Faellt von oben nach unten (volle Hoehe = "frisch",
            // 0 = abgelaufen).
            VisualElement sweep = new() { name = "aura-icon-sweep" };
            sweep.style.position = Position.Absolute;
            sweep.style.left = 0;
            sweep.style.right = 0;
            sweep.style.top = 0;
            sweep.style.height = new StyleLength(new Length(0f, LengthUnit.Percent));
            sweep.style.backgroundColor = SweepOverlayColor;
            sweep.pickingMode = PickingMode.Ignore;
            root.Add(sweep);

            Label durationLabel = new() { name = "aura-icon-duration", text = string.Empty };
            durationLabel.style.position = Position.Absolute;
            durationLabel.style.left = 0;
            durationLabel.style.right = 0;
            durationLabel.style.bottom = 1f;
            durationLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            durationLabel.style.color = Color.white;
            durationLabel.style.fontSize = 11;
            durationLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            durationLabel.style.textShadow = new TextShadow
            {
                offset = new Vector2(1f, 1f),
                blurRadius = 1f,
                color = new Color(0f, 0f, 0f, 0.9f),
            };
            UIFonts.Apply(durationLabel, UIFonts.Numeric);
            durationLabel.pickingMode = PickingMode.Ignore;
            root.Add(durationLabel);

            Label stackLabel = new() { name = "aura-icon-stacks", text = string.Empty };
            stackLabel.style.position = Position.Absolute;
            stackLabel.style.right = 2f;
            stackLabel.style.bottom = 14f;
            stackLabel.style.color = new Color(1f, 0.95f, 0.55f, 1f);
            stackLabel.style.fontSize = 12;
            stackLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            stackLabel.style.textShadow = new TextShadow
            {
                offset = new Vector2(1f, 1f),
                blurRadius = 1f,
                color = new Color(0f, 0f, 0f, 0.9f),
            };
            UIFonts.Apply(stackLabel, UIFonts.Numeric);
            stackLabel.style.display = DisplayStyle.None;
            stackLabel.pickingMode = PickingMode.Ignore;
            root.Add(stackLabel);

            return new AuraIconView
            {
                Root = root,
                IconImage = iconImage,
                Sweep = sweep,
                DurationLabel = durationLabel,
                StackLabel = stackLabel,
            };
        }

        // -------------------------------------------------------------------------
        // Cooldown-Sweep (lokaler Tick, kein Netcode)
        // -------------------------------------------------------------------------

        private void EnsureSweepTimerRunning()
        {
            if (m_SweepTimer != null || m_Container == null)
            {
                return;
            }
            // ~30 Hz reicht fuer einen weichen Sweep ohne sichtbares Treppen-Tick.
            m_SweepTimer = m_Container.schedule.Execute(TickSweep).Every(33);
        }

        private void StopSweepTimer()
        {
            if (m_SweepTimer != null)
            {
                m_SweepTimer.Pause();
                m_SweepTimer = null;
            }
        }

        private void TickSweep()
        {
            if (m_IconPool.Count == 0)
            {
                return;
            }
            float now = Time.unscaledTime;
            for (int i = 0; i < m_IconPool.Count; i++)
            {
                AuraIconView v = m_IconPool[i];
                if (v.Root.style.display == DisplayStyle.None) { continue; }
                UpdateSweep(v, now);
            }
        }

        private static void UpdateSweep(AuraIconView v, float now)
        {
            // Permanente Aura: kein Sweep, kein Duration-Label.
            if (v.MaxDurationMs <= 0 || v.RemainingMsAtReceive < 0)
            {
                v.Sweep.style.height = new StyleLength(new Length(0f, LengthUnit.Percent));
                v.DurationLabel.text = string.Empty;
                return;
            }

            float elapsedSinceReceive = (now - v.ReceivedAt) * 1000f;
            float remainingMs = Mathf.Max(0f, v.RemainingMsAtReceive - elapsedSinceReceive);
            float pct = v.MaxDurationMs > 0 ? Mathf.Clamp01(remainingMs / v.MaxDurationMs) : 0f;
            // Sweep faellt von oben (volle Hoehe = frisch, 0 = abgelaufen).
            float overlayPct = (1f - pct) * 100f;
            v.Sweep.style.height = new StyleLength(new Length(overlayPct, LengthUnit.Percent));

            // Restzeit-Label: nur in den letzten 60s anzeigen, sonst zu unruhig.
            float remainingSec = remainingMs / 1000f;
            if (remainingSec >= 60f)
            {
                v.DurationLabel.text = Mathf.CeilToInt(remainingSec / 60f) + "m";
            }
            else if (remainingSec >= 10f)
            {
                v.DurationLabel.text = Mathf.CeilToInt(remainingSec).ToString();
            }
            else if (remainingSec > 0f)
            {
                v.DurationLabel.text = remainingSec.ToString("0.0");
            }
            else
            {
                v.DurationLabel.text = string.Empty;
            }
        }

        // -------------------------------------------------------------------------
        // Sub-View Datenklasse
        // -------------------------------------------------------------------------

        private sealed class AuraIconView
        {
            public VisualElement Root;
            public VisualElement IconImage;
            public VisualElement Sweep;
            public Label DurationLabel;
            public Label StackLabel;

            public int SpellEntry;
            public int MaxDurationMs;
            public int RemainingMsAtReceive;
            public float ReceivedAt;
            public bool IsPositive;
        }
    }
}
