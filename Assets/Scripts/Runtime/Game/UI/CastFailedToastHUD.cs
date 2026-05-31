using Riftstorm.ApplicationLifecycle.UI;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Owner-only Screen-Toast f&uuml;r abgelehnte Casts. Wird vom
    /// <see cref="PlayerCombat.OwnerCastFailed"/>-Event gef&uuml;ttert, das der
    /// Server &uuml;ber einen Target-only ClientRpc nur an den Cast-anfordernden
    /// Spieler schickt. Mapped den <see cref="CastResult"/>-Code &uuml;ber
    /// <see cref="CastResultStrings.Get"/> auf den deutschen UI-String (z. B.
    /// "Spell auf Cooldown.", "Ziel ist zu weit weg.", "Ziel ist verb&uuml;ndet.",
    /// "Nicht genug Mana.") und blendet ihn kurz oberhalb der ActionBar ein.
    ///
    /// <para>
    /// Bind-Pattern und Visual-Tree-Aufbau analog zu <see cref="CastBarHUD"/>:
    /// <see cref="Update"/> sucht nur so lange den LocalPlayer, bis NGO ihn
    /// gespawnt hat; danach lebt das HUD eventbasiert. Visual-Tree wird
    /// komplett programmatisch gebaut &#8212; es muss kein UXML-Asset
    /// zugewiesen werden. Es reicht ein GameObject mit <see cref="UIDocument"/>
    /// plus dieser Komponente in der Game-Scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CastFailedToastHUD : MonoBehaviour
    {
        /// <summary>Sichtbarkeitsdauer in Sekunden bevor das Fade-Out startet.</summary>
        private const float VisibleDurationSeconds = 1.6f;

        /// <summary>Fade-Out-Dauer in Sekunden.</summary>
        private const float FadeDurationSeconds = 0.4f;

        private UIDocument m_Document;
        private VisualElement m_Root;
        private Label m_Label;
        private IVisualElementScheduledItem m_HideTick;
        private IVisualElementScheduledItem m_FadeTick;
        private float m_FadeStartUnscaled;

        private PlayerCombat m_BoundCombat;

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

            m_Label = new Label
            {
                name = "cast-failed-toast",
                text = string.Empty,
            };
            // Frei platzieren ueber dem ActionBar-Bereich; Mitte unten.
            m_Label.style.position = Position.Absolute;
            m_Label.style.left = 0f;
            m_Label.style.right = 0f;
            m_Label.style.bottom = 220f;
            m_Label.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_Label.style.color = new StyleColor(new Color(1f, 0.32f, 0.28f, 1f));
            m_Label.style.fontSize = 22f;
            m_Label.style.unityFontStyleAndWeight = FontStyle.Bold;
            // Leichter Drop-Shadow ueber TextShadow (verbessert Lesbarkeit auf
            // hellen Hintergruenden wie Sand-Boden).
            m_Label.style.textShadow = new StyleTextShadow(new TextShadow
            {
                offset = new Vector2(1f, 1f),
                blurRadius = 2f,
                color = new Color(0f, 0f, 0f, 0.85f),
            });
            UIFonts.Apply(m_Label, UIFonts.Body);

            // Initial unsichtbar &#8212; wird nur eingeblendet, wenn ein Fehler kommt.
            m_Label.style.opacity = 0f;
            m_Label.pickingMode = PickingMode.Ignore;

            m_Root.Add(m_Label);
        }

        // -------------------------------------------------------------------------
        // Bind / Unbind
        // -------------------------------------------------------------------------

        private void TryBindLocalPlayer()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient || nm.LocalClient == null)
            {
                return;
            }
            NetworkObject po = nm.LocalClient.PlayerObject;
            if (po == null)
            {
                return;
            }
            if (!po.TryGetComponent<PlayerCombat>(out var combat))
            {
                return;
            }
            m_BoundCombat = combat;
            m_BoundCombat.OwnerCastFailed += OnOwnerCastFailed;
        }

        private void DetachFromLocalPlayer()
        {
            if (m_BoundCombat != null)
            {
                m_BoundCombat.OwnerCastFailed -= OnOwnerCastFailed;
                m_BoundCombat = null;
            }
            m_HideTick?.Pause();
            m_HideTick = null;
            m_FadeTick?.Pause();
            m_FadeTick = null;
        }

        // -------------------------------------------------------------------------
        // Toast-Logik
        // -------------------------------------------------------------------------

        private void OnOwnerCastFailed(CastResult result)
        {
            if (m_Label == null || result == CastResult.Success)
            {
                return;
            }

            m_Label.text = CastResultStrings.Get(result);
            m_Label.style.opacity = 1f;

            // Vorherige Timer canceln, falls ein neuer Fehler waehrend des
            // Fade-Outs eintrifft (Spam-Klicker bei Cooldown).
            m_HideTick?.Pause();
            m_FadeTick?.Pause();

            m_HideTick = m_Label.schedule.Execute(StartFadeOut)
                .StartingIn((long)(VisibleDurationSeconds * 1000f));
        }

        private void StartFadeOut()
        {
            m_FadeStartUnscaled = Time.unscaledTime;
            m_FadeTick = m_Label.schedule.Execute(TickFade).Every(16);
        }

        private void TickFade()
        {
            float elapsed = Time.unscaledTime - m_FadeStartUnscaled;
            float t = Mathf.Clamp01(elapsed / FadeDurationSeconds);
            m_Label.style.opacity = 1f - t;
            if (t >= 1f)
            {
                m_FadeTick?.Pause();
                m_FadeTick = null;
            }
        }
    }
}
