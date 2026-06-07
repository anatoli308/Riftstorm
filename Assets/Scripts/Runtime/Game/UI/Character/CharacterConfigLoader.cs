using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Riftstorm.Management.TextureManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using UnityEngine;

namespace Riftstorm.Game.UI.Character
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="CharacterConfig"/> aus
    /// <c>StreamingAssets/interface/character_config.json</c>. Cached den
    /// Config prozessweit. Spiegelt das Pattern von
    /// <see cref="Inventory.InventoryConfigLoader"/>.
    /// </summary>
    public static class CharacterConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "character_config.json";

        private static CharacterConfig s_Cached;
        private static bool s_LoadAttempted;

        /// <summary>
        /// Liefert den geladenen Config, oder einen frischen
        /// <see cref="CharacterConfig"/> mit Default-Werten, falls die Datei
        /// fehlt oder das JSON kaputt ist. Nie <c>null</c>.
        /// </summary>
        public static CharacterConfig Load()
        {
            if (s_LoadAttempted)
            {
                return s_Cached;
            }
            s_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[CharacterConfigLoader] Kein Character-Config gefunden ({path}) - Defaults aktiv.");
                s_Cached = new();
                return s_Cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                // StringEnumConverter erlaubt EquipSlot-Eintraege als Strings
                // ("Helm", "Amulet", ...) im JSON statt nackter Integer.
                JsonSerializerSettings settings = new();
                settings.Converters.Add(new StringEnumConverter());
                s_Cached = JsonConvert.DeserializeObject<CharacterConfig>(json, settings) ?? new CharacterConfig();
                Debug.Log($"[CharacterConfigLoader] Character-Config geladen: {path}");
                return s_Cached;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CharacterConfigLoader] Fehler beim Laden von {path}: {ex.Message}");
                s_Cached = new();
                return s_Cached;
            }
        }

        /// <summary>
        /// Resolved einen Textur-Key via <see cref="TextureManager"/>. Liefert
        /// <c>null</c>, wenn der Key leer ist, der TextureManager fehlt oder
        /// die Datei nicht existiert.
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
                Debug.LogWarning($"[CharacterConfigLoader] TextureManager nicht im ServiceLocator (Key '{textureKey}').");
                return null;
            }

            Texture2D tex = manager.GetTexture(textureKey);
            if (tex == null)
            {
                Debug.LogWarning($"[CharacterConfigLoader] Textur nicht gefunden: '{textureKey}'");
            }
            return tex;
        }

        /// <summary>
        /// Loest den Icon-Key fuer ein Item-Template auf und laedt die Textur.
        /// Liefert <c>null</c> bei leerem Icon-Feld.
        /// </summary>
        public static Texture2D LoadItemIconOrNull(string iconFileName, string keyPrefix)
        {
            if (string.IsNullOrWhiteSpace(iconFileName))
            {
                return null;
            }
            string baseName = Path.GetFileNameWithoutExtension(iconFileName);
            string key = string.IsNullOrEmpty(keyPrefix) ? baseName : keyPrefix + baseName;
            return LoadTextureOrNull(key);
        }

        /// <summary>
        /// Setzt den Config-Cache zurueck. Fuer Tests oder ein Editor-Reload.
        /// </summary>
        public static void ResetCacheForTesting()
        {
            s_Cached = null;
            s_LoadAttempted = false;
        }
    }
}
