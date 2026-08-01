using Riftstorm.Core.Configuration;
using UnityEngine;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="HudConfig"/> aus
    /// <c>StreamingAssets/interface/hud_config.json</c>. Caching delegiert an
    /// <see cref="JsonConfigFile{T}"/>, Textur-Lookups an
    /// <see cref="ConfigTextureResolver"/>.
    /// </summary>
    public static class HudConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "hud_config.json";

        private static readonly JsonConfigFile<HudConfig> s_Config =
            new("HudConfigLoader", DefaultFileName, DefaultSubFolder);

        /// <summary>Liefert den geladenen Config (nie <c>null</c>, Default-Fallback).</summary>
        public static HudConfig Load() => s_Config.Load();

        /// <summary>Resolved einen HUD-Textur-Key ueber den TextureManager.</summary>
        public static Texture2D LoadTextureOrNull(string textureKey) =>
            ConfigTextureResolver.LoadTextureOrNull("HudConfigLoader", textureKey);

        /// <summary>Setzt den Config-Cache zurueck (Tests / Editor-Reload).</summary>
        public static void ResetCacheForTesting() => s_Config.ResetCache();
    }
}
