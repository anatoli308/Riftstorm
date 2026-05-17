using System.IO;
using Newtonsoft.Json;
using Riftstorm.Management.TextureManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using UnityEngine;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="HudConfig"/> aus
    /// <c>StreamingAssets/interface/hud_config.json</c>. Cached den Config
    /// prozessweit, sodass mehrere HUD-Views (Player + Target) nur einmal IO machen.
    /// </summary>
    /// <remarks>
    /// Textur-Loading delegiert an den <see cref="TextureManager"/>-Pure-Service.
    /// Die JSON-Keys sind relative Pfade unter <c>Application.dataPath/Art</c>
    /// ohne Extension (z. B. <c>"interface/unit_frame"</c>).
    /// </remarks>
    public static class HudConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "hud_config.json";

        private static HudConfig s_Cached;
        private static bool s_LoadAttempted;

        /// <summary>
        /// Liefert den geladenen Config oder <c>null</c>, wenn die Datei fehlt
        /// bzw. das JSON kaputt ist (dann greifen die SerializeField-Defaults).
        /// </summary>
        public static HudConfig LoadOrNull()
        {
            if (s_LoadAttempted)
            {
                return s_Cached;
            }
            s_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[HudConfigLoader] Kein HUD-Config gefunden ({path}) - Inspector-Defaults aktiv.");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                s_Cached = JsonConvert.DeserializeObject<HudConfig>(json);
                if (s_Cached != null)
                {
                    Debug.Log($"[HudConfigLoader] HUD-Config geladen: {path}");
                }
                return s_Cached;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HudConfigLoader] Fehler beim Laden von {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolved einen Textur-Key (z. B. <c>"interface/unit_frame"</c>) via
        /// <see cref="TextureManager"/>. Liefert <c>null</c>, wenn der Key leer
        /// ist, der TextureManager nicht registriert ist oder die Datei fehlt.
        /// </summary>
        public static Texture2D LoadTextureOrNull(string textureKey)
        {
            if (string.IsNullOrWhiteSpace(textureKey))
            {
                return null;
            }

            TextureManager manager = ServiceLocator.Get<TextureManager>();
            if (manager == null)
            {
                Debug.LogWarning($"[HudConfigLoader] TextureManager nicht im ServiceLocator (Key '{textureKey}').");
                return null;
            }

            Texture2D tex = manager.GetTexture(textureKey);
            if (tex == null)
            {
                Debug.LogWarning($"[HudConfigLoader] HUD-Textur nicht gefunden: '{textureKey}'");
            }
            return tex;
        }

        /// <summary>
        /// Setzt den Config-Cache + Load-Attempt zurueck. Fuer Tests oder ein
        /// "Reload-Button"-Feature im Editor. Texturen werden vom
        /// <see cref="TextureManager"/> verwaltet und hier nicht beruehrt.
        /// </summary>
        public static void ResetCacheForTesting()
        {
            s_Cached = null;
            s_LoadAttempted = false;
        }
    }
}
