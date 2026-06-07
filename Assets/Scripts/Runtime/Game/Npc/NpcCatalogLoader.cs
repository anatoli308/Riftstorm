using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Synchroner Lade-Utility fuer NPC-Templates und NPC-Models aus
    /// <c>StreamingAssets/npc/_templates.json</c> und
    /// <c>StreamingAssets/npc/_models.json</c>. Cached prozessweit
    /// (Lazy-Static-Cache), damit jeder Spawner ohne IO arbeitet.
    /// </summary>
    /// <remarks>
    /// Bewusst static und ohne ServiceLocator-Registrierung (KISS): die
    /// Daten sind unveraenderlich pro Prozess und werden ausschliesslich
    /// per Template-ID / Model-ID gelesen. Bei fehlender Datei werden
    /// leere Dictionaries gecacht und alle Lookups liefern <c>null</c>.
    /// Pattern entspricht <see cref="UI.HudConfigLoader"/>.
    /// </remarks>
    public static class NpcCatalogLoader
    {
        /// <summary>Unterordner unter <c>Application.streamingAssetsPath</c>.</summary>
        public const string SubFolder = "npc";

        /// <summary>Dateiname der Template-Tabelle.</summary>
        public const string TemplatesFileName = "_templates.json";

        /// <summary>Dateiname der Models-Tabelle.</summary>
        public const string ModelsFileName = "_models.json";

        private static Dictionary<int, NpcTemplate> s_Templates;
        private static Dictionary<int, NpcModel> s_Models;
        private static bool s_TemplatesLoadAttempted;
        private static bool s_ModelsLoadAttempted;

        // ---- Templates --------------------------------------------------

        /// <summary>
        /// Liefert das Template mit <paramref name="entry"/>, oder <c>null</c>,
        /// wenn es nicht existiert.
        /// </summary>
        public static NpcTemplate GetTemplateOrNull(int entry)
        {
            EnsureTemplatesLoaded();
            return s_Templates != null && s_Templates.TryGetValue(entry, out NpcTemplate t) ? t : null;
        }

        /// <summary>Gesamt-Tabelle (read-only Lookup); nie <c>null</c>.</summary>
        public static IReadOnlyDictionary<int, NpcTemplate> AllTemplates
        {
            get
            {
                EnsureTemplatesLoaded();
                return s_Templates;
            }
        }

        // ---- Models -----------------------------------------------------

        /// <summary>
        /// Liefert das Model mit <paramref name="id"/>, oder <c>null</c>,
        /// wenn es nicht existiert.
        /// </summary>
        public static NpcModel GetModelOrNull(int id)
        {
            EnsureModelsLoaded();
            return s_Models != null && s_Models.TryGetValue(id, out NpcModel m) ? m : null;
        }

        /// <summary>Gesamt-Tabelle (read-only Lookup); nie <c>null</c>.</summary>
        public static IReadOnlyDictionary<int, NpcModel> AllModels
        {
            get
            {
                EnsureModelsLoaded();
                return s_Models;
            }
        }

        // ---- Cache-Reset (Editor / Tests) ------------------------------

        /// <summary>Setzt beide Caches zurueck. Fuer Tests oder Editor-Reload.</summary>
        public static void ResetCacheForTesting()
        {
            s_Templates = null;
            s_Models = null;
            s_TemplatesLoadAttempted = false;
            s_ModelsLoadAttempted = false;
        }

        // ---- Internals --------------------------------------------------

        private static void EnsureTemplatesLoaded()
        {
            if (s_TemplatesLoadAttempted)
            {
                return;
            }
            s_TemplatesLoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, SubFolder, TemplatesFileName);
            s_Templates = LoadDictionary<NpcTemplate>(path, "NpcCatalogLoader/Templates");
        }

        private static void EnsureModelsLoaded()
        {
            if (s_ModelsLoadAttempted)
            {
                return;
            }
            s_ModelsLoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, SubFolder, ModelsFileName);
            s_Models = LoadDictionary<NpcModel>(path, "NpcCatalogLoader/Models");
        }

        /// <summary>
        /// Generischer Reader fuer die <c>{ "1": {...}, "2": {...} }</c>-Form,
        /// die der Migrator (<c>Tools/Scripts/migrate_game_db.py</c>) fuer
        /// Tabellen mit Single-PK schreibt. Liefert bei Fehlern ein leeres
        /// Dictionary statt <c>null</c>, damit Konsumenten keinen Null-Guard
        /// brauchen.
        /// </summary>
        private static Dictionary<int, T> LoadDictionary<T>(string path, string logTag)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[{logTag}] Datei fehlt: {path}");
                return new Dictionary<int, T>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<int, T> result =
                    JsonConvert.DeserializeObject<Dictionary<int, T>>(json) ?? new Dictionary<int, T>();
                Debug.Log($"[{logTag}] {result.Count} Eintraege geladen aus {path}");
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{logTag}] Fehler beim Laden von {path}: {ex.Message}");
                return new Dictionary<int, T>();
            }
        }
    }
}
