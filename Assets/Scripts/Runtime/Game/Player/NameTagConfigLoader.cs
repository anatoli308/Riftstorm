using Riftstorm.Core.Configuration;
using Riftstorm.Game.UI;
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

        private static readonly JsonConfigFile<NameTagConfig> s_Config =
            new("NameTagConfigLoader", DefaultFileName, DefaultSubFolder);

        /// <summary>
        /// Liefert den geladenen Config oder einen frischen <see cref="NameTagConfig"/>
        /// mit Default-Werten, falls die Datei fehlt oder das JSON kaputt ist. Der
        /// R&#252;ckgabewert ist nie <c>null</c>.
        /// </summary>
        public static NameTagConfig Load() => s_Config.Load();

        /// <summary>
        /// Resolved einen Textur-Key (z. B. <c>"interface/generic_highlight_hover"</c>)
        /// via <see cref="TextureManager"/>. Liefert <c>null</c>, wenn der Key leer ist,
        /// der TextureManager nicht registriert ist oder die Datei fehlt.
        /// </summary>
        public static Texture2D LoadTextureOrNull(string textureKey) =>
            ConfigTextureResolver.LoadTextureOrNull("NameTagConfigLoader", textureKey);

        /// <summary>
        /// Setzt den Config-Cache + Load-Attempt zur&#252;ck. F&#252;r Tests oder ein
        /// "Reload"-Feature im Editor.
        /// </summary>
        public static void ResetCacheForTesting() => s_Config.ResetCache();
    }
}
