using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Riftstorm.Core.Configuration;
using UnityEngine;

namespace Riftstorm.Game.UI.Character
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="CharacterConfig"/> aus
    /// <c>StreamingAssets/interface/character_config.json</c>. Caching delegiert
    /// an <see cref="JsonConfigFile{T}"/> (mit Enum-Converter), Textur-Lookups
    /// an <see cref="ConfigTextureResolver"/>.
    /// </summary>
    public static class CharacterConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "character_config.json";

        private static readonly JsonConfigFile<CharacterConfig> s_Config = CreateConfig();

        // StringEnumConverter erlaubt EquipSlot-Eintraege als Strings
        // ("Helm", "Amulet", ...) im JSON statt nackter Integer.
        private static JsonConfigFile<CharacterConfig> CreateConfig()
        {
            JsonSerializerSettings settings = new();
            settings.Converters.Add(new StringEnumConverter());
            return new JsonConfigFile<CharacterConfig>("CharacterConfigLoader", DefaultFileName, DefaultSubFolder, settings);
        }

        /// <summary>Liefert den geladenen Config (nie <c>null</c>, Default-Fallback).</summary>
        public static CharacterConfig Load() => s_Config.Load();

        /// <summary>Resolved einen Textur-Key ueber den TextureManager.</summary>
        public static Texture2D LoadTextureOrNull(string textureKey) =>
            ConfigTextureResolver.LoadTextureOrNull("CharacterConfigLoader", textureKey);

        /// <summary>Loest den Icon-Key fuer ein Item-Template auf und laedt die Textur.</summary>
        public static Texture2D LoadItemIconOrNull(string iconFileName, string keyPrefix) =>
            ConfigTextureResolver.LoadItemIconOrNull("CharacterConfigLoader", iconFileName, keyPrefix);

        /// <summary>Setzt den Config-Cache zurueck (Tests / Editor-Reload).</summary>
        public static void ResetCacheForTesting() => s_Config.ResetCache();
    }
}
