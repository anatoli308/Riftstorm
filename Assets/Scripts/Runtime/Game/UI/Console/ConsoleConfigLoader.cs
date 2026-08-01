using Riftstorm.Core.Configuration;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// Synchroner Lade-Helfer fuer <see cref="ConsoleConfig"/> aus
    /// <c>StreamingAssets/interface/console_config.json</c>. Caching delegiert
    /// an <see cref="JsonConfigFile{T}"/>.
    /// </summary>
    public static class ConsoleConfigLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "interface";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "console_config.json";

        private static readonly JsonConfigFile<ConsoleConfig> s_Config =
            new("ConsoleConfigLoader", DefaultFileName, DefaultSubFolder);

        /// <summary>Liefert den geladenen Config (nie <c>null</c>, Default-Fallback).</summary>
        public static ConsoleConfig Load() => s_Config.Load();

        /// <summary>Setzt den Config-Cache zurueck (Tests / Editor-Reload).</summary>
        public static void ResetCacheForTesting() => s_Config.ResetCache();
    }
}
