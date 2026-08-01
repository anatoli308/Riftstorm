using Riftstorm.Core.Configuration;

namespace Riftstorm.Management.FontManagement
{
    /// <summary>
    /// Synchrones Lade-Utility fuer <see cref="UIFontConfig"/> aus
    /// <c>StreamingAssets/interface/ui_fonts.json</c>. Caching delegiert an
    /// <see cref="JsonConfigFile{T}"/>.
    /// </summary>
    public static class UIFontConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";

        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "ui_fonts.json";

        private static readonly JsonConfigFile<UIFontConfig> s_Config =
            new("UIFontConfigLoader", DefaultFileName, DefaultSubFolder);

        /// <summary>Liefert den geladenen Config (nie <c>null</c>, Default-Fallback).</summary>
        public static UIFontConfig Load() => s_Config.Load();

        /// <summary>Setzt den Config-Cache zurueck (Tests / Editor-Reload).</summary>
        public static void ResetCacheForTesting() => s_Config.ResetCache();
    }
}
