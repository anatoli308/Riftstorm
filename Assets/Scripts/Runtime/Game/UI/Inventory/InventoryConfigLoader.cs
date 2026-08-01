using Riftstorm.Core.Configuration;
using UnityEngine;

namespace Riftstorm.Game.UI.Inventory
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="InventoryConfig"/> aus
    /// <c>StreamingAssets/interface/inventory_config.json</c>. Caching delegiert
    /// an <see cref="JsonConfigFile{T}"/>, Textur-Lookups an
    /// <see cref="ConfigTextureResolver"/>.
    /// </summary>
    public static class InventoryConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "inventory_config.json";

        private static readonly JsonConfigFile<InventoryConfig> s_Config =
            new("InventoryConfigLoader", DefaultFileName, DefaultSubFolder);

        /// <summary>Liefert den geladenen Config (nie <c>null</c>, Default-Fallback).</summary>
        public static InventoryConfig Load() => s_Config.Load();

        /// <summary>Resolved einen Textur-Key ueber den TextureManager.</summary>
        public static Texture2D LoadTextureOrNull(string textureKey) =>
            ConfigTextureResolver.LoadTextureOrNull("InventoryConfigLoader", textureKey);

        /// <summary>Loest den Icon-Key fuer ein Item-Template auf und laedt die Textur.</summary>
        public static Texture2D LoadItemIconOrNull(string iconFileName, string keyPrefix) =>
            ConfigTextureResolver.LoadItemIconOrNull("InventoryConfigLoader", iconFileName, keyPrefix);

        /// <summary>Setzt den Config-Cache zurueck (Tests / Editor-Reload).</summary>
        public static void ResetCacheForTesting() => s_Config.ResetCache();
    }
}
