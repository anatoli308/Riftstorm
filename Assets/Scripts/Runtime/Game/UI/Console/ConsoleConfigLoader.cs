using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// Synchroner Lade-Helfer fuer <see cref="ConsoleConfig"/> aus
    /// <c>StreamingAssets/interface/console_config.json</c>. Mirror von
    /// <see cref="HudConfigLoader"/>: Lazy-Static-Cache + Default-Fallback.
    /// </summary>
    public static class ConsoleConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "console_config.json";

        private static ConsoleConfig s_Cached;
        private static bool s_LoadAttempted;

        /// <summary>
        /// Liefert den geladenen Config oder einen frischen <see cref="ConsoleConfig"/>
        /// mit Default-Werten, falls die Datei fehlt oder das JSON kaputt ist.
        /// Nie <c>null</c>.
        /// </summary>
        public static ConsoleConfig Load()
        {
            if (s_LoadAttempted)
            {
                return s_Cached;
            }
            s_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[ConsoleConfigLoader] Kein Console-Config gefunden ({path}) - ConsoleConfig-Defaults aktiv.");
                s_Cached = new();
                return s_Cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                s_Cached = JsonConvert.DeserializeObject<ConsoleConfig>(json) ?? new ConsoleConfig();
                Debug.Log($"[ConsoleConfigLoader] Console-Config geladen: {path}");
                return s_Cached;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ConsoleConfigLoader] Fehler beim Laden von {path}: {ex.Message}");
                s_Cached = new();
                return s_Cached;
            }
        }

        /// <summary>Setzt den Cache zurueck (Editor-Reload-Tooling, Tests).</summary>
        public static void ResetCacheForTesting()
        {
            s_Cached = null;
            s_LoadAttempted = false;
        }
    }
}
