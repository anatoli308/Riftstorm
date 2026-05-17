using UnityEngine;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Statischer Service fuer den Hardware-Cursor. Tauscht zwischen Default-
    /// und Attack-Cursor je nach Hover-Zustand. Texturen + Hotspot stammen aus
    /// <see cref="HudConfig"/> (JSON-konfigurierbar) und werden ueber den
    /// <c>TextureManager</c> (via <see cref="HudConfigLoader"/>) aufgeloest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bewusst statisch: Unitys <see cref="Cursor.SetCursor(Texture2D, Vector2, CursorMode)"/>
    /// ist selbst global. Ein zusaetzliches Singleton-MonoBehaviour wuerde
    /// keinen Mehrwert bieten. Aufrufer (z. B. <c>PlayerTargetingInput</c>)
    /// rufen ausschliesslich <see cref="SetAttack(bool)"/> idempotent auf —
    /// kein Polling.
    /// </para>
    /// <para>
    /// Lazy Init: Beim ersten <see cref="SetAttack(bool)"/>-Aufruf werden die
    /// Texturen geladen. Falls der <c>TextureManager</c> noch nicht im
    /// <c>ServiceLocator</c> sitzt, wird die Init beim naechsten Aufruf
    /// erneut versucht — kein Hard-Fail, kein Timer.
    /// </para>
    /// </remarks>
    public static class CursorService
    {
        private static Texture2D s_DefaultCursor;
        private static Texture2D s_AttackCursor;
        private static Vector2 s_Hotspot;
        private static bool s_Initialized;
        private static bool s_AttackActive;

        /// <summary>
        /// Setzt den Cursor auf Attack- bzw. Default-Variante. Idempotent —
        /// wiederholte Aufrufe mit gleichem Wert sind kostenfrei.
        /// </summary>
        public static void SetAttack(bool attack)
        {
            EnsureInitialized();
            if (s_AttackActive == attack && s_Initialized)
            {
                return;
            }
            s_AttackActive = attack;
            Apply();
        }

        /// <summary>
        /// Erzwingt das Neuladen der Cursor-Texturen aus <see cref="HudConfig"/>
        /// und wendet den aktuellen Zustand sofort an. Nuetzlich nach
        /// <see cref="HudConfigLoader.ResetCacheForTesting"/> oder einem
        /// JSON-Reload zur Laufzeit.
        /// </summary>
        public static void Reload()
        {
            s_Initialized = false;
            s_DefaultCursor = null;
            s_AttackCursor = null;
            EnsureInitialized();
            Apply();
        }

        /// <summary>
        /// Setzt den Cursor explizit auf System-Default zurueck (z. B. beim
        /// Verlassen einer Szene). Folgt nicht dem Attack-State.
        /// </summary>
        public static void ResetToSystem()
        {
            s_AttackActive = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private static void EnsureInitialized()
        {
            if (s_Initialized)
            {
                return;
            }
            HudConfig cfg = HudConfigLoader.Load();
            Texture2D defaultSrc = HudConfigLoader.LoadTextureOrNull(cfg.defaultCursorTexture);
            Texture2D attackSrc = HudConfigLoader.LoadTextureOrNull(cfg.attackCursorTexture);

            float scale = cfg.cursorScale <= 0f ? 1f : cfg.cursorScale;
            s_DefaultCursor = ResizeIfNeeded(defaultSrc, scale);
            s_AttackCursor = ResizeIfNeeded(attackSrc, scale);
            s_Hotspot = new Vector2(cfg.cursorHotspotX * scale, cfg.cursorHotspotY * scale);

            // Nur als "initialisiert" markieren, wenn mindestens der Default-Cursor
            // geladen wurde. Sonst beim naechsten SetAttack erneut versuchen
            // (z. B. wenn TextureManager beim ersten Aufruf noch nicht im
            // ServiceLocator registriert war).
            s_Initialized = s_DefaultCursor != null;
        }

        /// <summary>
        /// Erzeugt eine skalierte Kopie der Quell-Textur. Funktioniert ohne
        /// <c>isReadable</c>, weil ueber einen RenderTexture-Blit kopiert wird.
        /// Bei <paramref name="scale"/> = 1 wird die Original-Textur zurueckgegeben.
        /// </summary>
        private static Texture2D ResizeIfNeeded(Texture2D source, float scale)
        {
            if (source == null)
            {
                return null;
            }
            if (Mathf.Approximately(scale, 1f))
            {
                return source;
            }

            int newWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int newHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            rt.filterMode = FilterMode.Bilinear;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                Texture2D scaled = new(newWidth, newHeight, TextureFormat.RGBA32, false, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = source.name + "_Scaled"
                };
                scaled.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
                scaled.Apply(false, false);
                return scaled;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Apply()
        {
            Texture2D tex = s_AttackActive ? s_AttackCursor : s_DefaultCursor;
            if (tex == null)
            {
                // Fallback: System-Cursor verwenden.
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            // ForceSoftware ist Pflicht, damit Unity die Cursor-Textur in ihrer
            // tatsaechlichen Pixelgroesse rendert. CursorMode.Auto delegiert an
            // den Hardware-Cursor von Windows, der die Groesse vom OS uebernimmt
            // und damit unsere Skalierung (cursorScale) sichtbar ignoriert.
            Cursor.SetCursor(tex, s_Hotspot, CursorMode.ForceSoftware);
        }
    }
}
