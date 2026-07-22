using Riftstorm.Management.FontManagement;
using Riftstorm.Game.Spells;
using Riftstorm.Game.UI;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Client-seitiger Cast-Zustand eines NPCs. Wird vom
    /// <see cref="NpcController"/> per ClientRpc gestartet/beendet und h&#228;lt
    /// w&#228;hrend eines laufenden Cast-Time-Spells Fortschritt, Spell-Namen und
    /// die Balken-Texturen bereit. Gezeichnet wird die Bar NICHT hier, sondern
    /// vom co-lokalen <see cref="Player.UnitNameTag"/> direkt unter
    /// der Overhead-HP-Bar (gemeinsames <c>nameRect</c>), damit die Cast-Bar nie
    /// gegen&#252;ber Name und HP-Bar verrutscht.
    ///
    /// <para>
    /// Reine Anzeige-/Datenkomponente: Der Fortschritt wird lokal aus
    /// <see cref="Time.unscaledTime"/> interpoliert (kein Netzwerk-Polling, keine
    /// Coroutine, kein Timer). Begin/End werden ausschlie&#223;lich event-getrieben
    /// vom Server gesetzt.
    /// </para>
    ///
    /// <para>
    /// Die Komponente wird zur Laufzeit von <see cref="NpcController"/> per
    /// <c>AddComponent</c> auf Clients erg&#228;nzt, damit keine Prefab-&#196;nderung
    /// n&#246;tig ist.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcCastBarView : MonoBehaviour
    {
        private bool m_Active;
        private float m_CastStartUnscaled;
        private float m_CastDurationSeconds;
        private string m_SpellName = string.Empty;

        private GUIStyle m_NameStyle;
        private Texture2D m_BackgroundTexture;
        private Texture2D m_FillTexture;

        // Ownership-Flags: TextureManager-Texturen (interface/castbar*) sind
        // geteilt und duerfen in OnDestroy NICHT zerstoert werden. Nur die als
        // Fallback selbst erzeugten 1x1-Volltexturen gehoeren uns.
        private bool m_OwnsBackgroundTexture;
        private bool m_OwnsFillTexture;
        private bool m_ResourcesResolved;

        /// <summary>
        /// Startet die Cast-Bar f&#252;r einen Cast-Time-Spell. Der Spell-Name wird
        /// lokal aus dem <see cref="SpellCatalogLoader"/> aufgel&#246;st.
        /// </summary>
        /// <param name="spellId">Katalog-ID des gecasteten Spells.</param>
        /// <param name="durationSeconds">Gesamte Cast-Zeit in Sekunden.</param>
        public void Begin(int spellId, float durationSeconds)
        {
            m_Active = true;
            m_CastStartUnscaled = Time.unscaledTime;
            m_CastDurationSeconds = Mathf.Max(0.01f, durationSeconds);
            m_SpellName = ResolveSpellName(spellId);
        }

        /// <summary>Blendet die Cast-Bar aus (Abschluss, Abbruch oder Interrupt).</summary>
        public void End()
        {
            m_Active = false;
        }

        /// <summary>
        /// Liefert die aktuellen Cast-Zeichen-Daten, falls gerade ein Cast l&#228;uft.
        /// Wird vom co-lokalen <see cref="Player.UnitNameTag"/> in
        /// dessen <c>OnGUI</c>-Pass aufgerufen, damit die Cast-Bar exakt unter der
        /// HP-Bar gezeichnet wird und nicht durch eine separate
        /// World-to-Screen-Berechnung verrutscht. F&#252;hrt das Auto-Hide aus, sobald
        /// die Cast-Zeit abgelaufen ist (falls das End-Event verloren ginge).
        /// </summary>
        /// <param name="progress01">Cast-Fortschritt 0..1.</param>
        /// <param name="spellName">Aufgel&#246;ster Spell-Name.</param>
        /// <param name="background">Hintergrund-Textur des Balkens.</param>
        /// <param name="fill">Fill-Textur des Balkens.</param>
        /// <param name="nameStyle">GUIStyle f&#252;r den Spell-Namen.</param>
        /// <returns>True, wenn ein Cast aktiv ist und gezeichnet werden soll.</returns>
        public bool TryGetActiveCast(
            out float progress01,
            out string spellName,
            out Texture2D background,
            out Texture2D fill,
            out GUIStyle nameStyle)
        {
            progress01 = 0f;
            spellName = string.Empty;
            background = null;
            fill = null;
            nameStyle = null;

            if (!m_Active)
            {
                return false;
            }

            // Auto-Hide, falls das End-Event verloren geht: nach Ablauf der
            // Cast-Zeit verschwindet die Bar in jedem Fall.
            float elapsed = Time.unscaledTime - m_CastStartUnscaled;
            float t = Mathf.Clamp01(elapsed / m_CastDurationSeconds);
            if (t >= 1f)
            {
                m_Active = false;
                return false;
            }

            EnsureResources();

            progress01 = t;
            spellName = m_SpellName;
            background = m_BackgroundTexture;
            fill = m_FillTexture;
            nameStyle = m_NameStyle;
            return true;
        }

        private void EnsureResources()
        {
            if (!m_ResourcesResolved)
            {
                // Datengetriebene Cast-Bar-Texturen (interface/castbar +
                // interface/castbar_fill) aus der HUD-Config, identisch zur
                // Owner-/Target-Cast-Bar. Fallback auf 1x1-Volltexturen nur, wenn
                // die konfigurierte Textur fehlt.
                HudConfig cfg = HudConfigLoader.Load();
                m_BackgroundTexture = HudConfigLoader.LoadTextureOrNull(cfg.castBarBackgroundTexture);
                if (m_BackgroundTexture == null)
                {
                    m_BackgroundTexture = MakeSolidTexture(new Color(0f, 0f, 0f, 0.65f));
                    m_OwnsBackgroundTexture = true;
                }
                m_FillTexture = HudConfigLoader.LoadTextureOrNull(cfg.castBarFillTexture);
                if (m_FillTexture == null)
                {
                    m_FillTexture = MakeSolidTexture(new Color(0.95f, 0.82f, 0.25f, 0.9f));
                    m_OwnsFillTexture = true;
                }
                m_ResourcesResolved = true;
            }
            if (m_NameStyle == null)
            {
                m_NameStyle = new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                };
                Font font = UIFonts.Small;
                if (font != null)
                {
                    m_NameStyle.font = font;
                }
                m_NameStyle.normal.textColor = Color.white;
                // Alle IMGUI-State-Slots fixieren, damit GUI.Label bei Mouse-Over
                // nicht auf einen abweichenden Hover-State umschaltet.
                m_NameStyle.hover = m_NameStyle.normal;
                m_NameStyle.active = m_NameStyle.normal;
                m_NameStyle.focused = m_NameStyle.normal;
                m_NameStyle.onNormal = m_NameStyle.normal;
                m_NameStyle.onHover = m_NameStyle.normal;
                m_NameStyle.onActive = m_NameStyle.normal;
                m_NameStyle.onFocused = m_NameStyle.normal;
            }
        }

        /// <summary>Erzeugt eine 1x1-Volltextur f&#252;r IMGUI-Balkenfl&#228;chen.</summary>
        private static Texture2D MakeSolidTexture(Color color)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            // Nur selbst erzeugte Fallback-Texturen zerstoeren. Geteilte
            // TextureManager-Texturen bleiben unangetastet.
            if (m_OwnsBackgroundTexture && m_BackgroundTexture != null)
            {
                Destroy(m_BackgroundTexture);
            }
            if (m_OwnsFillTexture && m_FillTexture != null)
            {
                Destroy(m_FillTexture);
            }
        }

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
