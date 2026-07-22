using Riftstorm.Management.FontManagement;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Owner-only CastBar HUD. Wird vom <see cref="PlayerCombat.OwnerCastStarted"/>-
    /// und <see cref="PlayerCombat.OwnerCastEnded"/>-Event gefuettert, die wiederum
    /// vom Server per ClientRpc gesetzt werden. Zeigt waehrend eines laufenden
    /// Casts den Spell-Namen und einen progressiven Fill-Balken; verschwindet
    /// sofort nach Cast-Ende (egal ob erfolgreich oder durch Move/Tod
    /// unterbrochen).
    ///
    /// <para>
    /// Bind-Pattern und Visual-Tree-Aufbau analog zu <see cref="PlayerFrameUI"/>:
    /// <see cref="Update"/> sucht nur so lange den LocalPlayer, bis NGO ihn
    /// gespawnt hat; danach laufen Show/Hide eventbasiert. Die Progress-
    /// Animation nutzt den UIToolkit-<see cref="IVisualElementScheduledItem"/>-
    /// Scheduler statt einer eigenen Update-Schleife.
    /// </para>
    /// <para>
    /// Visual-Tree wird komplett programmatisch gebaut — es muss kein
    /// UXML-Asset zugewiesen werden. Es reicht ein GameObject mit
    /// <see cref="UIDocument"/> plus dieser Komponente in der Game-Scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CastBarHUD : MonoBehaviour
    {
        private UIDocument m_Document;
        private HudConfig m_Config;
        private Texture2D m_BackgroundTexture;
        private Texture2D m_FillTexture;

        // Visual-Tree
        private VisualElement m_Root;
        private VisualElement m_Container;
        private VisualElement m_Fill;
        private Label m_NameLabel;
        private Label m_PercentLabel;
        private IVisualElementScheduledItem m_Tick;

        // Bindungen
        private PlayerCombat m_BoundCombat;

        // Cast-Daten (nur waehrend eines aktiven Casts gueltig)
        private float m_CastStartUnscaled;
        private float m_CastDurationSeconds;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
            m_Config = HudConfigLoader.Load();
            m_BackgroundTexture = HudConfigLoader.LoadTextureOrNull(m_Config.castBarBackgroundTexture);
            m_FillTexture = HudConfigLoader.LoadTextureOrNull(m_Config.castBarFillTexture);
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
            if (m_BoundCombat != null)
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
            // Wichtig: rootVisualElement KEIN Clear() — andere HUD-Komponenten
            // (PlayerFrameUI, TargetFrameUI, ActionBarHUD) teilen sich denselben
            // UIDocument-Root in der Game-Scene NICHT, daher waere Clear() hier
            // theoretisch okay, aber wir bleiben defensiv: CastBarHUD lebt auf
            // einem eigenen GameObject mit eigenem UIDocument.

            HudConfig c = m_Config;

            m_Container = HudStyle.BuildTexturedBar(
                "castbar",
                m_FillTexture,
                c.castBarWidth,
                c.castBarHeight,
                fillFromRight: false,
                out m_Fill,
                out Label valueLabel);

            // Hintergrund-Textur (leerer Rahmen) hinter dem Fill.
            if (m_BackgroundTexture != null)
            {
                m_Container.style.backgroundImage = new StyleBackground(m_BackgroundTexture);
            }

            // Default value-Label (HP-Style "0 / 0") als Prozent-Anzeige rechts
            // umfunktionieren — zeigt die verbleibenden Prozent bis zum Cast-Ende.
            m_PercentLabel = valueLabel;
            m_PercentLabel.text = "100%";
            m_PercentLabel.style.left = StyleKeyword.Auto;
            m_PercentLabel.style.right = 6f;
            m_PercentLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            m_PercentLabel.style.fontSize = c.castBarPercentFontSize;

            // Container unten zentriert, ueber der Action-Bar.
            m_Container.style.position = Position.Absolute;
            m_Container.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            m_Container.style.bottom = c.castBarBottomMargin;
            m_Container.style.translate = new StyleTranslate(new Translate(
                new Length(-50f, LengthUnit.Percent),
                new Length(0f, LengthUnit.Pixel)));

            // Spell-Name-Label (mittig, ueber dem Fill).
            m_NameLabel = new Label(string.Empty) { name = "castbar-name" };
            m_NameLabel.style.position = Position.Absolute;
            m_NameLabel.style.left = 6f;
            m_NameLabel.style.right = 48f;
            m_NameLabel.style.top = 0f;
            m_NameLabel.style.bottom = 0f;
            m_NameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_NameLabel.style.color = Color.white;
            m_NameLabel.style.fontSize = c.castBarNameFontSize;
            m_NameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            UIFonts.Apply(m_NameLabel, UIFonts.Heading);
            m_Container.Add(m_NameLabel);

            // Default: ausgeblendet. Wird nur waehrend eines Casts sichtbar.
            m_Container.style.display = DisplayStyle.None;
            m_Container.pickingMode = PickingMode.Ignore;

            m_Root.Add(m_Container);
        }

        // -------------------------------------------------------------------------
        // LocalPlayer-Binding
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

            if (!playerObj.TryGetComponent<PlayerCombat>(out var combat))
            {
                combat = playerObj.GetComponentInChildren<PlayerCombat>();
            }
            if (combat == null)
            {
                return;
            }

            AttachToLocalPlayer(combat);
        }

        private void AttachToLocalPlayer(PlayerCombat combat)
        {
            m_BoundCombat = combat;
            m_BoundCombat.OwnerCastStarted += OnOwnerCastStarted;
            m_BoundCombat.OwnerCastEnded += OnOwnerCastEnded;
        }

        private void DetachFromLocalPlayer()
        {
            if (m_BoundCombat != null)
            {
                m_BoundCombat.OwnerCastStarted -= OnOwnerCastStarted;
                m_BoundCombat.OwnerCastEnded -= OnOwnerCastEnded;
                m_BoundCombat = null;
            }
            StopTick();
            if (m_Container != null)
            {
                m_Container.style.display = DisplayStyle.None;
            }
        }

        // -------------------------------------------------------------------------
        // Event-Handler
        // -------------------------------------------------------------------------

        private void OnOwnerCastStarted(int spellEntry, float durationSeconds)
        {
            if (m_Container == null || m_Fill == null)
            {
                return;
            }
            m_CastStartUnscaled = Time.unscaledTime;
            m_CastDurationSeconds = Mathf.Max(0.01f, durationSeconds);

            if (m_NameLabel != null)
            {
                m_NameLabel.text = ResolveSpellName(spellEntry);
            }

            // Fill auf 0% setzen und Container sichtbar machen.
            m_Fill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            if (m_PercentLabel != null)
            {
                m_PercentLabel.text = "100%";
            }
            m_Container.style.display = DisplayStyle.Flex;

            // UIToolkit-Scheduler statt Update()-Polling: ~60 Hz Progress-Update.
            if (m_Tick == null)
            {
                m_Tick = m_Container.schedule.Execute(UpdateProgress).Every(16);
            }
            else
            {
                m_Tick.Resume();
            }
        }

        private void OnOwnerCastEnded(bool completed)
        {
            StopTick();
            if (m_Container != null)
            {
                m_Container.style.display = DisplayStyle.None;
            }
        }

        private void StopTick()
        {
            if (m_Tick != null)
            {
                m_Tick.Pause();
            }
        }

        private void UpdateProgress()
        {
            if (m_Fill == null)
            {
                return;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - m_CastStartUnscaled) / m_CastDurationSeconds);
            m_Fill.style.width = new StyleLength(new Length(t * 100f, LengthUnit.Percent));
            if (m_PercentLabel != null)
            {
                int remaining = Mathf.CeilToInt((1f - t) * 100f);
                m_PercentLabel.text = remaining + "%";
            }
        }

        private static string ResolveSpellName(int spellEntry)
        {
            SpellTemplate template = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (template == null || string.IsNullOrWhiteSpace(template.Name))
            {
                return "Spell " + spellEntry;
            }
            return template.Name;
        }
    }
}
