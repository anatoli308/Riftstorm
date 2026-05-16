using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Lokal-visuelles Hover-Feedback: faerbt alle <see cref="SpriteRenderer"/>
    /// einer Einheit rot, solange der Owner-Client mit der Maus drueberfaehrt.
    ///
    /// <para>
    /// Source-Aequivalent: <c>ClientGameObj.cpp</c> setzt bei <c>isMousedOver()</c>
    /// einen Brightness-Boost (<c>brightenPct = 0.1f</c>) auf das gesamte Sprite-
    /// Modell. Hier zusaetzlich rot eingefaerbt nach Nutzer-Wunsch — multiplikativ
    /// auf die Original-<see cref="SpriteRenderer.color"/>, damit der Effekt auch
    /// auf gefaerbten Skin-Sprites sichtbar bleibt.
    /// </para>
    /// <para>
    /// Rein client-lokal, keine Netcode-Synchronisation. Wird ausschliesslich von
    /// <see cref="PlayerTargetingInput"/> getoggelt — keine eigene Polling-Logik.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoverHighlight : MonoBehaviour
    {
        [Tooltip("Multiplikativer Tint auf alle SpriteRenderer-Farben, solange Hover aktiv " +
                 "ist. Werte > 1 brennen das Bild auf (Glow-Effekt). RGB getrennt einstellbar, " +
                 "Default ist heller Rot-Boost.")]
        [SerializeField] private Color m_HoverTint = new(1.6f, 0.4f, 0.4f, 1f);

        private SpriteRenderer[] m_SpriteRenderers;
        private Color[] m_OriginalColors;
        private bool m_Hovered;

        private void Awake()
        {
            CacheRenderers();
        }

        private void CacheRenderers()
        {
            m_SpriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            m_OriginalColors = new Color[m_SpriteRenderers.Length];
            for (int i = 0; i < m_SpriteRenderers.Length; i++)
            {
                m_OriginalColors[i] = m_SpriteRenderers[i] != null ? m_SpriteRenderers[i].color : Color.white;
            }
        }

        /// <summary>
        /// Erneuert den Renderer-Cache. Aufrufen, wenn die Sprite-Hierarchie zur
        /// Laufzeit ausgetauscht wird (z. B. nach Skin-Wechsel). Der bisherige
        /// Hover-Zustand wird zurueckgesetzt, damit kein Tint auf neuen Renderern
        /// "haengen" bleibt. Kein Polling — der Aufrufer signalisiert die Aenderung explizit.
        /// </summary>
        public void RefreshRenderers()
        {
            if (m_Hovered)
            {
                SetHovered(false);
            }
            CacheRenderers();
        }

        /// <summary>
        /// Schaltet den Hover-Tint hart ein oder aus. Idempotent — doppelte Aufrufe
        /// (z. B. mehrere Frames mit demselben Hover-Status) sind kostenfrei.
        /// </summary>
        public void SetHovered(bool hovered)
        {
            if (m_Hovered == hovered)
            {
                return;
            }
            m_Hovered = hovered;
            // Self-heal: FLARE-Sprites werden vom GamePlayerBootstrap async erst NACH unserem
            // Awake erzeugt — der initiale Cache ist dann leer. Bei jedem Toggle prüfen und
            // bei Bedarf neu cachen. Kein Polling: das passiert nur beim Hover-Wechsel.
            if (hovered && (m_SpriteRenderers == null || m_SpriteRenderers.Length == 0))
            {
                CacheRenderers();
            }
            if (m_SpriteRenderers == null)
            {
                return;
            }
            for (int i = 0; i < m_SpriteRenderers.Length; i++)
            {
                SpriteRenderer sr = m_SpriteRenderers[i];
                if (sr == null)
                {
                    continue;
                }
                if (hovered)
                {
                    Color orig = m_OriginalColors[i];
                    sr.color = new Color(
                        orig.r * m_HoverTint.r,
                        orig.g * m_HoverTint.g,
                        orig.b * m_HoverTint.b,
                        orig.a * m_HoverTint.a);
                }
                else
                {
                    sr.color = m_OriginalColors[i];
                }
            }
        }

        private void OnDisable()
        {
            // Sichergehen, dass beim Despawn / Disable kein Sprite rot eingefroren bleibt.
            if (m_Hovered)
            {
                SetHovered(false);
            }
        }
    }
}
