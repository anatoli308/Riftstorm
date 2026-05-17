using System.IO;
using Newtonsoft.Json;
using Riftstorm.Management.TextureManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using UnityEngine;

namespace Riftstorm.Game.Player
{
    /// <summary>
    /// Synchrones Lade-Utility f&#252;r <see cref="NameTagConfig"/> aus
    /// <c>StreamingAssets/interface/nametag_config.json</c>. Cached den Config
    /// prozessweit (1:1-Mirror von <c>HudConfigLoader</c>).
    /// </summary>
    /// <remarks>
    /// Textur-Loading delegiert an den <see cref="TextureManager"/>-Pure-Service.
    /// JSON-Keys sind relative Pfade unter <c>Application.dataPath/Art</c> ohne
    /// Extension (z. B. <c>"interface/generic_highlight_hover"</c>).
    /// </remarks>
    public static class NameTagConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";

        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "nametag_config.json";

        private static NameTagConfig s_Cached;
        private static bool s_LoadAttempted;

        /// <summary>
        /// Liefert den geladenen Config oder einen frischen <see cref="NameTagConfig"/>
        /// mit Default-Werten, falls die Datei fehlt oder das JSON kaputt ist. Der
        /// R&#252;ckgabewert ist nie <c>null</c>.
        /// </summary>
        public static NameTagConfig Load()
        {
            if (s_LoadAttempted)
            {
                return s_Cached;
            }
            s_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[NameTagConfigLoader] Kein NameTag-Config gefunden ({path}) - Defaults aktiv.");
                s_Cached = new NameTagConfig();
                return s_Cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                s_Cached = JsonConvert.DeserializeObject<NameTagConfig>(json) ?? new NameTagConfig();
                Debug.Log($"[NameTagConfigLoader] NameTag-Config geladen: {path}");
                return s_Cached;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NameTagConfigLoader] Fehler beim Laden von {path}: {ex.Message}");
                s_Cached = new NameTagConfig();
                return s_Cached;
            }
        }

        /// <summary>
        /// Resolved einen Textur-Key (z. B. <c>"interface/generic_highlight_hover"</c>)
        /// via <see cref="TextureManager"/>. Liefert <c>null</c>, wenn der Key leer ist,
        /// der TextureManager nicht registriert ist oder die Datei fehlt.
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
                Debug.LogWarning($"[NameTagConfigLoader] TextureManager nicht im ServiceLocator (Key '{textureKey}').");
                return null;
            }

            Texture2D tex = manager.GetTexture(textureKey);
            if (tex == null)
            {
                Debug.LogWarning($"[NameTagConfigLoader] NameTag-Textur nicht gefunden: '{textureKey}'");
            }
            return tex;
        }

        /// <summary>
        /// Setzt den Config-Cache + Load-Attempt zur&#252;ck. F&#252;r Tests oder ein
        /// "Reload"-Feature im Editor.
        /// </summary>
        public static void ResetCacheForTesting()
        {
            s_Cached = null;
            s_LoadAttempted = false;
        }
    }
}
