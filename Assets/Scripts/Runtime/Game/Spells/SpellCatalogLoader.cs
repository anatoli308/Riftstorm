using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Synchroner Lade-Utility fuer Spell-Templates aus
    /// <c>StreamingAssets/spells/_templates.json</c>. Cached prozessweit
    /// (Lazy-Static-Cache), damit jeder Caster ohne IO arbeitet.
    /// </summary>
    /// <remarks>
    /// Pattern identisch zu <see cref="Riftstorm.Game.Npc.NpcCatalogLoader"/>:
    /// bewusst static und ohne ServiceLocator (KISS), Daten unveraenderlich
    /// pro Prozess. Bei fehlender Datei wird ein leeres Dictionary gecached
    /// und Lookups liefern <c>null</c>.
    /// </remarks>
    public static class SpellCatalogLoader
    {
        /// <summary>Unterordner unter <c>Application.streamingAssetsPath</c>.</summary>
        public const string SubFolder = "spells";

        /// <summary>Dateiname der Template-Tabelle.</summary>
        public const string TemplatesFileName = "_templates.json";

        private static Dictionary<int, SpellTemplate> s_Templates;
        private static bool s_LoadAttempted;

        // ---- Templates --------------------------------------------------

        /// <summary>
        /// Liefert das Spell-Template mit <paramref name="entry"/>, oder
        /// <c>null</c>, wenn es nicht existiert.
        /// </summary>
        public static SpellTemplate GetTemplateOrNull(int entry)
        {
            EnsureLoaded();
            return s_Templates != null && s_Templates.TryGetValue(entry, out SpellTemplate t) ? t : null;
        }

        /// <summary>
        /// Versucht den Spell mit <paramref name="entry"/> zu laden.
        /// </summary>
        public static bool TryGetTemplate(int entry, out SpellTemplate template)
        {
            EnsureLoaded();
            if (s_Templates != null && s_Templates.TryGetValue(entry, out template))
            {
                return true;
            }
            template = null;
            return false;
        }

        /// <summary>Gesamt-Tabelle (read-only Lookup); nie <c>null</c>.</summary>
        public static IReadOnlyDictionary<int, SpellTemplate> AllTemplates
        {
            get
            {
                EnsureLoaded();
                return s_Templates;
            }
        }

        // ---- Cache-Reset (Editor / Tests) ------------------------------

        /// <summary>Setzt den Cache zurueck. Fuer Tests oder Editor-Reload.</summary>
        public static void ResetCacheForTesting()
        {
            s_Templates = null;
            s_LoadAttempted = false;
        }

        // ---- Internals --------------------------------------------------

        private static void EnsureLoaded()
        {
            if (s_LoadAttempted)
            {
                return;
            }
            s_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, SubFolder, TemplatesFileName);
            s_Templates = LoadDictionary(path);
        }

        /// <summary>
        /// Reader fuer die <c>{ "1": {...}, "2": {...} }</c>-Form des
        /// Migrator-Exports (<c>Tools/Scripts/migrate_game_db.py</c>).
        /// Liefert bei Fehlern ein leeres Dictionary statt <c>null</c>,
        /// damit Konsumenten keinen Null-Guard brauchen.
        /// </summary>
        private static Dictionary<int, SpellTemplate> LoadDictionary(string path)
        {
            const string LogTag = "SpellCatalogLoader";

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[{LogTag}] Datei fehlt: {path}");
                return new Dictionary<int, SpellTemplate>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<int, SpellTemplate> result =
                    JsonConvert.DeserializeObject<Dictionary<int, SpellTemplate>>(json)
                    ?? new Dictionary<int, SpellTemplate>();
                Debug.Log($"[{LogTag}] {result.Count} Eintraege geladen aus {path}");
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{LogTag}] Fehler beim Laden von {path}: {ex.Message}");
                return new Dictionary<int, SpellTemplate>();
            }
        }
    }
}
