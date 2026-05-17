using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.ApplicationLifecycle.UI
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="UIFontConfig"/> aus
    /// <c>StreamingAssets/interface/ui_fonts.json</c>. Cached den Config
    /// prozessweit, sodass mehrere Konsumenten (HUD, Metagame, Tooltips) nur
    /// einmal IO machen. Mirror von <see cref="HudConfigLoader"/>.
    /// </summary>
    /// <remarks>
    /// Faellt bei fehlender Datei oder kaputtem JSON auf einen frischen
    /// <see cref="UIFontConfig"/> mit Default-Werten zurueck — Konsumenten
    /// brauchen keinen eigenen Fallback-Pfad.
    /// </remarks>
    public static class UIFontConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";

        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "ui_fonts.json";

        static UIFontConfig s_Cached;
        static bool s_LoadAttempted;

        /// <summary>
        /// Liefert den geladenen Config, oder einen frischen <see cref="UIFontConfig"/>
        /// mit Default-Werten, falls die Datei fehlt oder das JSON kaputt ist.
        /// Der Rueckgabewert ist nie <c>null</c>.
        /// </summary>
        public static UIFontConfig Load()
        {
            if (s_LoadAttempted)
            {
                return s_Cached;
            }
            s_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[UIFontConfigLoader] Kein UI-Font-Config gefunden ({path}) — Defaults aktiv.");
                s_Cached = new UIFontConfig();
                return s_Cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                s_Cached = JsonConvert.DeserializeObject<UIFontConfig>(json) ?? new UIFontConfig();
                Debug.Log($"[UIFontConfigLoader] UI-Font-Config geladen: {path}");
                return s_Cached;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UIFontConfigLoader] Fehler beim Laden von {path}: {ex.Message}");
                s_Cached = new UIFontConfig();
                return s_Cached;
            }
        }

        /// <summary>
        /// Setzt den Config-Cache + Load-Attempt zurueck. Fuer Tests oder ein
        /// "Reload-Button"-Feature im Editor.
        /// </summary>
        public static void ResetCacheForTesting()
        {
            s_Cached = null;
            s_LoadAttempted = false;
        }
    }
}
